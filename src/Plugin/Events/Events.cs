// Intentional compatibility fallback for servers without Workshop UI resources.
#pragma warning disable CS0618
using MapDecals.Menus;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace MapDecals;

public sealed partial class Plugin
{
    private void RegisterGameEvents()
    {
        Core.GameEvent.HookPost<EventRoundPoststart>(OnRoundStart);
        Core.GameEvent.HookPost<EventPlayerPing>(OnPlayerPing);
        Core.GameEvent.HookPost<EventPlayerConnectFull>(OnPlayerConnectFull);
        Core.GameEvent.HookPre<EventPlayerDisconnect>(OnPlayerDisconnect);

        Core.Event.OnPrecacheResource += OnPrecacheResource;
        Core.Event.OnMapLoad += OnMapLoad;
    }

    private void OnMapLoad(IOnMapLoadEvent @event)
    {
        MapGeneration++;
        _debugMap = @event.MapName;
        DebugEvent("MAP_LOAD", $"newMap={@event.MapName}");
        Panorama.Reset();
        _applyingDecal.Clear();
        LoadMapDecals(@event.MapName);
        _warnErrorDecal = false;
    }

    private void OnPrecacheResource(IOnPrecacheResourceEvent @event)
    {
        DebugEvent("PRECACHE_BEGIN", $"props={Config.CurrentValue.Props.Count} panorama={Config.CurrentValue.UsePanoramaMenu}; AddItem records a request, not completion");
        if (Config.CurrentValue.UsePanoramaMenu)
        {
            @event.AddItem(Config.CurrentValue.PanoramaLayout);
            DebugEvent("PRECACHE_REQUEST", $"kind=layout path={Config.CurrentValue.PanoramaLayout}");
        }
        var props = Config.CurrentValue.Props;
        if (props.Count() > 0)
        {
            foreach (var prop in props)
            {
                @event.AddItem(prop.Material);
                DebugEvent("PRECACHE_REQUEST", $"kind=material uniqId={prop.UniqId} path={prop.Material}");
            }
        }
    }

    private HookResult OnRoundStart(EventRoundPoststart @event)
    {
        DebugEvent("ROUND_RESPAWN", $"records={_mapDecals.Count} active={_mapDecals.Count(d => d.IsActive)}");
        foreach (var decal in _mapDecals)
        {
            // CS2 may preserve entities across a round; never create a duplicate.
            var old = decal.GetEntity();
            if (old != null) old.Despawn();
            decal.Entity = default;
            if (!decal.IsActive) continue;

            var entityHandle = CreateDecal(
                decal.Position,
                decal.Angles,
                decal.DecalId,
                decal.Depth,
                decal.Width,
                decal.Height,
                decal.ForceOnVIP,
                decal
            );

            DebugEvent("ROUND_RECORD", $"id={decal.Id} uniqId={decal.DecalId} created={entityHandle.HasValue}");
            if (entityHandle == null) continue;

            decal.Entity = (CHandle<CEnvDecal>)entityHandle;
        }
        return HookResult.Continue;
    }

    private HookResult OnPlayerPing(EventPlayerPing @event)
    {
        var player = @event.Accessor.GetPlayer("userid");
        if (player == null || !player.IsValid || player.IsFakeClient) return HookResult.Continue;

        var steamid = player.SteamID;

        if (!_applyingDecal.TryGetValue(steamid, out var decal) || decal == null) return HookResult.Continue;

        var pawn = player.PlayerPawn;
        bool allowed = PanoramaDecalMenu.CanManage(player);
        DebugEvent("PLACEMENT_PING", $"slot={player.PlayerID} uniqId={decal.UniqId} material={decal.Material} ping=[{DecalRules.Triple(@event.X, @event.Y, @event.Z)}] pawnPresent={pawn != null} lifeState={pawn?.LifeState} adminAllowed={allowed}");
        if (pawn == null || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE || !allowed)
        { DebugEvent("PLACEMENT_REJECT", "missing pawn, not alive, or missing admin permission; pending selection retained"); return HookResult.Continue; }

        _applyingDecal.TryRemove(steamid, out _);
        Panorama.Close(player.PlayerID);

        var menu = Core.MenusAPI.GetCurrentMenu(player);
        if (menu != null)
            Core.MenusAPI.CloseMenuForPlayer(player, menu);

        CreateDecalOnPing(pawn, new(@event.X, @event.Y, @event.Z), decal);
        return HookResult.Continue;
    }

    private HookResult OnPlayerConnectFull(EventPlayerConnectFull @event)
    {
        var player = @event.Accessor.GetPlayer("userid");
        if (player == null || !player.IsValid || player.IsFakeClient) return HookResult.Continue;

        LoadPlayerPrefs(player);

        return HookResult.Continue;
    }

    private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event)
    {
        var player = @event.Accessor.GetPlayer("userid");
        if (player == null || player.IsFakeClient) return HookResult.Continue;
        _lastDebugCommand.Remove(player.PlayerID);
        Panorama.OnPlayerDisconnected(player.PlayerID);
        _applyingDecal.TryRemove(player.SteamID, out _);
        if (_adDecalStatus.TryRemove(player.PlayerID, out var status) && PlayerCookiesAPIv1 != null)
        {
            PlayerCookiesAPIv1.Set(player, MapDecalCookieKey, status);
            _ = SavePrefsAsync(player);
        }
        return HookResult.Continue;
    }

    private async Task SavePrefsAsync(SwiftlyS2.Shared.Players.IPlayer player)
    {
        try { if (PlayerCookiesAPIv1 != null) await PlayerCookiesAPIv1.Save(player); }
        catch (Exception ex) { Core.Logger.LogWarning(ex, "Could not save decal preferences"); }
    }
}
