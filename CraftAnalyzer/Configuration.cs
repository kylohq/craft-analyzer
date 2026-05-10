using Dalamud.Configuration;
using System;

namespace CraftAnalyzer;

/// <summary>
/// Persistent plugin configuration.
/// </summary>
[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;
    
    public bool QueryEntireRegion { get; set; } = false;
    public string LastSeenVersion { get; set; } = string.Empty;

    /// <summary>
    /// Persists the current configuration to disk.
    /// </summary>
    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}

