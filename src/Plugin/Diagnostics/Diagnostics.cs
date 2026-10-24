using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.FileSystem;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace MapDecals;

public sealed partial class Plugin
{
    private static DiagnosticLog? _debugLog;
    private static string _debugMap = "<not-initialized>";
    private readonly Dictionary<int, long> _lastDebugCommand = [];
    internal static bool DebugEnabled => Running && Config?.CurrentValue.DebugLogging == true;

    private static void InitializeDiagnostics(bool hotReload)
    {
        _debugMap = Core.Engine.GlobalVars.MapName;
        _debugLog = new DiagnosticLog(System.IO.Path.Combine(Core.PluginDataDirectory, "logs", "mapdecals-debug.log"),
            line => Core.Logger.LogInformation("{DiagnosticLine}", line),
            line => Core.Logger.LogWarning("{DiagnosticWarning}", line));
        Core.Logger.LogInformation("MapDecals 1.1.3 diagnostics: DebugLogging={Enabled}; file={File}. Run mapdecaldebug for a read-only snapshot.",
            Config.CurrentValue.DebugLogging, _debugLog.Path);
        DebugEvent("LOAD", $"version=1.1.3 api=1.5.0-beta.26 hotReload={hotReload} panorama={Config.CurrentValue.UsePanoramaMenu} layout={Config.CurrentValue.PanoramaLayout}");
        DebugEvent("LIMIT", "server filesystem/transmit checks do NOT prove client mounting, texture/shader validity, successful precache completion or visible pixels");
        foreach (var prop in Config.CurrentValue.Props.Take(64))
            DebugEvent("CONFIG_PROP", $"uniqId={prop.UniqId} name={prop.Name} material={prop.Material} showPermission={prop.ShowPermission}");
        if (DebugEnabled)
        {
            foreach (var prop in Config.CurrentValue.Props.Take(64)) DebugProbeMaterial(prop.Material);
        }
    }

    internal static void DebugEvent(string stage, string message, bool force = false)
    {
        if (!force && !DebugEnabled) return;
        _debugLog?.Write(stage, $"map={_debugMap} generation={MapGeneration} thread={Environment.CurrentManagedThreadId} {message}",
            Config?.CurrentValue.DebugLogToFile != false);
    }

    /// <summary>Main thread only. Tests server's mounted GAME namespace, including VPK search paths.</summary>
    private static void DebugProbeMaterial(string material, bool force = false)
    {
        if (!force && !DebugEnabled) return;
        string? compiled = DiagnosticPaths.CompiledMaterial(material);
        if (compiled == null)
        {
            DebugEvent("MATERIAL_PATH_INVALID", $"configured={material}; expected a relative materials/.../*.vmat path", force);
            return;
        }
        try
        {
            bool found = Core.GameFileSystem.FileExists(compiled, "GAME");
            uint size = found ? Core.GameFileSystem.GetFileSize(compiled, "GAME") : 0;
            DebugEvent("MATERIAL_VFS", $"configured={material} compiled={compiled} serverGameExists={found} bytes={size} clientState=UNKNOWN dependencies=NOT_INSPECTED", force);
            if (!found)
                DebugEvent("MATERIAL_HINT", "compiled material not resolved by server GAME filesystem at this moment; check installed/mounted content and exact path/case; this does not inspect the client", force);
            string lower = compiled.ToLowerInvariant();
            if (lower != compiled)
                DebugEvent("MATERIAL_CASE", $"lowercase={lower} serverGameExists={Core.GameFileSystem.FileExists(lower, "GAME")}", force);
        }
        catch (Exception ex)
        {
            DebugEvent("MATERIAL_CHECK_ERROR", $"compiled={compiled} error={ex.GetType().Name}: {ex.Message}; existence=UNKNOWN", force);
        }
    }

