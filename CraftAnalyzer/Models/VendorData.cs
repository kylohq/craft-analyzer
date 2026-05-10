using System;

namespace CraftAnalyzer.Models;

/// <summary>
/// Represents data for an NPC vendor selling a specific item.
/// </summary>
public class VendorData
{
    public uint ItemId { get; set; }
    public uint Cost { get; set; }
    public string NpcName { get; set; } = string.Empty;
    public uint TerritoryId { get; set; }
    public uint MapId { get; set; }

    public string LocationName { get; set; } = string.Empty;
}
