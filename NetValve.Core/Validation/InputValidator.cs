using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace NetValve.Core.Validation;

public static partial class InputValidator
{
    [GeneratedRegex("^[A-Za-z0-9_.:-]{1,15}$", RegexOptions.CultureInvariant)]
    private static partial Regex LinuxDeviceNamePattern();

    [GeneratedRegex("^[A-Za-z0-9_.:-]{1,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex ServerIdPattern();

    public static bool TryNormalizeLinuxDeviceName(string? value, out string normalized)
    {
        normalized = value?.Trim() ?? string.Empty;
        return normalized.Length > 0 && LinuxDeviceNamePattern().IsMatch(normalized);
    }

    public static bool TryNormalizeIpv4(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (!TryNormalizeIpAddress(value, out var address))
            return false;

        if (!IPAddress.TryParse(address, out var parsed) || parsed.AddressFamily != AddressFamily.InterNetwork)
            return false;

        normalized = parsed.ToString();
        return true;
    }

    public static bool TryNormalizeIpAddress(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (!IPAddress.TryParse(value?.Trim(), out var parsed))
            return false;

        normalized = parsed.ToString();
        return true;
    }

    public static bool TryNormalizeServerId(string? value, out string normalized)
    {
        normalized = value?.Trim() ?? string.Empty;
        return normalized.Length > 0 && ServerIdPattern().IsMatch(normalized);
    }

    public static bool IsValidPort(uint port) => port is > 0 and <= 65535;

    public static bool IsValidPort(int port) => port is > 0 and <= 65535;
}