    private static void DebugEntity(string stage, CEnvDecal? entity, bool force = false)
    {
        if (!force && !DebugEnabled) return;
        try
        {
            if (entity is not { IsValid: true }) { DebugEvent(stage, "entityValid=false", force); return; }
            var origin = entity.AbsOrigin;
            var rotation = entity.AbsRotation;
            string pos = origin.HasValue ? DecalRules.Triple(origin.Value.X, origin.Value.Y, origin.Value.Z) : "<null>";
            string ang = rotation.HasValue ? DecalRules.Triple(rotation.Value.X, rotation.Value.Y, rotation.Value.Z) : "<null>";
            DebugEvent(stage, $"entity={entity.Index} valid=true origin=[{pos}] angles=[{ang}] width={DecalRules.Number(entity.Width)} height={DecalRules.Number(entity.Height)} depth={DecalRules.Number(entity.Depth)} projectOnWorld={entity.ProjectOnWorld} renderMode={entity.RenderMode} renderOrder={entity.RenderOrder} materialHandleValid={entity.DecalMaterial.IsValid} clientRendering=UNKNOWN", force);
        }
        catch (Exception ex) { DebugEvent(stage + "_ERROR", $"error={ex.GetType().Name}: {ex.Message}", force); }
    }

    private static void DebugPlayerView(IPlayer player, CEnvDecal entity, ConfigDecals prop, bool forced, bool force = false)
    {
        if (!force && (!DebugEnabled || !Config.CurrentValue.DebugPlayerTransmit)) return;
        try
        {
            if (!player.IsValid || player.IsFakeClient || !entity.IsValid) return;
            bool permitted = string.IsNullOrWhiteSpace(prop.ShowPermission)
                || Core.Permission.PlayerHasPermission(player.SteamID, prop.ShowPermission);
            bool hasPreference = _adDecalStatus.TryGetValue(player.PlayerID, out bool saved);
            bool preference = !hasPreference || saved;
            bool expected = DecalRules.IsVisible(permitted, forced, preference);
            bool actual = entity.IsTransmitting(player.PlayerID);
            DebugEvent("TRANSMIT", $"entity={entity.Index} playerSlot={player.PlayerID} uniqId={prop.UniqId} showPermission={prop.ShowPermission} permitted={permitted} forced={forced} preferenceKnown={hasPreference} preference={preference} expected={expected} serverTransmitFlag={actual} match={expected == actual} clientRendered=UNKNOWN", force);
        }
        catch (Exception ex) { DebugEvent("TRANSMIT_CHECK_ERROR", $"playerSlot={player.PlayerID} error={ex.GetType().Name}: {ex.Message}", force); }
    }

