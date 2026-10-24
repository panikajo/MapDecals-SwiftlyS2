using MapDecals.Menus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Plugins;

using Cookies.Contract;

namespace MapDecals;

[PluginMetadata(
    Id = "cc.mapdecals",
    Version = "1.1.4",
    Name = "Map Decals",
    Author = "Cruze",
    Description = "Place map decals inside map",
    MinimumAPIVersion = "1.5.0-beta.26"
)]
public sealed partial class Plugin(ISwiftlyCore core) : BasePlugin(core)
{
    /* ==================== Static Access ==================== */
    public static new ISwiftlyCore Core { get; private set; } = null!;

    /* ==================== Configurations ==================== */
    internal static IOptionsMonitor<PluginConfig> Config { get; private set; } = null!;

    /* ==================== Services ==================== */
    internal static DatabaseService Database { get; private set; } = null!;
    internal static MenuManager MenuManager { get; private set; } = null!;
    internal static PanoramaDecalMenu Panorama { get; private set; } = null!;
    private ServiceProvider? _configProvider;
    internal static bool Running { get; private set; }
    internal static int MapGeneration { get; private set; }

    /* ==================== Shared API ==================== */
    internal IPlayerCookiesAPIv1? PlayerCookiesAPIv1 { get; private set; } = null;
    internal const string MapDecalCookieKey = "cc_mapdecals.v1";

    public override void Load(bool hotReload)
    {
        Core = base.Core;
        Running = true;
        MapGeneration++;

        InitializeConfigs();
        InitializeDiagnostics(hotReload);
        Database = new DatabaseService("cc_mapdecals", hotReload);

        MenuManager = new MenuManager(Database, Config.CurrentValue);
        Panorama = new PanoramaDecalMenu();
        RegisterCommands();
        RegisterGameEvents();
        Database.Start();
        foreach (var player in Core.PlayerManager.GetAllPlayers())
            if (player.IsValid && !player.IsFakeClient) LoadPlayerPrefs(player);
    }

    /* ==================== Configuration Loading ==================== */
    private void InitializeConfigs()
    {
        Config = BuildConfigService<PluginConfig>("config.json", "MapDecals");
    }

    private IOptionsMonitor<T> BuildConfigService<T>(string fileName, string sectionName) where T : class, new()
    {
        Core.Configuration
            .InitializeJsonWithModel<T>(fileName, sectionName)
            .Configure(builder =>
            {
                builder.AddJsonFile(Core.Configuration.GetConfigPath(fileName), optional: false, reloadOnChange: true);
            });

        ServiceCollection services = new();
        services.AddSwiftly(Core)
            .AddOptionsWithValidateOnStart<T>()
            .BindConfiguration(sectionName);

        _configProvider = services.BuildServiceProvider();
        return _configProvider.GetRequiredService<IOptionsMonitor<T>>();
    }

    /* ==================== Shared API ==================== */
    public override void UseSharedInterface(IInterfaceManager interfaceManager)
    {
        DebugEvent("COOKIES_API", $"available={interfaceManager.HasSharedInterface("Cookies.Player.v1")}");
        if (interfaceManager.HasSharedInterface("Cookies.Player.v1"))
        {
            PlayerCookiesAPIv1 = interfaceManager.GetSharedInterface<IPlayerCookiesAPIv1>("Cookies.Player.v1");

            foreach (var player in Core.PlayerManager.GetAllPlayers())
            {
                if (player.IsFakeClient) continue;

                LoadPlayerPrefs(player);
            }
        }
    }

    /* ==================== Unload ==================== */
    public override void Unload()
    {
        DebugEvent("UNLOAD", $"records={_mapDecals.Count} pendingPlacements={_applyingDecal.Count}");
        Running = false;
        MapGeneration++;
        Core.Event.OnPrecacheResource -= OnPrecacheResource;
        Core.Event.OnMapLoad -= OnMapLoad;
        Panorama.Dispose();
        foreach (var decal in _mapDecals) decal.DeleteEntity();
        _mapDecals.Clear();
        _applyingDecal.Clear();
        _adDecalStatus.Clear();
        _configProvider?.Dispose();
    }
}