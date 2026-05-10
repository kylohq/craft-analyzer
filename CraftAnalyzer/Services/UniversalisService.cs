using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using CraftAnalyzer.Models;
using Dalamud.Plugin.Services;

namespace CraftAnalyzer.Services;

/// <summary>
/// Service for interacting with the Universalis API to fetch market data.
/// </summary>
public class UniversalisService
{
    private static readonly HttpClient HttpClient = new HttpClient();
    
    static UniversalisService()
    {
        HttpClient.DefaultRequestHeaders.Add("User-Agent", "CraftAnalyzer/1.0 (FFXIV Plugin)");
    }
    
    private readonly IObjectTable objectTable;
    private readonly IDataManager dataManager;
    private readonly Configuration configuration;

    public UniversalisService(IObjectTable objectTable, IDataManager dataManager, Configuration configuration)
    {
        this.objectTable = objectTable;
        this.dataManager = dataManager;
        this.configuration = configuration;
    }

    /// <summary>
    /// Gets the region of the local player.
    /// </summary>
    /// <returns>A string representing the region (Japan, North-America, Europe, Oceania).</returns>
    public string GetRegion()
    {
        if (objectTable.LocalPlayer == null || objectTable.LocalPlayer.HomeWorld.RowId == 0) return "Europe";

        var regionId = objectTable.LocalPlayer.HomeWorld.Value.DataCenter.Value.Region.RowId;
        return regionId switch
        {
            1 => "Japan",
            2 => "North-America",
            3 => "Europe",
            4 => "Oceania",
            _ => "Europe"
        };
    }

    /// <summary>
    /// Gets the data center name of the local player.
    /// </summary>
    /// <returns>A string representing the data center name.</returns>
    public string GetDataCenter()
    {
        if (objectTable.LocalPlayer == null || objectTable.LocalPlayer.HomeWorld.RowId == 0) return "Chaos";

        var dc = objectTable.LocalPlayer.HomeWorld.Value.DataCenter.Value;
        return dc.Name.ToString();
    }

    /// <summary>
    /// Fetches the lowest prices for multiple items across the current region.
    /// </summary>
    /// <param name="itemIds">The list of item IDs to query.</param>
    /// <returns>A dictionary mapping item IDs to their lowest price and corresponding world name.</returns>
    public async Task<Dictionary<uint, (int Price, string World)>> GetRegionPricesAsync(IEnumerable<uint> itemIds)
    {
        var results = new Dictionary<uint, (int Price, string World)>();
        var idList = itemIds.Distinct().ToList();
        if (idList.Count == 0) return results;

        var scope = configuration.QueryEntireRegion ? GetRegion() : GetDataCenter();
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        for (int i = 0; i < idList.Count; i += 100)
        {
            var chunk = idList.Skip(i).Take(100).ToList();
            var commaSeparatedIds = string.Join(",", chunk);
            var url = $"https://universalis.app/api/v2/{scope}/{commaSeparatedIds}?listings=5";

            try
            {
                var response = await HttpClient.GetStringAsync(url);
                
                var data = JsonSerializer.Deserialize<UniversalisResponse>(response, options);
                if (data?.Items != null)
                {
                    foreach (var entry in data.Items)
                    {
                        var lowest = entry.Value.Listings.OrderBy(l => l.PricePerUnit).FirstOrDefault();
                        if (lowest != null) 
                        {
                            results[entry.Value.ItemID] = (lowest.PricePerUnit, lowest.WorldName);
                        }
                    }
                }
                else
                {
                    var single = JsonSerializer.Deserialize<ItemData>(response, options);
                    if (single != null)
                    {
                        var lowest = single.Listings.OrderBy(l => l.PricePerUnit).FirstOrDefault();
                        if (lowest != null) 
                        {
                            uint id = single.ItemID == 0 ? chunk[0] : single.ItemID;
                            results[id] = (lowest.PricePerUnit, lowest.WorldName);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, $"Failed to fetch prices for {scope} in chunk {i / 100}");
            }
        }

        return results;
    }

    /// <summary>
    /// Fetches the lowest price and sales velocity for a specific item on a specific world.
    /// </summary>
    /// <param name="worldId">The ID of the world to query.</param>
    /// <param name="itemId">The ID of the item to query.</param>
    /// <returns>A tuple containing the price, availability status, and average sales per day.</returns>
    public async Task<(int Price, bool HasListings, float SalesPerDay)> GetWorldPriceAsync(uint worldId, uint itemId)
    {
        var url = $"https://universalis.app/api/v2/{worldId}/{itemId}?listings=5&entries=10";
        
        try
        {
            var response = await HttpClient.GetStringAsync(url);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            
            ItemData? data = null;
            if (response.Contains("\"items\":"))
            {
                var multi = JsonSerializer.Deserialize<UniversalisResponse>(response, options);
                if (multi?.Items != null && multi.Items.Count > 0)
                {
                    data = multi.Items.Values.FirstOrDefault();
                }
            }
            
            if (data == null)
            {
                data = JsonSerializer.Deserialize<ItemData>(response, options);
            }

            if (data == null) return (0, false, 0);

            float salesPerDay = 0;
            if (data.RecentHistory != null && data.RecentHistory.Count > 1)
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var oldest = data.RecentHistory.Last().Timestamp;
                var days = (now - oldest) / 86400.0f;
                if (days > 0) salesPerDay = data.RecentHistory.Count / days;
            }

            if (data.Listings == null || data.Listings.Count == 0)
            {
                return (0, false, salesPerDay);
            }

            var lowest = data.Listings.OrderBy(l => l.PricePerUnit).FirstOrDefault();
            return (lowest?.PricePerUnit ?? 0, lowest != null, salesPerDay);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, $"Failed to fetch world price for world {worldId}, item {itemId}");
            return (0, false, 0);
        }
    }
}

