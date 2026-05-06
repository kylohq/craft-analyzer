namespace CraftAnalyzer.Models;

public class CartItem
{
    public uint ItemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
}