    private void OnDebugCommand(ICommandContext ctx)
    {
        // Diagnostic command is read-only, but server paths and placements are admin information.
        if (ctx.IsSentByPlayer && (ctx.Sender == null || !Menus.PanoramaDecalMenu.CanManage(ctx.Sender)))
        { ctx.Reply("[MapDecals] Admin permission required."); return; }
        int slot = ctx.Sender?.PlayerID ?? -1;
        long now = Environment.TickCount64;
        if (_lastDebugCommand.TryGetValue(slot, out long last) && now - last < 2000)
        { ctx.Reply("[MapDecals] Wait 2 seconds between diagnostic snapshots."); return; }
        _lastDebugCommand[slot] = now;
        if (ctx.Args.Length > 1 || (ctx.Args.Length == 1 && ctx.Args[0] != "paths" && !ulong.TryParse(ctx.Args[0], out _)))
        { ctx.Reply("[MapDecals] Usage: mapdecaldebug [decal-id|paths]"); return; }
        ulong? id = ctx.Args.Length == 1 && ulong.TryParse(ctx.Args[0], out ulong parsed) ? parsed : null;
        try
        {
            _debugMap = Core.Engine.GlobalVars.MapName;
            DebugEvent("REPORT_BEGIN", $"version=1.1.3 id={(id.HasValue ? id.Value.ToString() : "all")} source={(ctx.IsSentByPlayer ? $"playerSlot={slot}" : "server-console")} automaticLogging={DebugEnabled} fileLogging={Config.CurrentValue.DebugLogToFile}", true);
            DebugEvent("SERVER", $"gameFiles={Core.GameFilesDirectory} pluginData={Core.PluginDataDirectory} config={Core.Configuration.GetConfigPath("config.json")} dbEnabled={Database.IsEnabled} dbInitialization={Database.Initialization.Status} activeRecords={_mapDecals.Count} pendingPlacements={_applyingDecal.Count} panorama={Config.CurrentValue.UsePanoramaMenu} layout={Config.CurrentValue.PanoramaLayout}", true);
            DebugSearchPaths();
            if (ctx.Args.Length == 1 && ctx.Args[0] == "paths")
            { FinishDebugReport(ctx); return; }
            var checkedMaterials = new HashSet<string>(StringComparer.Ordinal);
            foreach (var prop in Config.CurrentValue.Props.Take(64))
            {
                DebugEvent("CONFIG_PROP", $"uniqId={prop.UniqId} name={prop.Name} material={prop.Material} showPermission={prop.ShowPermission}", true);
                if (checkedMaterials.Add(prop.Material)) DebugProbeMaterial(prop.Material, true);
            }
            if (Config.CurrentValue.Props.Count > 64) DebugEvent("REPORT_LIMIT", "only first 64 configured materials shown", true);
            int records = 0, views = 0;
            var candidates = _mapDecals.Where(d => !id.HasValue || d.Id == id.Value).ToArray();
            foreach (var decal in candidates.Take(32))
            {
                records++;
                DebugEvent("RECORD", $"id={decal.Id} name={decal.DecalName} storedMap={decal.Map} uniqId={decal.DecalId} active={decal.IsActive} forced={decal.ForceOnVIP} storedPosition=[{decal.Position}] storedAngles=[{decal.Angles}] width={DecalRules.Number(decal.Width)} height={DecalRules.Number(decal.Height)} depth={decal.Depth} handleValid={decal.Entity.IsValid}", true);
                var prop = Config.CurrentValue.Props.FirstOrDefault(p => p.UniqId == decal.DecalId);
                if (prop == null) DebugEvent("RECORD_MISSING_CONFIG", $"id={decal.Id} uniqId={decal.DecalId}; no matching Props entry", true);
                var entity = decal.GetEntity();
                DebugEntity("ENTITY_SNAPSHOT", entity, true);
                if (prop == null || entity == null) continue;
                foreach (var player in Core.PlayerManager.GetAllPlayers())
                {
                    if (!player.IsValid || player.IsFakeClient || (ctx.IsSentByPlayer && player.PlayerID != slot)) continue;
                    if (views >= 128) break;
                    DebugPlayerView(player, entity, prop, decal.ForceOnVIP, true); views++;
                }
            }
            if (records == 0) DebugEvent("REPORT_NO_RECORDS", $"no matching in-memory record for id={id}; this is not a fresh SQL query", true);
            if (candidates.Length > 32 || views >= 128) DebugEvent("REPORT_LIMIT", "snapshot capped at 32 records / 128 player-entity pairs; request a specific decal ID", true);
            DebugEvent("LIMIT", "material existence, valid handle and transmit flag do NOT prove texture dependencies, shader compatibility, client mounting or visible pixels. Orientation uses player eye angles, not a traced surface normal.", true);
            FinishDebugReport(ctx);
        }
        catch (Exception ex)
        {
            DebugEvent("REPORT_ERROR", $"error={ex.GetType().Name}: {ex.Message}", true);
            ctx.Reply("[MapDecals] Diagnostic snapshot failed; check MDDBG lines in the server log.");
        }
    }

    private static void DebugSearchPaths()
    {
        try
        {
            var paths = Core.GameFileSystem.GetSearchPath("GAME", GetSearchPathTypes_t.GET_SEARCH_PATH_ALL, 64);
            int i = 0;
            foreach (string path in paths.Split(';', StringSplitOptions.RemoveEmptyEntries).Take(64))
                DebugEvent("SEARCH_PATH", $"index={i++} pathId=GAME path={path}", true);
            if (i == 0) DebugEvent("SEARCH_PATH", "no paths returned by API; not proof that no content is mounted", true);
        }
        catch (Exception ex) { DebugEvent("SEARCH_PATH_ERROR", $"error={ex.GetType().Name}: {ex.Message}", true); }
    }

    private static void FinishDebugReport(ICommandContext ctx)
    {
        DebugEvent("REPORT_END", "readOnly=true; no entities, permissions, filesystems or preferences modified", true);
        ctx.Reply("[MapDecals] Report written to server console (MDDBG). File: "
            + (Config.CurrentValue.DebugLogToFile ? _debugLog?.Path : "disabled by DebugLogToFile=false"));
    }
}
