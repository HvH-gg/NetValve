using System.Diagnostics;
using NetValve.Core.Validation;
using Microsoft.Extensions.Logging;
using NetValve.Core.Models;

namespace NetValve.Core;

public class TcCommands
{
    // Minor IDs double as netem qdisc majors, so 1 (root on dev), 2 (root on ifb)
    // and 0xFFFF (ingress on dev) are reserved. Class minor 1 is the default class.
    private const int FirstMinorId = 3;
    private const int LastMinorId = 0xFFFE;

    private readonly ServerConfig _config;
    private readonly ILogger _logger;
    private readonly object _minorIdLock = new();
    private readonly Dictionary<string, int> _minorIds = new();
    private readonly Queue<int> _freeMinorIds = new();
    private int _nextMinorId = FirstMinorId;
    private bool _baseSetupReady;

    public TcCommands(ServerConfig config, ILogger logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<bool> EnsureBaseSetupAsync(CancellationToken ct)
    {
        if (!TryGetValidatedDevices(out var dev, out var ifb))
        {
            _baseSetupReady = false;
            return false;
        }

        var cmds = new[]
        {
            new CommandSpec("modprobe", ["sch_htb"]),
            new CommandSpec("modprobe", ["sch_netem"]),
            new CommandSpec("modprobe", ["sch_ingress"]),
            new CommandSpec("modprobe", ["cls_flower"]),
            new CommandSpec("modprobe", ["act_mirred"]),
            new CommandSpec("modprobe", ["ifb"]),
            new CommandSpec("ip", ["link", "add", ifb, "type", "ifb"], IsAlreadyExistsError),
            new CommandSpec("ip", ["link", "set", "dev", ifb, "up"]),
            new CommandSpec("tc", ["qdisc", "del", "dev", dev, "ingress"], IsIgnorableDeleteError),
            new CommandSpec("tc", ["qdisc", "del", "dev", dev, "root"], IsIgnorableDeleteError),
            new CommandSpec("tc", ["qdisc", "del", "dev", ifb, "root"], IsIgnorableDeleteError),
            new CommandSpec("tc", ["qdisc", "add", "dev", dev, "root", "handle", "1:", "htb", "default", "1"]),
            new CommandSpec("tc", ["class", "replace", "dev", dev, "parent", "1:", "classid", "1:1", "htb", "rate", "1gbit", "ceil", "1gbit"]),
            new CommandSpec("tc", ["qdisc", "add", "dev", ifb, "root", "handle", "2:", "htb", "default", "1"]),
            new CommandSpec("tc", ["class", "replace", "dev", ifb, "parent", "2:", "classid", "2:1", "htb", "rate", "1gbit", "ceil", "1gbit"]),
            new CommandSpec("tc", ["qdisc", "add", "dev", dev, "ingress"]),
            new CommandSpec("tc", ["filter", "del", "dev", dev, "parent", "ffff:", "protocol", "ip", "pref", "1", "handle", "1", "flower"], IsIgnorableDeleteError),
            new CommandSpec("tc", ["filter", "add", "dev", dev, "parent", "ffff:", "protocol", "ip", "pref", "1", "handle", "1", "flower", "skip_hw", "ip_proto", "udp", "action", "mirred", "egress", "redirect", "dev", ifb])
        };

        var success = await ExecuteCommandsAsync(cmds, ct);
        _baseSetupReady = success;

        if (success)
        {
            _logger.LogInformation("TC base setup ready on {Device} with IFB {IfbDevice}", dev, ifb);
        }
        else
        {
            _logger.LogWarning("TC base setup is incomplete; shaping commands will be retried");
        }

        return success;
    }

    public async Task<bool> ApplyShapingAsync(string ip, uint port, int delayMs, CancellationToken ct)
    {
        if (!_baseSetupReady && !await EnsureBaseSetupAsync(ct))
            return false;

        if (!TryGetValidatedDevices(out var dev, out var ifb)
            || !TryNormalizeShapingTarget(ip, port, out var normalizedIp)
            || delayMs < 0)
        {
            return false;
        }

        if (!TryAcquireMinorId(normalizedIp, port, out var minor))
        {
            _logger.LogError("No free TC class IDs left; skipping shaping for port {Port}", port);
            return false;
        }

        var filterHandle = minor.ToString();

        var cmds = new[]
        {
            new CommandSpec("tc", ["class", "replace", "dev", dev, "parent", "1:1", "classid", $"1:{minor:x}", "htb", "rate", "1gbit", "ceil", "1gbit"]),
            new CommandSpec("tc", ["qdisc", "replace", "dev", dev, "parent", $"1:{minor:x}", "handle", $"{minor:x}:", "netem", "delay", $"{delayMs}ms"]),
            new CommandSpec("tc", ["filter", "replace", "dev", dev, "protocol", "ip", "parent", "1:", "pref", "10", "handle", filterHandle, "flower", "skip_hw", "dst_ip", normalizedIp, "ip_proto", "udp", "src_port", port.ToString(), "flowid", $"1:{minor:x}"]),
            new CommandSpec("tc", ["class", "replace", "dev", ifb, "parent", "2:1", "classid", $"2:{minor:x}", "htb", "rate", "1gbit", "ceil", "1gbit"]),
            new CommandSpec("tc", ["qdisc", "replace", "dev", ifb, "parent", $"2:{minor:x}", "handle", $"{minor:x}:", "netem", "delay", $"{delayMs}ms"]),
            new CommandSpec("tc", ["filter", "replace", "dev", ifb, "protocol", "ip", "parent", "2:", "pref", "10", "handle", filterHandle, "flower", "skip_hw", "src_ip", normalizedIp, "ip_proto", "udp", "dst_port", port.ToString(), "flowid", $"2:{minor:x}"])
        };

        return await ExecuteCommandsAsync(cmds, ct);
    }

    public async Task<bool> RemoveShapingAsync(string ip, uint port, CancellationToken ct)
    {
        if (!_baseSetupReady && !await EnsureBaseSetupAsync(ct))
            return false;

        if (!TryGetValidatedDevices(out var dev, out var ifb) || !TryNormalizeShapingTarget(ip, port, out var normalizedIp))
            return false;

        if (!TryReleaseMinorId(normalizedIp, port, out var minor))
            return true;

        var filterHandle = minor.ToString();

        var cmds = new[]
        {
            new CommandSpec("tc", ["filter", "del", "dev", dev, "parent", "1:", "protocol", "ip", "pref", "10", "handle", filterHandle, "flower"], IsIgnorableDeleteError),
            new CommandSpec("tc", ["qdisc", "del", "dev", dev, "parent", $"1:{minor:x}"], IsIgnorableDeleteError),
            new CommandSpec("tc", ["class", "del", "dev", dev, "classid", $"1:{minor:x}"], IsIgnorableDeleteError),
            new CommandSpec("tc", ["filter", "del", "dev", ifb, "parent", "2:", "protocol", "ip", "pref", "10", "handle", filterHandle, "flower"], IsIgnorableDeleteError),
            new CommandSpec("tc", ["qdisc", "del", "dev", ifb, "parent", $"2:{minor:x}"], IsIgnorableDeleteError),
            new CommandSpec("tc", ["class", "del", "dev", ifb, "classid", $"2:{minor:x}"], IsIgnorableDeleteError)
        };

        return await ExecuteCommandsAsync(cmds, ct);
    }

    private async Task<bool> ExecuteCommandsAsync(IEnumerable<CommandSpec> cmds, CancellationToken ct)
    {
        var allSucceeded = true;

        foreach (var cmd in cmds)
        {
            var psi = new ProcessStartInfo
            {
                FileName = cmd.FileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            foreach (var arg in cmd.Arguments)
                psi.ArgumentList.Add(arg);

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                allSucceeded = false;
                _baseSetupReady = false;
                _logger.LogError("Failed to start process for TC command: {Command}", FormatCommand(cmd));
                continue;
            }

            var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);
            await stdoutTask;
            var stderr = await stderrTask;

            if (proc.ExitCode == 0 || cmd.IsExpectedFailure(proc.ExitCode, stderr))
                continue;

            allSucceeded = false;
            if (IsBaseSetupError(stderr))
                _baseSetupReady = false;

            _logger.LogError("TC command failed: {Command} - {Error}", FormatCommand(cmd), stderr.Trim());
        }

        return allSucceeded;
    }

