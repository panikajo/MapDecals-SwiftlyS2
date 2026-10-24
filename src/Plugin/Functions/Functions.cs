using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.EntitySystem;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace MapDecals;

public sealed partial class Plugin
{
    public const float DecalWidth = 128;
    public const float DecalHeight = 128;
    public const int DecalDepth = 12;

    private static ConcurrentDictionary<int, bool> _adDecalStatus = new();
    public static List<MapDecal> _mapDecals = new();
    public static ConcurrentDictionary<ulong, ConfigDecals?> _applyingDecal = new();
    public static bool _warnErrorDecal = false;

    private void CreateDecalOnPing(CCSPlayerPawn pawn, Vector position, ConfigDecals decal)
    {
        if (pawn == null)
            return;

        try
        {
            var eyeAngles = pawn.EyeAngles;

            // Flip yaw to face the player
            float flippedYaw = (eyeAngles.Y + 180.0f) % 360.0f;
            float yawRad = flippedYaw * MathF.PI / 180f;

            // Backward direction (already unit length in XY plane)
            Vector backward = new(
                -MathF.Cos(yawRad),
                -MathF.Sin(yawRad),
                0f
            );

            Vector decalPos = position + backward * 2f;

            float pitchRad = eyeAngles.X * MathF.PI / 180f;
            float eyeZ = -MathF.Sin(pitchRad);

            var angle = new QAngle(0f, flippedYaw, 0f);

            CHandle<CEnvDecal>? entityHandle;

            // If looking down steeply, push decal slightly up
            if (eyeZ < -0.90f)
            {
                decalPos.Z += 1f;
                entityHandle = CreateDecal(
                    decalPos,
                    angle,
                    decal,
                    DecalDepth,
                    DecalWidth,
                    DecalHeight,
                    false
                );
                if (entityHandle == null) return;
                SaveDecalPosition(pawn, decal, decalPos, angle, DecalDepth, DecalWidth, DecalHeight, (CHandle<CEnvDecal>)entityHandle);
                return;
            }

            // Default wall decal
            angle.X = 90.0f;
            entityHandle = CreateDecal(
                decalPos,
                angle,
                decal,
                DecalDepth,
                DecalWidth,
                DecalHeight,
                false
            );
            if (entityHandle == null) return;
            SaveDecalPosition(pawn, decal, decalPos, angle, DecalDepth, DecalWidth, DecalHeight, (CHandle<CEnvDecal>)entityHandle);
        }
        catch (Exception ex)
        {
            Core.Logger.LogError($"Error while creating decal: {ex}");
        }
    }

    private static void SaveDecalPosition(CCSPlayerPawn pawn, ConfigDecals decal, Vector position, QAngle angle, int depth, float width, float height, CHandle<CEnvDecal> entityHandle)
    {
        if (!entityHandle.IsValid) return;

        var player = pawn.ToPlayer()!;

        var localizer = Core.Translation.GetPlayerLocalizer(player);

        var mapdecal = new MapDecal
        {
            Map = Core.Engine.GlobalVars.MapName,
            DecalId = decal.UniqId,

            DecalName = $"{pawn.LastPlaceName}_{Core.Engine.GlobalVars.TickCount}",

            Position = DecalRules.Triple(position.X, position.Y, position.Z),
            Angles = DecalRules.Triple(angle.X, angle.Y, angle.Z),

            Depth = depth,
            Width = width,
            Height = height,

            ForceOnVIP = false,
            IsActive = true,
            Entity = entityHandle
        };

        var generation = MapGeneration;
        var steamId = player.SteamID;
        _ = Task.Run(async () =>
        {
            var id = await Database.SaveDecal(mapdecal);

            if (id != null)
            {
                mapdecal.Id = (ulong)id;
            }
            Core.Scheduler.NextWorldUpdate(() =>
            {
                if (!Running || generation != MapGeneration) return;
                if (id == null)
                {
                    mapdecal.DeleteEntity();
                    if (player.IsValid && player.SteamID == steamId) player.SendChat("[MapDecals] Database save failed.");
                    return;
                }
                _mapDecals.Add(mapdecal);
                if (player.IsValid && player.SteamID == steamId)
                    MenuManager.OpenDecalMenu(player, localizer, mapdecal);
            });
        });
    }

    public static CHandle<CEnvDecal>? CreateDecal(string position, string ang, string uniqueid, int depth, float width, float height, bool forceOnVip, MapDecal mapdecal)
    {
        var decal = Config.CurrentValue.Props.FirstOrDefault(d => d.UniqId == uniqueid);
        if (!Vector.TryDeserialize(position, out var cords) || !QAngle.TryDeserialize(ang, out var angle)) return null;

        if (decal == null)
        {
            if (!_warnErrorDecal)
            {
                Core.Logger.LogError($"Failed to spawn {mapdecal.DecalName} because unable to find any decal with uniqueid `{uniqueid}` in config.json.");
                _warnErrorDecal = true;
            }
            return null;
        }

        return CreateDecal(
            cords,
            angle,
            decal,
            depth,
            width,
            height,
            forceOnVip
        );
    }

