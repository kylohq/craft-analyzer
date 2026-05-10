using System.Collections.Generic;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using CraftAnalyzer.Models;
using System;
using System.Linq;

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
    /// Calculates the materials required to craft a specified quantity of an item.
    /// </summary>
    /// <param name="itemId">The ID of the item to analyze.</param>
    /// <param name="quantity">The target quantity to produce.</param>
    /// <param name="recursive">If true, breaks down pre-crafts into raw materials.</param>
    /// <returns>A dictionary of material IDs and their required quantities.</returns>
    public Dictionary<uint, float> GetBaseMaterials(uint itemId, float quantity = 1.0f, bool recursive = true)
    {
        var baseMaterials = new Dictionary<uint, float>();
        if (recursive)
        {
            ParseRecursive(itemId, quantity, baseMaterials);
        }
        else
        {
            return GetImmediateIngredients(itemId, quantity);
        }
        return baseMaterials;
    }

    /// <summary>
    /// Calculates the immediate ingredients required for an item (no recursion).
    /// </summary>
    public Dictionary<uint, float> GetImmediateIngredients(uint itemId, float quantity = 1.0f)
    {
        var ingredients = new Dictionary<uint, float>();
        if (itemToRecipe.TryGetValue(itemId, out var recipe))
        {
            float amountResult = recipe.AmountResult;
            if (amountResult <= 0) amountResult = 1;
            float perUnitQuantity = quantity / amountResult;

            for (int i = 0; i < recipe.Ingredient.Count; i++)
            {
                var ingredientId = recipe.Ingredient[i].RowId;
                var ingredientAmount = recipe.AmountIngredient[i];
                if (ingredientId == 0 || ingredientAmount == 0) continue;

                float totalRequired = ingredientAmount * perUnitQuantity;
                if (ingredients.ContainsKey(ingredientId))
                    ingredients[ingredientId] += totalRequired;
                else
                    ingredients[ingredientId] = totalRequired;
            }
        }
        else
        {
            ingredients[itemId] = quantity;
        }
        return ingredients;
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

    /// <summary>
    /// Aggregates materials for a collection of cart items.
    /// </summary>
    public Dictionary<uint, int> GetAggregateMaterials(IEnumerable<CartItem> cartItems, bool recursive = true)
    {
        var aggregate = new Dictionary<uint, float>();
        foreach (var item in cartItems)
        {
            if (recursive)
            {
                ParseRecursive(item.ItemId, item.Quantity, aggregate);
            }
            else
            {
                var immediate = GetImmediateIngredients(item.ItemId, item.Quantity);
                foreach (var kvp in immediate)
                {
                    if (aggregate.ContainsKey(kvp.Key))
                        aggregate[kvp.Key] += kvp.Value;
                    else
                        aggregate[kvp.Key] = kvp.Value;
                }
            }
        }

        return aggregate.ToDictionary(k => k.Key, v => (int)Math.Ceiling(v.Value));
    }
}