    private bool TryGetValidatedDevices(out string dev, out string ifb)
    {
        dev = string.Empty;
        ifb = string.Empty;

        if (!InputValidator.TryNormalizeLinuxDeviceName(_config.NetworkInterface, out dev))
        {
            _logger.LogError("Invalid NetworkInterface value '{NetworkInterface}'", _config.NetworkInterface);
            return false;
        }

        if (!InputValidator.TryNormalizeLinuxDeviceName(_config.IfbDevice, out ifb))
        {
            _logger.LogError("Invalid IfbDevice value '{IfbDevice}'", _config.IfbDevice);
            return false;
        }

        return true;
    }

    private bool TryNormalizeShapingTarget(string ip, uint port, out string normalizedIp)
    {
        normalizedIp = string.Empty;

        if (!InputValidator.TryNormalizeIpv4(ip, out normalizedIp))
        {
            _logger.LogWarning("Skipping shaping for invalid IPv4 address");
            return false;
        }

        if (!InputValidator.IsValidPort(port))
        {
            _logger.LogWarning("Skipping shaping for invalid port {Port}", port);
            return false;
        }

        return true;
    }

    private static bool IsBaseSetupError(string stderr)
    {
        return stderr.Contains("Cannot find device", StringComparison.OrdinalIgnoreCase)
               || stderr.Contains("Parent Qdisc doesn't exists", StringComparison.OrdinalIgnoreCase)
               || stderr.Contains("Failed to find specified qdisc", StringComparison.OrdinalIgnoreCase)
               || stderr.Contains("No such file or directory", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAlreadyExistsError(int exitCode, string stderr)
    {
        if (exitCode == 0)
            return true;

        return stderr.Contains("File exists", StringComparison.OrdinalIgnoreCase)
               || stderr.Contains("Exclusivity flag on", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIgnorableDeleteError(int exitCode, string stderr)
    {
        if (exitCode == 0)
            return true;

        return stderr.Contains("No such file or directory", StringComparison.OrdinalIgnoreCase)
               || stderr.Contains("Cannot find device", StringComparison.OrdinalIgnoreCase)
               || stderr.Contains("Parent Qdisc doesn't exists", StringComparison.OrdinalIgnoreCase)
               || stderr.Contains("Failed to find specified qdisc", StringComparison.OrdinalIgnoreCase)
               || stderr.Contains("Cannot delete qdisc with handle of zero", StringComparison.OrdinalIgnoreCase)
               || stderr.Contains("Cannot flush filters with protocol, handle or kind set", StringComparison.OrdinalIgnoreCase)
               || stderr.Contains("Cannot find specified filter chain", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatCommand(CommandSpec cmd) => $"{cmd.FileName} {string.Join(' ', cmd.Arguments)}";

    private bool TryAcquireMinorId(string ip, uint port, out int minor)
    {
        var key = $"{ip}:{port}";

        lock (_minorIdLock)
        {
            if (_minorIds.TryGetValue(key, out minor))
                return true;

            // Prefer never-used IDs; reuse freed ones only once the range is exhausted
            if (_nextMinorId <= LastMinorId)
                minor = _nextMinorId++;
            else if (!_freeMinorIds.TryDequeue(out minor))
                return false;

            _minorIds[key] = minor;
            return true;
        }
    }

    private bool TryReleaseMinorId(string ip, uint port, out int minor)
    {
        lock (_minorIdLock)
        {
            if (!_minorIds.Remove($"{ip}:{port}", out minor))
                return false;

            _freeMinorIds.Enqueue(minor);
            return true;
        }
    }

    private sealed record CommandSpec(
        string FileName,
        IReadOnlyList<string> Arguments,
        Func<int, string, bool>? ExpectedFailure = null)
    {
        public bool IsExpectedFailure(int exitCode, string stderr) => ExpectedFailure?.Invoke(exitCode, stderr) ?? false;
    }
}
