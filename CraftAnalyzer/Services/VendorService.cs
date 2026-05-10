using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using CraftAnalyzer.Models;

namespace CraftAnalyzer.Services;

/// <summary>
/// Service for resolving item acquisition from NPC vendors.
/// Inspired by GatherBuddy's Vulcan engine.
/// </summary>
public class VendorService
{
    private readonly IDataManager dataManager;
    private readonly Dictionary<uint, List<VendorData>> itemToVendors = new();

    public VendorService(IDataManager dataManager)
    {
        this.dataManager = dataManager;
        InitializeVendorMap();
    }

    private void InitializeVendorMap()
    {
        var gilShopItems = dataManager.GetSubrowExcelSheet<GilShopItem>();
        var enpcBases = dataManager.GetExcelSheet<ENpcBase>();
        var enpcResidents = dataManager.GetExcelSheet<ENpcResident>();
        var levels = dataManager.GetExcelSheet<Level>();
        var items = dataManager.GetExcelSheet<Item>();
        var territories = dataManager.GetExcelSheet<TerritoryType>();

        if (gilShopItems == null || enpcBases == null || enpcResidents == null || levels == null || items == null || territories == null) return;

        // 1. Map NPCs to their names and locations
        var npcInfoMap = new Dictionary<uint, (string Name, uint TerritoryId, uint MapId)>();
        
        var npcLocations = levels
            .Where(l => l.Type == 8 && l.Object.RowId != 0)
            .GroupBy(l => l.Object.RowId)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var npc in enpcBases)
        {
            if (npc.RowId == 0) continue;
            
            var resident = enpcResidents.GetRow(npc.RowId);
            var name = resident.Singular.ToString();
            if (string.IsNullOrEmpty(name)) continue;

            if (npcLocations.TryGetValue(npc.RowId, out var level))
            {
                uint territoryId = level.Territory.RowId;
                uint mapId = 0;
                
                if (territories.TryGetRow(territoryId, out var territory))
                {
                    mapId = territory.Map.RowId;
                }

                npcInfoMap[npc.RowId] = (name, territoryId, mapId);
            }
        }

        // 2. Map GilShopId -> NPC
        var shopToNpc = new Dictionary<uint, uint>();
        foreach (var npc in enpcBases)
        {
            foreach (var dataId in npc.ENpcData)
            {
                if (dataId.RowId != 0)
                {
                    shopToNpc[dataId.RowId] = npc.RowId;
                }
            }
        }

        // 3. Process GilShopItems (Subrow sheet)
        foreach (var shopRow in gilShopItems)
        {
            uint shopId = shopRow.RowId;
            if (!shopToNpc.TryGetValue(shopId, out uint npcId)) continue;
            if (!npcInfoMap.TryGetValue(npcId, out var npcInfo)) continue;

            foreach (var itemRow in shopRow)
            {
                uint itemId = itemRow.Item.RowId;
                if (itemId == 0) continue;

                if (items.TryGetRow(itemId, out var item))
                {
                    if (!itemToVendors.ContainsKey(itemId))
                        itemToVendors[itemId] = new List<VendorData>();

                    itemToVendors[itemId].Add(new VendorData
                    {
                        ItemId = itemId,
                        Cost = item.PriceMid,
                        NpcName = npcInfo.Name,
                        TerritoryId = npcInfo.TerritoryId,
                        MapId = npcInfo.MapId
                    });
                }
            }
        }
    }

    public VendorData? GetCheapestVendor(uint itemId)
    {
        if (itemToVendors.TryGetValue(itemId, out var vendors))
        {
            return vendors.OrderBy(v => v.Cost).FirstOrDefault();
        }
        return null;
    }
}
