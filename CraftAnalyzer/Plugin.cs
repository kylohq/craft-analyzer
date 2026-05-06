using System;
using System.Reflection;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Dalamud.Game.Gui.ContextMenu;
using CraftAnalyzer.Windows;
using CraftAnalyzer.Services;
using CraftAnalyzer.Models;
using System.Collections.Generic;
using System.Linq;
using Lumina.Excel.Sheets;

namespace CraftAnalyzer;

/// <summary>
/// Main entry point for the CraftAnalyzer plugin.
/// Handles service initialization, UI management, and context menu integration.
/// </summary>
public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IContextMenu ContextMenu { get; private set; } = null!;
    [PluginService] internal static IToastGui ToastGui { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    public static RecipeParserService RecipeParser { get; private set; } = null!;
    public static UniversalisService Universalis { get; private set; } = null!;

    private const string CommandName = "/craftanalyzer";

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("CraftAnalyzer");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }
    
    public List<CartItem> ShoppingCart { get; } = new();

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        RecipeParser = new RecipeParserService(DataManager);
        Universalis = new UniversalisService(ObjectTable);

        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);

        ContextMenu.OnMenuOpened += OnMenuOpened;

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens the main menu for CraftAnalyzer."
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        Log.Information($"{PluginInterface.Manifest.Name} initialized successfully.");
    }

    public void Dispose()
    {
        ContextMenu.OnMenuOpened -= OnMenuOpened;

        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        
        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();
        MainWindow.Dispose();

        CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args)
    {
        MainWindow.Toggle();
    }

    /// <summary>
    /// Occurs when a context menu is opened. Attempts to resolve the item ID from the menu target.
    /// </summary>
    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        uint itemId = 0;
        
        // Attempt to resolve item ID from the context menu target object
        if (args.Target != null)
        {
            var target = args.Target;
            var type = target.GetType();

            var itemIdProp = type.GetProperty("ItemId", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase) 
                          ?? type.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            
            if (itemIdProp != null)
            {
                try
                {
                    var val = itemIdProp.GetValue(target);
                    if (val != null)
                    {
                        itemId = Convert.ToUInt32(val);
                    }
                }
                catch { }
            }
        }

        // Fallback to hovered item if context menu target resolution failed
        if (itemId == 0 && GameGui.HoveredItem != 0)
        {
            itemId = (uint)GameGui.HoveredItem;
        }

        // Normalize Item ID (handle HQ/Collectable offsets)
        if (itemId > 0)
        {
            if (itemId > 1000000) itemId -= 1000000;
            if (itemId > 500000) itemId -= 500000;
        }
        
        // Add context menu option if the item is valid and craftable
        if (itemId > 0 && RecipeParser.IsCraftable(itemId))
        {
            args.AddMenuItem(new MenuItem
            {
                Name = "Add to Shopping Cart",
                OnClicked = _ => 
                {
                    AddToCart(itemId);
                }
            });
        }
    }

    /// <summary>
    /// Adds an item to the shopping cart or increments quantity if it exists.
    /// </summary>
    public void AddToCart(uint itemId)
    {
        var existing = ShoppingCart.FirstOrDefault(x => x.ItemId == itemId);
        if (existing != null)
        {
            existing.Quantity++;
            ToastGui.ShowNormal($"Incremented {existing.Name} in cart.");
        }
        else
        {
            var itemRow = DataManager.GetExcelSheet<Item>().GetRow(itemId);
            var name = itemRow.Name.ToString();
            ShoppingCart.Add(new CartItem 
            { 
                ItemId = itemId, 
                Name = name, 
                Quantity = 1 
            });
            ToastGui.ShowNormal($"Added {name} to cart.");
        }
        
        MainWindow.IsOpen = true;
    }
    
    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();
}

