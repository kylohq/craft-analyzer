using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace CraftAnalyzer.Windows;

/// <summary>
/// Window for managing plugin configuration.
/// </summary>
public class ConfigWindow : Window, IDisposable
{
    private readonly Configuration configuration;

    public ConfigWindow(Plugin plugin) : base("CraftAnalyzer Configuration###ConfigWindow")
    {
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse;

        Size = new Vector2(300, 150);
        SizeCondition = ImGuiCond.FirstUseEver;

        configuration = plugin.Configuration;
    }

    public void Dispose() { }

    public override void Draw()
    {
        ImGui.Text("CraftAnalyzer Settings");
        ImGui.Separator();
        
        var queryEntireRegion = configuration.QueryEntireRegion;
        if (ImGui.Checkbox("Search entire region instead of data center", ref queryEntireRegion))
        {
            configuration.QueryEntireRegion = queryEntireRegion;
            configuration.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("If enabled, the plugin will search for prices across your entire region (e.g. Europe).\n" +
                             "If disabled, it will only search your current Data Center (e.g. Chaos).");
        }
    }
}

