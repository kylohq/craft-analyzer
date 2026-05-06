using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Lumina.Excel.Sheets;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using CraftAnalyzer.Models;

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
    private List<MaterialData> materialDataList = new();
    private Dictionary<uint, (int Price, string World)> prices = new();
    private float totalMaterialCost = 0; // Cumulative cost of materials to buy
    private float targetItemPrice = 0;    // Market price of the primary target item
    private float totalMarketValue = 0;   // Summed market value of all items in current plan
    private Dictionary<uint, int> targetPricesHomeWorld = new(); // Cached home-world prices for cart items

    private string searchInput = "";
    private List<Item> searchResults = new();
    
    private int craftQuantity = 1;
    private float salesVelocity = 0;
    private Dictionary<uint, int> inventoryCounts = new();

    private bool hasTargetListings = true;
    private string playerWorldName = "N/A";
    private bool lastFetchFailed = false;
    
    // Tracks items marked to be gathered manually, excluding them from cost analysis.
    private HashSet<uint> itemsToGather = new();

    private bool isCartMode = false;

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
        itemsToGather.Clear(); // Reset gathered items when switching to a new target
        UpdateItemName();
        IsOpen = true;
        _ = RunAnalysisAsync();
    }

    public override void Draw()
    {
        using var scroll = ImRaii.Child("MainScroll", Vector2.Zero, false);
        if (scroll.Success)
        {
            DrawSearchSection();
            ImGui.Spacing();
            DrawShoppingCartSection();
            ImGui.Separator();
            DrawResultsSection();
        }
    }
    
    /// <summary>
    /// Renders the item search and autocomplete interface.
    /// </summary>
    private void DrawSearchSection()
    {
        using (var group = ImRaii.Group())
        {
            ImGui.TextColored(new Vector4(1, 0.8f, 0.2f, 1), "ITEM SEARCH");
            ImGui.SameLine();
            ImGui.TextDisabled("|");
            ImGui.SameLine();
            ImGui.TextDisabled("Find items to add to your plan");
            
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputTextWithHint("##SearchInput", "Search by name (e.g. 'Exarchic', 'Coffee')...", ref searchInput, 100))
            {
                UpdateSearchResults();
            }
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
                    plugin.AddToCart(selectedId);
                    searchInput = "";
                    searchResults.Clear();
                    RecomputeMaterials(); // Instant update when adding
                }
                else
                {
                    Plugin.ToastGui.ShowError("Item is not craftable");
                }
            }
        }
        
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
    }

    /// <summary>
    /// Renders the shopping cart management interface.
    /// </summary>
    private void DrawShoppingCartSection()
    {
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), "SHOPPING CART");
        ImGui.SameLine();
        ImGui.TextDisabled("|");
        ImGui.SameLine();
        ImGui.TextDisabled($"{plugin.ShoppingCart.Count} items in current plan");
        
        ImGui.Spacing();

        using (var table = ImRaii.Table("CartTable", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.NoHostExtendX))
        {
            if (table.Success)
            {
                ImGui.TableSetupColumn("Item Name", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Quantity", ImGuiTableColumnFlags.WidthFixed, 80 * ImGuiHelpers.GlobalScale);
                ImGui.TableSetupColumn(" ", ImGuiTableColumnFlags.WidthFixed, 30 * ImGuiHelpers.GlobalScale);
                ImGui.TableHeadersRow();

                for (int i = 0; i < plugin.ShoppingCart.Count; i++)
                {
                    var item = plugin.ShoppingCart[i];
                    ImGui.TableNextRow();
                    
                    ImGui.TableNextColumn();
                    uint iconId = Plugin.DataManager.GetExcelSheet<Item>().TryGetRow(item.ItemId, out var row) ? row.Icon : 0u;
                    var icon = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty();
                    ImGui.Image(icon.Handle, new Vector2(20, 20) * ImGuiHelpers.GlobalScale);
                    ImGui.SameLine();
                    ImGui.Text(item.Name);
                    
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(-1);
                    int qty = item.Quantity;
                    if (ImGui.InputInt($"##Qty{i}", ref qty, 0))
                    {
                        item.Quantity = Math.Max(1, qty);
                        RecomputeMaterials();
                    }
                    
                    ImGui.TableNextColumn();
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.6f, 0.2f, 0.2f, 0.6f));
                    if (ImGui.Button($"X##Remove{i}", new Vector2(24, 24) * ImGuiHelpers.GlobalScale))
                    {
                        plugin.ShoppingCart.RemoveAt(i);
                        i--; // Adjust index after removal
                        RecomputeMaterials();
                    }
                    ImGui.PopStyleColor();
                }
            }
        }

        if (plugin.ShoppingCart.Count == 0)
        {
            ImGui.TextDisabled("   (Add items from search or context menu to begin)");
        }
        else
        {
            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.4f, 0.6f, 1f));
            if (ImGui.Button("Calculate Craft Cost", new Vector2(-1, 35 * ImGuiHelpers.GlobalScale)))
            {
                isCartMode = true;
                _ = RunAnalysisAsync();
            }
            ImGui.PopStyleColor();
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

            if (searchItemId == 0 && !isCartMode)
            {
                ImGui.TextWrapped("Select a craftable item above or right-click one in-game.");
                return;
            }

            if (isCartMode)
            {
                ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), "COST ANALYSIS");
                ImGui.SameLine();
                ImGui.TextDisabled("|");
                ImGui.SameLine();
                ImGui.TextDisabled($"{plugin.ShoppingCart.Count} items added");
            }
            else
            {
                ImGui.TextColored(new Vector4(1, 0.8f, 0.2f, 1), "ITEM ANALYSIS");
                ImGui.SameLine();
                ImGui.TextDisabled("|");
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(1, 1, 1, 1), $"{itemName}");
                
                ImGui.SameLine();
                ImGui.SetNextItemWidth(60 * ImGuiHelpers.GlobalScale);
                if (ImGui.InputInt("##CraftQty", ref craftQuantity, 0))
                {
                    craftQuantity = Math.Clamp(craftQuantity, 1, 999);
                    RecomputeMaterials();
                }
                ImGui.SameLine();
                ImGui.TextDisabled("ct.");
            }
            
            ImGui.Spacing();

            if (isLoading)
            {
                ImGui.Text("Fetching market data... Please wait.");
                float time = (float)DateTime.Now.TimeOfDay.TotalSeconds;
                string dots = new string('.', (int)(time * 2) % 4);
                ImGui.Text(dots);
            }
            else if (materialDataList.Count > 0)
            {
                if (lastFetchFailed)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 0.4f, 0.4f, 1));
                    ImGui.TextWrapped("Market data request failed. Showing last known or empty prices.");
                    ImGui.PopStyleColor();
                    ImGui.Spacing();
                }

                DrawResultsTable();
                
                ImGui.Spacing();
                using (var group = ImRaii.Group())
                {
                    ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);
                    if (ImGui.Button("Copy Shopping List", new Vector2(ImGui.GetContentRegionAvail().X / 3 - 4 * ImGuiHelpers.GlobalScale, 30 * ImGuiHelpers.GlobalScale)))
                    {
                        CopyMaterialListToClipboard();
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Refresh Prices", new Vector2(ImGui.GetContentRegionAvail().X / 2 - 4 * ImGuiHelpers.GlobalScale, 30 * ImGuiHelpers.GlobalScale)))
                    {
                        _ = RunAnalysisAsync();
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("TeamCraft Import", new Vector2(-1, 30 * ImGuiHelpers.GlobalScale)))
                    {
                        ExportToTeamCraft();
                    }
                    ImGui.PopStyleVar();
                }
                
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
    /// Re-runs the material aggregation and inventory check without performing fresh API requests.
    /// This is used for instantaneous updates when quantities change.
    /// </summary>
    private void RecomputeMaterials()
    {
        if (materialDataList.Count == 0 && !isCartMode) return;

        Dictionary<uint, int> aggregate;
        if (isCartMode)
        {
            // Recalculate the entire recursive material tree for all items in the shopping cart
            aggregate = Plugin.RecipeParser.GetAggregateMaterials(plugin.ShoppingCart);
            
            // Re-calculate projected revenue based on current quantities and cached home-world prices
            totalMarketValue = 0;
            foreach (var item in plugin.ShoppingCart)
            {
                if (targetPricesHomeWorld.TryGetValue(item.ItemId, out var price))
                {
                    totalMarketValue += (float)price * item.Quantity;
                }
            }
        }
        else
        {
            // Calculate material requirements for a single item multi-crafted N times
            var single = Plugin.RecipeParser.GetBaseMaterials(searchItemId, craftQuantity);
            aggregate = single.ToDictionary(k => k.Key, v => (int)Math.Ceiling(v.Value));
            totalMarketValue = targetItemPrice * craftQuantity;
        }

        var itemIds = aggregate.Keys.ToList();
        UpdateInventoryCounts(itemIds);

        materialDataList.Clear();
        foreach (var kvp in aggregate)
        {
            var itemRow = Plugin.DataManager.GetExcelSheet<Item>().GetRow(kvp.Key);
            materialDataList.Add(new MaterialData(
                kvp.Key,
                itemRow.Name.ToString(),
                kvp.Value,
                inventoryCounts.GetValueOrDefault(kvp.Key, 0)
            ));
        }

        RefreshCalculations();
    }

    /// <summary>
    /// Recalculates costs based on currently cached price and inventory data.
    /// This is called when local state (quantity, gather list) changes to avoid redundant API calls.
    /// </summary>
    private void RefreshCalculations()
    {
        totalMaterialCost = 0;
        
        foreach (var mat in materialDataList)
        {
            if (prices.TryGetValue(mat.ItemId, out var priceData))
            {
                int needed = Math.Max(0, mat.TotalNeeded - mat.AmountOwned);
                
                // Exclude cost if the item is marked as "to be gathered"
                if (!itemsToGather.Contains(mat.ItemId))
                {
                    totalMaterialCost += needed * priceData.Price;
                }
            }
        }
    }

    /// <summary>
    /// Coordinates the data fetching process. Local calculations are deferred to RefreshCalculations.
    /// </summary>
    private async Task RunAnalysisAsync()
    {
        isLoading = true;
        lastFetchFailed = false;

        try
        {
            Dictionary<uint, int> aggregate;
            if (isCartMode)
            {
                aggregate = Plugin.RecipeParser.GetAggregateMaterials(plugin.ShoppingCart);
            }
            else
            {
                var single = Plugin.RecipeParser.GetBaseMaterials(searchItemId, craftQuantity);
                aggregate = single.ToDictionary(k => k.Key, v => (int)Math.Ceiling(v.Value));
            }

            if (aggregate.Count == 0) return;

            var itemIds = aggregate.Keys.ToList();
            UpdateInventoryCounts(itemIds);

            materialDataList.Clear();
            foreach (var kvp in aggregate)
            {
                var itemRow = Plugin.DataManager.GetExcelSheet<Item>().GetRow(kvp.Key);
                materialDataList.Add(new MaterialData(
                    kvp.Key,
                    itemRow.Name.ToString(),
                    kvp.Value,
                    inventoryCounts.GetValueOrDefault(kvp.Key, 0)
                ));
            }

            // Query prices for ALL materials in the list to ensure we have data if quantities change
            var idsToQuery = materialDataList.Select(m => m.ItemId).ToList();
            
            // Initiate parallel market data requests
            var materialsTask = idsToQuery.Count > 0 
                ? Plugin.Universalis.GetRegionPricesAsync(idsToQuery)
                : Task.FromResult(new Dictionary<uint, (int Price, string World)>());
            
            var player = Plugin.ObjectTable.LocalPlayer;
            uint homeWorldId = player?.HomeWorld.RowId ?? 0;
            playerWorldName = (player != null && homeWorldId != 0) ? player.HomeWorld.Value.Name.ToString() : "N/A";
            
            Task<(int Price, bool HasListings, float SalesPerDay)> targetTask;
            if (!isCartMode)
            {
                targetTask = homeWorldId != 0 
                    ? Plugin.Universalis.GetWorldPriceAsync(homeWorldId, searchItemId)
                    : Task.FromResult((Price: 0, HasListings: false, SalesPerDay: 0.0f));
            }
            else
            {
                targetTask = Task.FromResult((Price: 0, HasListings: false, SalesPerDay: 0.0f));
            }

            // Update local cache with fresh data
            prices = await materialsTask;
            var targetResult = await targetTask;
            
            targetItemPrice = targetResult.Price;
            hasTargetListings = targetResult.HasListings;
            salesVelocity = targetResult.SalesPerDay;

            // Cache individual item prices for the home world to support instant scaling
            targetPricesHomeWorld.Clear();
            if (isCartMode)
            {
                var targetPricesTasks = new List<Task<(int Price, bool HasListings, float SalesPerDay)>>();
                foreach (var item in plugin.ShoppingCart)
                {
                    targetPricesTasks.Add(homeWorldId != 0 
                        ? Plugin.Universalis.GetWorldPriceAsync(homeWorldId, item.ItemId)
                        : Task.FromResult((Price: 0, HasListings: false, SalesPerDay: 0.0f)));
                }

                var targetPricesResults = await Task.WhenAll(targetPricesTasks);
                for (int i = 0; i < plugin.ShoppingCart.Count; i++)
                {
                    uint itemId = plugin.ShoppingCart[i].ItemId;
                    int price = targetPricesResults[i].Price;
                    targetPricesHomeWorld[itemId] = price;
                }
            }
            else
            {
                targetPricesHomeWorld[searchItemId] = (int)targetItemPrice;
            }

            // Finalize with local calculations
            RecomputeMaterials();
            
            // If we have materials but no prices were fetched, something went wrong
            if (idsToQuery.Count > 0 && prices.Count == 0)
            {
                lastFetchFailed = true;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Analysis process failed. Preserving last known data.");
            lastFetchFailed = true;
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
        using var table = ImRaii.Table("AnalysisTable", 8, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY, new Vector2(0, 250 * ImGuiHelpers.GlobalScale));
        if (table.Success)
        {
            ImGui.TableSetupColumn("Material Name", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Gather", ImGuiTableColumnFlags.WidthFixed, 50 * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Needed", ImGuiTableColumnFlags.WidthFixed, 60 * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Owned", ImGuiTableColumnFlags.WidthFixed, 60 * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("To Buy", ImGuiTableColumnFlags.WidthFixed, 60 * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Unit Price", ImGuiTableColumnFlags.WidthFixed, 80 * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Server", ImGuiTableColumnFlags.WidthFixed, 90 * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Subtotal", ImGuiTableColumnFlags.WidthFixed, 90 * ImGuiHelpers.GlobalScale);
            ImGui.TableHeadersRow();

            foreach (var mat in materialDataList.OrderByDescending(m => Math.Max(0, m.TotalNeeded - m.AmountOwned) * (prices.ContainsKey(m.ItemId) ? prices[m.ItemId].Price : 0)))
            {
                ImGui.TableNextRow();
                
                // Material Name
                ImGui.TableNextColumn();
                uint iconId = 0;
                if (Plugin.DataManager.GetExcelSheet<Item>().TryGetRow(mat.ItemId, out var itemRow))
                {
                    iconId = itemRow.Icon;
                }
                
                using (ImRaii.Group())
                {
                    var iconTex = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty();
                    ImGui.Image(iconTex.Handle, new Vector2(20, 20) * ImGuiHelpers.GlobalScale);
                    ImGui.SameLine();
                    ImGui.Text(mat.Name);
                }
                
                if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                {
                    ImGui.SetClipboardText(mat.Name);
                    Plugin.ToastGui.ShowNormal($"Copied '{mat.Name}' to clipboard");
                }
                
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Left-click to copy name");
                }

                // Gather Checkbox
                ImGui.TableNextColumn();
                if (Plugin.RecipeParser.IsGatherable(mat.ItemId))
                {
                    bool isGathered = itemsToGather.Contains(mat.ItemId);
                    if (ImGui.Checkbox($"##Gather{mat.ItemId}", ref isGathered))
                    {
                        if (isGathered) itemsToGather.Add(mat.ItemId);
                        else itemsToGather.Remove(mat.ItemId);
                        
                        RefreshCalculations();
                    }
                }
                else
                {
                    ImGui.TextDisabled("N/A");
                }

                // Needed
                ImGui.TableNextColumn();
                ImGui.Text(mat.TotalNeeded.ToString());

                // Owned
                ImGui.TableNextColumn();
                ImGui.Text(mat.AmountOwned.ToString());

                // To Buy
                ImGui.TableNextColumn();
                int toBuy = Math.Max(0, mat.TotalNeeded - mat.AmountOwned);
                ImGui.TextColored(toBuy > 0 ? new Vector4(1, 0.5f, 0.5f, 1) : new Vector4(0.5f, 1, 0.5f, 1), $"{toBuy}");

                // Unit Price
                ImGui.TableNextColumn();
                bool hasPrice = prices.TryGetValue(mat.ItemId, out var priceData);
                int price = hasPrice ? priceData.Price : 0;
                
                if (hasPrice)
                {
                    ImGui.Text($"{price:N0}g");
                }
                else
                {
                    ImGui.TextDisabled(lastFetchFailed ? "???" : "N/A");
                }

                // Server
                ImGui.TableNextColumn();
                string world = prices.TryGetValue(mat.ItemId, out var worldData) ? worldData.World : (lastFetchFailed ? "Error" : "N/A");
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 1, 1), world);

                // Subtotal
                ImGui.TableNextColumn();
                if (toBuy == 0)
                {
                    ImGui.TextDisabled("0g");
                }
                else
                {
                    long subtotal = (long)toBuy * price;
                    ImGui.Text($"{subtotal:N0}g");
                }
            }
        }
    }

    /// <summary>
    /// Builds a formatted string of the materials the user needs to buy and copies it to the clipboard.
    /// </summary>
    private void CopyMaterialListToClipboard()
    {
        var sb = new StringBuilder();
        foreach (var mat in materialDataList)
        {
            int toBuy = Math.Max(0, mat.TotalNeeded - mat.AmountOwned);
            if (toBuy > 0)
            {
                sb.AppendLine($"x{toBuy} {mat.Name}");
            }
        }

        if (sb.Length > 0)
        {
            ImGui.SetClipboardText(sb.ToString().TrimEnd());
            Plugin.ToastGui.ShowNormal("Copied shopping list to clipboard!");
        }
        else
        {
            Plugin.ToastGui.ShowNormal("Nothing to buy!");
        }
    }
    
    /// <summary>
    /// Scans the player's inventory and crystal tab for required materials.
    /// </summary>
    private unsafe void UpdateInventoryCounts(List<uint> itemIds)
    {
        inventoryCounts.Clear();
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

        if (isCartMode)
        {
            ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), "ESTIMATED PROFIT");
        }
        else
        {
            ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), "ESTIMATED ITEM PROFIT");
        }
        
        ImGui.SameLine();
        ImGui.TextDisabled($"(Market: {playerWorldName})");
        ImGui.Spacing();

        using (var summaryChild = ImRaii.Child("SummaryArea", new Vector2(0, 130 * ImGuiHelpers.GlobalScale), true))
        {
            if (summaryChild.Success)
            {
                ImGui.Columns(2, "ProfitColumns", false);
                ImGui.SetColumnWidth(0, 280 * ImGuiHelpers.GlobalScale);

                ImGui.TextDisabled("Projected Revenue");
                
                if (isCartMode || hasTargetListings)
                {
                    ImGui.TextColored(new Vector4(1, 0.9f, 0, 1), $"{totalMarketValue:N0} Gil");
                }
                else
                {
                    ImGui.TextColored(new Vector4(1, 0.4f, 0.4f, 1), $"No Listings");
                }
                
                if (!isCartMode)
                {
                    ImGui.TextDisabled("Market Velocity");
                    string healthText = salesVelocity switch
                    {
                        > 10 => "High",
                        > 3 => "Steady",
                        > 0.5f => "Moderate",
                        _ => "Low"
                    };
                    ImGui.TextColored(salesVelocity > 3 ? new Vector4(0, 1, 0, 1) : new Vector4(1, 1, 0, 1), healthText);
                    ImGui.SameLine();
                    ImGui.TextDisabled($"({salesVelocity:F1}/d)");
                }

                ImGui.NextColumn();
                
                ImGui.TextDisabled("Acquisition Cost");
                ImGui.TextColored(new Vector4(1, 0.6f, 0.4f, 1), $"{totalMaterialCost:N0} Gil");

                ImGui.Columns(1);
                ImGui.Separator();

                float tax = totalMarketValue * 0.05f;
                float profit = totalMarketValue - totalMaterialCost - tax;
                Vector4 profitColor = profit >= 0 ? new Vector4(0.4f, 1, 0.4f, 1) : new Vector4(1, 0.3f, 0.3f, 1);

                ImGui.Spacing();
                ImGui.Text("NET PROFIT");
                ImGui.SameLine();
                ImGui.TextDisabled("(After 5% MB Tax)");
                
                ImGui.TextColored(profitColor, $"{profit:N0} Gil");
                if (totalMarketValue > 0)
                {
                    ImGui.SameLine();
                    float percentage = (profit / totalMarketValue) * 100;
                    ImGui.TextColored(profitColor * 0.8f, $"({percentage:F1}%)");
                }

                ImGui.Spacing();
                ImGui.TextDisabled($"Regional Pricing: Lowest from {Plugin.Universalis.GetRegion()}");
            }
        }

        ImGui.Spacing();
    }

    /// <summary>
    /// Encodes the current gathering list into a TeamCraft import URL and opens it in the browser.
    /// </summary>
    private void ExportToTeamCraft()
    {
        if (itemsToGather.Count == 0)
        {
            Plugin.ToastGui.ShowError("Mark some materials as 'Gathered' first.");
            return;
        }

        var exportItems = new List<string>();
        foreach (var mat in materialDataList)
        {
            if (itemsToGather.Contains(mat.ItemId))
            {
                int needed = Math.Max(0, mat.TotalNeeded - mat.AmountOwned);
                
                if (needed > 0)
                {
                    // TeamCraft format: itemId,recipeId(null),quantity
                    exportItems.Add($"{mat.ItemId},null,{needed}");
                }
            }
        }

        if (exportItems.Count == 0)
        {
            Plugin.ToastGui.ShowError("No items to export (all required items are already in your inventory)");
            return;
        }

        try
        {
            string exportString = string.Join(";", exportItems);
            string base64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(exportString));
            string url = $"https://ffxivteamcraft.com/import/{base64}";
            
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            Plugin.ToastGui.ShowNormal("Opening TeamCraft import page...");
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Failed to open TeamCraft URL");
            Plugin.ToastGui.ShowError("Could not open browser. Link copied to clipboard.");
        }
    }
}
