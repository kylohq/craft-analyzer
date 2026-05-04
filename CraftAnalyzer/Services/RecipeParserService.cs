using System.Collections.Generic;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace CraftAnalyzer.Services;

/// <summary>
/// Service responsible for parsing game recipe data and calculating material breakdowns.
/// </summary>
public class RecipeParserService
{
    private readonly IDataManager dataManager;
    private readonly Dictionary<uint, Recipe> itemToRecipe = new();
    private readonly HashSet<uint> gatherableItems = new();

    public RecipeParserService(IDataManager dataManager)
    {
        this.dataManager = dataManager;
        InitializeRecipeMap();
        InitializeGatherableMap();
    }

    /// <summary>
    /// Populates the internal cache of item IDs to their corresponding recipes.
    /// </summary>
    private void InitializeRecipeMap()
    {
        var recipeSheet = dataManager.GetExcelSheet<Recipe>();
        if (recipeSheet == null) return;

        foreach (var recipe in recipeSheet)
        {
            if (recipe.ItemResult.RowId == 0) continue;
            
            // Map the item result to its recipe. In case of multiple recipes, the first encountered is prioritized.
            if (!itemToRecipe.ContainsKey(recipe.ItemResult.RowId))
            {
                itemToRecipe[recipe.ItemResult.RowId] = recipe;
            }
        }
    }

    /// <summary>
    /// Scans gathering sheets to identify items that can be obtained via Miner, Botanist, or Fisher.
    /// </summary>
    private void InitializeGatherableMap()
    {
        // Miner and Botanist
        var gatheringSheet = dataManager.GetExcelSheet<GatheringItem>();
        if (gatheringSheet != null)
        {
            foreach (var gather in gatheringSheet)
            {
                if (gather.Item.RowId != 0)
                    gatherableItems.Add(gather.Item.RowId);
            }
        }
        
        // Fisher (Spearfishing)
        var spearfishingSheet = dataManager.GetExcelSheet<SpearfishingItem>();
        if (spearfishingSheet != null)
        {
            foreach (var fish in spearfishingSheet)
            {
                if (fish.Item.RowId != 0)
                    gatherableItems.Add(fish.Item.RowId);
            }
        }
    }

    /// <summary>
    /// Determines if an item is gatherable via standard gathering professions.
    /// </summary>
    public bool IsGatherable(uint itemId) => gatherableItems.Contains(itemId);

    /// <summary>
    /// Determines if an item is craftable.
    /// </summary>
    /// <param name="itemId">The ID of the item to check.</param>
    /// <returns>True if the item has a known recipe.</returns>
    public bool IsCraftable(uint itemId)
    {
        return itemToRecipe.ContainsKey(itemId);
    }

    /// <summary>
    /// Recursively calculates the base materials required to craft a specified quantity of an item.
    /// </summary>
    /// <param name="itemId">The ID of the item to analyze.</param>
    /// <param name="quantity">The target quantity to produce.</param>
    /// <returns>A dictionary of base material IDs and their required quantities.</returns>
    public Dictionary<uint, float> GetBaseMaterials(uint itemId, float quantity = 1.0f)
    {
        var baseMaterials = new Dictionary<uint, float>();
        ParseRecursive(itemId, quantity, baseMaterials);
        return baseMaterials;
    }

    /// <summary>
    /// Recursively traverses the recipe tree to find raw materials.
    /// </summary>
    private void ParseRecursive(uint itemId, float quantity, Dictionary<uint, float> baseMaterials)
    {
        if (itemToRecipe.TryGetValue(itemId, out var recipe))
        {
            float amountResult = recipe.AmountResult;
            if (amountResult <= 0) amountResult = 1;

            float perUnitQuantity = quantity / amountResult;

            // Iterate through ingredients defined in the recipe
            if (recipe.Ingredient.Count > 0)
            {
                for (int i = 0; i < recipe.Ingredient.Count; i++)
                {
                    var ingredientId = recipe.Ingredient[i].RowId;
                    var ingredientAmount = recipe.AmountIngredient[i];

                    if (ingredientId == 0 || ingredientAmount == 0) continue;

                    float totalRequired = ingredientAmount * perUnitQuantity;
                    ParseRecursive(ingredientId, totalRequired, baseMaterials);
                }
            }
        }
        else
        {
            // If no recipe exists, treat the item as a base material
            if (baseMaterials.ContainsKey(itemId))
                baseMaterials[itemId] += quantity;
            else
                baseMaterials[itemId] = quantity;
        }
    }
}