    public static CHandle<CEnvDecal>? CreateDecal(Vector cords, QAngle angle, ConfigDecals decal, int depth, float width, float height, bool forceOnVip)
    {
        using (var keyValues = new CEntityKeyValues())
        {
            var entity = Core.EntitySystem.CreateEntityByDesignerName<CEnvDecal>("env_decal");
            if (entity == null) return null;

            try
            {
                entity.Entity!.Name = $"cc_decal_{Core.Engine.GlobalVars.TickCount}";

                if (forceOnVip)
                {
                    entity.Entity!.Name = $"cc_forcedecal_{Core.Engine.GlobalVars.TickCount}";
                }
                keyValues.SetString("targetname", entity.Entity.Name);
                keyValues.SetString("material", decal.Material);
                entity.Width = width;
                entity.Height = height;
                entity.Depth = depth;
                entity.RenderOrder = 1;
                entity.RenderMode = RenderMode_t.kRenderNormal;

                entity.ProjectOnWorld = true;

                entity.Teleport(cords, angle, null);
                entity.DispatchSpawn(keyValues);

                entity.SetTransmitState(true);

                foreach (var player in Core.PlayerManager.GetAllPlayers())
                {
                    if (!player.IsValid || player.IsFakeClient) continue;
                    UpdateDecalTransmit(player, entity, decal, forceOnVip);
                }

                return Core.EntitySystem.GetRefEHandle(entity);
            }
            catch (Exception error)
            {
                if (entity.IsValid) entity.Despawn();
                Core.Logger.LogError($"Error while creating decal: {error}");
                return null;
            }
        }
    }

    public static void UpdateDecalTransmit(IPlayer player, CEnvDecal entity, string uniqueid, bool forceOnVip)
    {
        var cdecal = Config.CurrentValue.Props.FirstOrDefault(d => d.UniqId == uniqueid);

        if (cdecal == null) return;

        UpdateDecalTransmit(player, entity, cdecal, forceOnVip);
    }

    public static void UpdateDecalTransmit(IPlayer player, CEnvDecal? entity = null, ConfigDecals? cdecal = null, bool? forceOnVip = null)
    {
        if (!player.IsValid || player.IsFakeClient) return;
        if (entity != null && cdecal != null && forceOnVip.HasValue)
        {
            if (!entity.IsValid) return;
            var permitted = string.IsNullOrWhiteSpace(cdecal.ShowPermission)
                || Core.Permission.PlayerHasPermission(player.SteamID, cdecal.ShowPermission);
            var preference = !_adDecalStatus.TryGetValue(player.PlayerID, out var status) || status;
            entity.SetTransmitState(DecalRules.IsVisible(permitted, forceOnVip.Value, preference), player.PlayerID);
            return;
        }
        foreach (var decal in _mapDecals)
        {
            var material = Config.CurrentValue.Props.FirstOrDefault(d => d.UniqId == decal.DecalId);
            var current = decal.GetEntity();
            if (material != null && current != null)
                UpdateDecalTransmit(player, current, material, decal.ForceOnVIP);
        }
    }

    private static void LoadMapDecals(string map)
    {
        _mapDecals.Clear();
        var generation = MapGeneration;
        _ = LoadMapDecalsAsync(map, generation);
    }

    private static async Task LoadMapDecalsAsync(string map, int generation)
    {
        var decals = await Task.Run(() => Database.GetDecals(map));
        Core.Scheduler.NextWorldUpdate(() =>
        {
            if (!Running || generation != MapGeneration) return;
            _mapDecals = decals;
            foreach (var decal in _mapDecals)
            {
                if (!decal.IsActive) continue;
                var handle = CreateDecal(decal.Position, decal.Angles, decal.DecalId,
                    decal.Depth, decal.Width, decal.Height, decal.ForceOnVIP, decal);
                if (handle.HasValue) decal.Entity = handle.Value;
            }
        });
    }

    private void LoadPlayerPrefs(IPlayer player)
    {
        _adDecalStatus[player.PlayerID] = true;
        UpdateDecalTransmit(player);
        if (PlayerCookiesAPIv1 != null) _ = LoadPlayerPrefsAsync(player, player.SteamID);
    }

    private async Task LoadPlayerPrefsAsync(IPlayer player, ulong steamId)
    {
        try
        {
            var api = PlayerCookiesAPIv1!;
            await api.Load(player);
            Core.Scheduler.NextWorldUpdate(() =>
            {
                if (!Running || !player.IsValid || player.SteamID != steamId) return;
                _adDecalStatus[player.PlayerID] = !api.Has(player, MapDecalCookieKey) || api.Get<bool>(player, MapDecalCookieKey);
                UpdateDecalTransmit(player);
            });
        }
        catch (Exception ex) { Core.Logger.LogWarning(ex, "Could not load decal preferences"); }
    }
}
