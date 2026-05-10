using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Interface.Colors;

namespace CraftAnalyzer.Windows;

/// <summary>
/// Window for displaying the plugin changelog.
/// </summary>
public class ChangelogWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    public ChangelogWindow(Plugin plugin) : base("CraftAnalyzer - What's New?###ChangelogWindow")
    {
        this.plugin = plugin;

        Size = new Vector2(400, 300);
        SizeCondition = ImGuiCond.FirstUseEver;
        
        Flags = ImGuiWindowFlags.NoCollapse;
    }

    public void Dispose() { }

    public override void Draw()
    {
        ImGui.TextColored(ImGuiColors.ParsedGold, "Thank you for using CraftAnalyzer!");
        ImGui.Text("Latest Updates (v1.0.3.0):");
        ImGui.Separator();

        if (ImGui.BeginChild("ChangelogContent"))
        {
            DrawBullet("Added Data Center specific market queries.");
            ImGui.Indent();
            ImGui.TextWrapped("The plugin now defaults to searching only your current Data Center (e.g. Chaos or Light).");
            ImGui.Unindent();

            DrawBullet("New Settings Toggle.");
            ImGui.Indent();
            ImGui.TextWrapped("You can switch back to region-wide searches in the plugin settings if you prefer scanning the entire region.");
            ImGui.Unindent();

            DrawBullet("Changelog UI.");
            ImGui.Indent();
            ImGui.TextWrapped("You are looking at it! A new window will now inform you of changes after every update.");
            ImGui.Unindent();

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (ImGui.Button("Got it!", new Vector2(-1, 30)))
            {
                IsOpen = false;
            }

            ImGui.EndChild();
        }
    }

    private void DrawBullet(string text)
    {
        ImGui.TextColored(ImGuiColors.DalamudOrange, " • ");
        ImGui.SameLine();
        ImGui.Text(text);
    }
}
