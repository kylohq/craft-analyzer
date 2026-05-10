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
        ImGui.Text("Latest Updates (v1.0.4.0):");
        ImGui.Separator();

        if (ImGui.BeginChild("ChangelogContent"))
        {
            DrawBullet("Vulcan-style Vendor Integration");
            ImGui.Indent();
            ImGui.TextWrapped("The plugin now automatically finds NPC vendors. If an item is sold by a vendor for less than the Market Board price, it will use the vendor price for all calculations.");
            ImGui.TextWrapped("Right-click a vendor price (marked with 'V') to set a map marker to the NPC!");
            ImGui.Unindent();

            DrawBullet("Pre-craft vs Material View");
            ImGui.Indent();
            ImGui.TextWrapped("Toggle between deep recursive breakdowns and immediate pre-craft ingredients using the new button in the analysis header.");
            ImGui.Unindent();

            DrawBullet("Data Center Specific Queries");
            ImGui.Indent();
            ImGui.TextWrapped("Queries now default to your current Data Center for more relevant pricing. You can revert this to Region-wide in settings.");
            ImGui.Unindent();

            DrawBullet("Under the Hood");
            ImGui.Indent();
            ImGui.TextWrapped("Implemented request chunking for Universalis to handle 100+ items and optimized startup indexing for lightning-fast performance.");
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
