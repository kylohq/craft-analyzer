using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CraftAnalyzer.Models;

/// <summary>
/// Represents the root response from the Universalis API for multiple items.
/// </summary>
public class UniversalisResponse
{
    [JsonPropertyName("items")]
    public Dictionary<string, ItemData> Items { get; set; } = new();
}

/// <summary>
/// Data container for a single item's market information.
/// </summary>
public class ItemData
{
    [JsonPropertyName("itemID")]
    public uint ItemID { get; set; }

    [JsonPropertyName("listings")]
    public List<Listing> Listings { get; set; } = new();

    [JsonPropertyName("recentHistory")]
    public List<HistoryEntry> RecentHistory { get; set; } = new();
}

/// <summary>
/// Represents a single active market board listing.
/// </summary>
public class Listing
{
    [JsonPropertyName("pricePerUnit")]
    public int PricePerUnit { get; set; }

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }

    [JsonPropertyName("worldName")]
    public string WorldName { get; set; } = string.Empty;

    [JsonPropertyName("hq")]
    public bool IsHq { get; set; }
}

/// <summary>
/// Represents a historical sale record.
/// </summary>
public class HistoryEntry
{
    [JsonPropertyName("pricePerUnit")]
    public int PricePerUnit { get; set; }

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }
}

