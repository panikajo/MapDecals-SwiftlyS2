using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Translation;

namespace MapDecals.Menus;

/// <summary>
/// Central menu manager for Map Decals
/// Handles all menu navigation and display
/// Split into partial classes for organization
/// </summary>
public sealed partial class MenuManager
{
    // =========================================
    // =           FIELDS
    // =========================================

    internal readonly Plugin.DatabaseService _database;
    internal PluginConfig _config => Plugin.Config.CurrentValue;

    // =========================================
    // =           CONSTRUCTOR
    // =========================================

    public MenuManager(Plugin.DatabaseService database, PluginConfig config)
    {
        _database = database;

    }

    // =========================================
    // =           PUBLIC API
    // =========================================

    /// <summary>Opens the main menu for decal placement</summary>
    public void OpenMainMenu(IPlayer player, ILocalizer localizer)
    {
        if (Plugin.Config.CurrentValue.UsePanoramaMenu) Plugin.Panorama.OpenMain(player);
        else DecalMenu.Show(this, player, localizer);
    }

    /// <summary>Opens decal menu to edit decal placement</summary>
    public void OpenDecalMenu(IPlayer player, ILocalizer localizer, MapDecal decal)
    {
        if (Plugin.Config.CurrentValue.UsePanoramaMenu) Plugin.Panorama.OpenEdit(player, decal);
        else EditDecalMenu.Show(this, player, localizer, decal);
    }
}