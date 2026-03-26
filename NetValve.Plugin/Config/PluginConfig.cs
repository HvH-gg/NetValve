using System.Text.Json.Serialization;
using CounterStrikeSharp.API.Core;

namespace NetValve.Plugin.Config;

public sealed partial class PluginConfig : BasePluginConfig
{
    [JsonPropertyName("ControllerUrl")] public string ControllerUrl { get; set; } = string.Empty;
    [JsonPropertyName("AuthToken")] public string AuthToken { get; set; } = string.Empty;
    [JsonPropertyName("ServerId")] public string ServerId { get; set; } = string.Empty;
    [JsonPropertyName("HttpFallbackUrl")] public string? HttpFallbackUrl { get; set; }
    [JsonPropertyName("EnableHttpFallback")] public bool EnableHttpFallback { get; set; } = true;
    [JsonPropertyName("QueueSize")] public int QueueSize { get; set; } = 10;
    [JsonPropertyName("ConfigVersion")] public override int Version { get; set; } = 1;
}