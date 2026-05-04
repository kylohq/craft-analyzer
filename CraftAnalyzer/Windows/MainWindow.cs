using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Lumina.Excel.Sheets;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace CraftAnalyzer.Windows;

/// <summary>
/// The primary user interface for the CraftAnalyzer plugin.
/// </summary>
public class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private uint searchItemId = 0;
    private string itemName = "No item selected";
    
    private bool isLoading = false;
    private Dictionary<uint, float> materials = new();
    private Dictionary<uint, (int Price, string World)> prices = new();
    private float totalMaterialCost = 0;
    private float targetItemPrice = 0;

    private string searchInput = "";
    private List<Item> searchResults = new();
    
    private int craftQuantity = 1;
    private float salesVelocity = 0;
    private Dictionary<uint, int> inventoryCounts = new();

    private bool hasTargetListings = true;
    private string playerWorldName = "N/A";

    public MainWindow(Plugin plugin)
        : base("CraftAnalyzer##MainWindow", ImGuiWindowFlags.NoScrollbar)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(600, 500),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        this.plugin = plugin;
    }

    public void Dispose() { }

    /// <summary>
    /// Opens the window and initiates analysis for the specified item.
    /// </summary>
    /// <param name="itemId">The ID of the item to analyze.</param>
    public void OpenWithItem(uint itemId)
    {
        if (itemId == 0) return;
        
        searchItemId = itemId;
        UpdateItemName();
        IsOpen = true;
        _ = RunAnalysisAsync();
    }

    public override void Draw()
    {
        DrawSearchSection();
        ImGui.Separator();
        DrawResultsSection();
    }
    
    /// <summary>
    /// Renders the item search and autocomplete interface.
    /// </summary>
    private void DrawSearchSection()
    {
        ImGui.Text("Search Item:");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("##SearchInput", "Start typing item name...", ref searchInput, 100))
        {
            UpdateSearchResults();
        }

        if (searchResults.Count > 0)
        {
            uint selectedId = 0;
            using var child = ImRaii.Child("SearchResults", new Vector2(0, Math.Min(searchResults.Count * 30, 200) * ImGuiHelpers.GlobalScale), true);
            if (child.Success)
            {
                foreach (var item in searchResults)
                {
                    var icon = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(item.Icon)).GetWrapOrEmpty();
                    ImGui.Image(icon.Handle, new Vector2(24, 24) * ImGuiHelpers.GlobalScale);
                    ImGui.SameLine();
                    
                    if (ImGui.Selectable($"{item.Name}##{item.RowId}", false, ImGuiSelectableFlags.None, new Vector2(0, 24 * ImGuiHelpers.GlobalScale)))
                    {
                        selectedId = item.RowId;
                    }
                }
            }

            if (selectedId != 0)
            {
                if (Plugin.RecipeParser.IsCraftable(selectedId))
                {
                    OpenWithItem(selectedId);
                    searchInput = "";
                    searchResults.Clear();
                }
                else
                {
                    Plugin.ToastGui.ShowError("Item is not craftable");
                }
            }
        }
    }

    /// <summary>
    /// Renders the analysis results, including material lists and profit estimates.
    /// </summary>
    private void DrawResultsSection()
    {
        using (var child = ImRaii.Child("ResultArea", Vector2.Zero, false))
        {
            if (!child.Success) return;

            if (searchItemId == 0)
            {
                ImGui.TextWrapped("Select a craftable item above or right-click one in-game.");
                return;
            }

            var targetIcon = Plugin.DataManager.GetExcelSheet<Item>().TryGetRow(searchItemId, out var row) ? row.Icon : 0u;
            var targetIconTex = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(targetIcon)).GetWrapOrEmpty();
            
            ImGui.Image(targetIconTex.Handle, new Vector2(32, 32) * ImGuiHelpers.GlobalScale);
            ImGui.SameLine();
            
            ImGui.TextColored(new Vector4(1, 0.8f, 0, 1), $"{itemName}");
            ImGui.TextDisabled($"(ID: {searchItemId})");
            
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100 * ImGuiHelpers.GlobalScale);
            if (ImGui.SliderInt("Quantity##CraftQty", ref craftQuantity, 1, 99))
            {
                _ = RunAnalysisAsync();
            }
            
            ImGui.Spacing();

            if (isLoading)
            {
                ImGui.Text("Fetching market data... Please wait.");
                float time = (float)DateTime.Now.TimeOfDay.TotalSeconds;
                string dots = new string('.', (int)(time * 2) % 4);
                ImGui.Text(dots);
            }
            else if (materials.Count > 0)
            {
                DrawResultsTable();
                DrawProfitMargin();
            }
        }
    }

    /// <summary>
    /// Filters the item sheet based on user input for search.
    /// </summary>
    private void UpdateSearchResults()
    {
        if (string.IsNullOrWhiteSpace(searchInput) || searchInput.Length < 3)
        {
            searchResults.Clear();
            return;
        }

        searchResults = Plugin.DataManager.GetExcelSheet<Item>()
            .Where(x => x.Name.ToString().Contains(searchInput, StringComparison.OrdinalIgnoreCase))
            .Take(10)
            .ToList();
    }

    /// <summary>
    /// Updates the target item name from game data.
    /// </summary>
    private void UpdateItemName()
    {
        if (Plugin.DataManager.GetExcelSheet<Item>().TryGetRow(searchItemId, out var itemRow))
        {
            itemName = itemRow.Name.ToString();
        }
        else
        {
            itemName = "Unknown Item";
        }
    }

    /// <summary>
    /// Coordinates the data fetching and calculation process.
    /// </summary>
    private async Task RunAnalysisAsync()
    {
        isLoading = true;
        materials.Clear();
        prices.Clear();
        totalMaterialCost = 0;
        targetItemPrice = 0;
        hasTargetListings = true;
        salesVelocity = 0;
        inventoryCounts.Clear();

        try
        {
            materials = Plugin.RecipeParser.GetBaseMaterials(searchItemId);

            if (materials.Count > 0)
            {
                var materialIds = materials.Keys.ToList();
                var materialsTask = Plugin.Universalis.GetRegionPricesAsync(materialIds);
                
                var player = Plugin.ObjectTable.LocalPlayer;
                uint homeWorldId = player?.HomeWorld.RowId ?? 0;
                playerWorldName = (player != null && homeWorldId != 0) ? player.HomeWorld.Value.Name.ToString() : "N/A";
                var targetTask = homeWorldId != 0 
                    ? Plugin.Universalis.GetWorldPriceAsync(homeWorldId, searchItemId)
                    : Task.FromResult((Price: 0, HasListings: false, SalesPerDay: 0.0f));

                UpdateInventoryCounts(materialIds);

                await Task.WhenAll(materialsTask, targetTask);

                var materialPrices = await materialsTask;
                totalMaterialCost = 0;
                foreach (var mat in materials)
                {
                    if (materialPrices.TryGetValue(mat.Key, out var priceData))
                    {
                        prices[mat.Key] = priceData;
                        
                        int inInventory = inventoryCounts.GetValueOrDefault(mat.Key, 0);
                        float totalRequired = mat.Value * craftQuantity;
                        float needed = Math.Max(0, totalRequired - inInventory);
                        totalMaterialCost += needed * priceData.Price;
                    }
                }

                var targetResult = await targetTask;
                targetItemPrice = targetResult.Price;
                hasTargetListings = targetResult.HasListings;
                salesVelocity = targetResult.SalesPerDay;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Analysis process failed.");
        }
        finally
        {
            isLoading = false;
        }
    }

    /// <summary>
    /// Renders the detailed materials breakdown table.
    /// </summary>
    private void DrawResultsTable()
    {
        using var table = ImRaii.Table("AnalysisTable", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY, new Vector2(0, 250 * ImGuiHelpers.GlobalScale));
        if (table.Success)
        {
            ImGui.TableSetupColumn("Material", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Have", ImGuiTableColumnFlags.WidthFixed, 50 * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Need", ImGuiTableColumnFlags.WidthFixed, 50 * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Price", ImGuiTableColumnFlags.WidthFixed, 80 * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Cheapest Server", ImGuiTableColumnFlags.WidthFixed, 100 * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Subtotal", ImGuiTableColumnFlags.WidthFixed, 90 * ImGuiHelpers.GlobalScale);
            ImGui.TableHeadersRow();

            foreach (var mat in materials.OrderByDescending(m => m.Value * (prices.ContainsKey(m.Key) ? prices[m.Key].Price : 0)))
            {
                ImGui.TableNextRow();
                
                ImGui.TableNextColumn();
                uint iconId = 0;
                string name = mat.Key.ToString();
                if (Plugin.DataManager.GetExcelSheet<Item>().TryGetRow(mat.Key, out var itemRow))
                {
                    name = itemRow.Name.ToString();
                    iconId = itemRow.Icon;
                }
                
                using (ImRaii.Group())
                {
                    var iconTex = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty();
                    ImGui.Image(iconTex.Handle, new Vector2(20, 20) * ImGuiHelpers.GlobalScale);
                    ImGui.SameLine();
                    ImGui.Text(name);
                }
                
                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                {
                    ImGui.SetClipboardText(name);
                    Plugin.ToastGui.ShowNormal($"Copied '{name}' to clipboard");
                }
                
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Right-click to copy name");
                }

                ImGui.TableNextColumn();
                int inInventory = inventoryCounts.GetValueOrDefault(mat.Key, 0);
                ImGui.Text(inInventory.ToString());

                ImGui.TableNextColumn();
                float totalRequired = mat.Value * craftQuantity;
                float needed = Math.Max(0, totalRequired - inInventory);
                ImGui.TextColored(needed > 0 ? new Vector4(1, 0.5f, 0.5f, 1) : new Vector4(0.5f, 1, 0.5f, 1), $"{(int)Math.Ceiling(needed)}");

                ImGui.TableNextColumn();
                int price = prices.TryGetValue(mat.Key, out var priceData) ? priceData.Price : 0;
                string world = prices.TryGetValue(mat.Key, out var worldData) ? worldData.World : "N/A";
                ImGui.Text($"{price:N0}g");

                ImGui.TableNextColumn();
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 1, 1), world);

                ImGui.TableNextColumn();
                float subtotal = needed * price;
                ImGui.Text($"{subtotal:N0}g");
            }
        }
    }
    
    /// <summary>
    /// Scans the player's inventory and crystal tab for required materials.
    /// </summary>
    private unsafe void UpdateInventoryCounts(List<uint> itemIds)
    {
        var invManager = InventoryManager.Instance();
        if (invManager == null) return;

        var types = new[] { 
            InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4,
            InventoryType.Crystals 
        };
        foreach (var type in types)
        {
            var container = invManager->GetInventoryContainer(type);
            if (container == null) continue;

            for (int i = 0; i < container->Size; i++)
            {
                var slot = container->GetInventorySlot(i);
                if (slot == null) continue;

                uint id = slot->ItemId;
                // Normalize Item ID by removing HQ/Collectable offsets
                if (id > 1000000) id -= 1000000; 

                if (itemIds.Contains(id))
                {
                    if (inventoryCounts.ContainsKey(id))
                        inventoryCounts[id] += (int)slot->Quantity;
                    else
                        inventoryCounts[id] = (int)slot->Quantity;
                }
            }
        }
    }

    /// <summary>
    /// Renders the profit margin analysis summary.
    /// </summary>
    private void DrawProfitMargin()
    {
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), "Market Value Analysis");
        ImGui.SameLine();
        ImGui.TextDisabled($"(On {playerWorldName})");
        ImGui.Spacing();

        using (var summaryChild = ImRaii.Child("SummaryArea", new Vector2(0, 160 * ImGuiHelpers.GlobalScale), true))
        {
            if (summaryChild.Success)
            {
                ImGui.Columns(2, "ProfitColumns", false);
                ImGui.SetColumnWidth(0, 300 * ImGuiHelpers.GlobalScale);

                ImGui.Text("Current Market Price:");
                
                if (hasTargetListings)
                {
                    ImGui.TextColored(new Vector4(1, 1, 0, 1), $"{targetItemPrice:N0} Gil");
                    ImGui.SameLine();
                    ImGui.TextDisabled($"x {craftQuantity}");
                }
                else
                {
                    ImGui.TextColored(new Vector4(1, 0.4f, 0.4f, 1), $"No Listings on {playerWorldName}");
                }
                
                ImGui.Text("Market Health:");
                ImGui.SameLine();
                string healthText = salesVelocity switch
                {
                    > 10 => "Excellent (High Velocity)",
                    > 3 => "Good (Steady Sales)",
                    > 0.5f => "Moderate (Slow)",
                    _ => "Poor (Stagnant)"
                };
                ImGui.TextColored(salesVelocity > 3 ? new Vector4(0, 1, 0, 1) : new Vector4(1, 1, 0, 1), healthText);
                ImGui.TextDisabled($"({salesVelocity:F1} sales / day)");

                ImGui.NextColumn();
                
                ImGui.Text("Total Material Cost:");
                ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), $"{totalMaterialCost:N0} Gil");
                ImGui.TextDisabled("(Items you don't already have)");

                ImGui.Columns(1);
                ImGui.Separator();

                float totalPrice = targetItemPrice * craftQuantity;
                float tax = totalPrice * 0.05f;
                float profit = totalPrice - totalMaterialCost - tax;
                Vector4 profitColor = (profit >= 0 && hasTargetListings) ? new Vector4(0.2f, 1, 0.2f, 1) : new Vector4(1, 0.3f, 0.3f, 1);

                ImGui.Spacing();
                ImGui.Text("Estimated Profit:");
                ImGui.SameLine();
                
                string profitText = hasTargetListings ? $"{profit:N0} Gil" : "N/A";
                ImGui.TextColored(profitColor, profitText);
                ImGui.SameLine();
                ImGui.TextDisabled("(After 5% MB Tax)");

                if (targetItemPrice > 0 && hasTargetListings)
                {
                    float percentage = (profit / totalPrice) * 100;
                    ImGui.SameLine();
                    ImGui.TextDisabled($"({percentage:F1}%)");
                }

                ImGui.Spacing();
                ImGui.TextDisabled($"Regional Pricing: Lowest from {Plugin.Universalis.GetRegion()}");
            }
        }

        ImGui.Spacing();
        if (ImGui.Button("Force Refresh All Prices", new Vector2(-1, 35 * ImGuiHelpers.GlobalScale)))
        {
            _ = RunAnalysisAsync();
        }
    }
}

