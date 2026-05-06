namespace CraftAnalyzer.Models;

public record MaterialData(
    uint ItemId,
    string Name,
    int TotalNeeded,
    int AmountOwned
);
