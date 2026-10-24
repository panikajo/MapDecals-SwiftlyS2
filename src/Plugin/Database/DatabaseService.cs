using Dommel;
using MapDecals.Database.Migrations;
using Microsoft.Extensions.Logging;

namespace MapDecals;

public sealed partial class Plugin
{
    public sealed partial class DatabaseService
    {
        /* ==================== Fields ==================== */
        private readonly string _connectionName;

        /* ==================== Properties ==================== */
        public bool IsEnabled { get; private set; }

        /* ==================== Constructor ==================== */
        public Task Initialization { get; private set; } = Task.CompletedTask;

        public DatabaseService(string connectionName, bool hotReload)
        {
            _connectionName = connectionName;
        }

        public void Start()
        {
            // Capture engine state on the game thread, not inside Task.Run.
            var map = Core.Engine.GlobalVars.MapName;
            DebugEvent("DB_INIT", "starting migrations; connection credentials excluded");
            Initialization = Task.Run(() =>
            {
                try
                {
                    MigrationRunner.RunMigrations(Core.Database, _connectionName);
                    IsEnabled = true;
                    DebugEvent("DB_READY", "migrations completed enabled=true");
                }
                catch (Exception ex)
                {
                    Core.Logger.LogError(ex, "Failed to initialize database");
                    IsEnabled = false;
                    DebugEvent("DB_INIT_ERROR", $"type={ex.GetType().Name}; see existing error log");
                }
            });
            if (!string.IsNullOrWhiteSpace(map)) LoadMapDecals(map);
        }

        public async Task<List<MapDecal>> GetDecals(string map)
        {
            await Initialization;
            if (!IsEnabled)
            { DebugEvent("DB_READ_REJECT", $"requestedMap={map} database not enabled"); return new(); }

            try
            {
                using var connection = Core.Database.GetConnection(_connectionName);
                connection.Open();

                var rows = (await connection.SelectAsync<MapDecal>(d => d.Map == map)).ToList();
                DebugEvent("DB_READ", $"requestedMap={map} records={rows.Count}");
                return rows;
            }
            catch (Exception ex)
            {
                DebugEvent("DB_READ_ERROR", $"requestedMap={map} type={ex.GetType().Name}; see existing error log");
                Core.Logger.LogError(ex, "Failed to load decals for map {Map}", map);
                return new();
            }
        }

        public async Task<ulong?> SaveDecal(MapDecal decal)
        {
            DebugEvent("DB_SAVE_BEGIN", $"id={decal.Id} uniqId={decal.DecalId} storedMap={decal.Map} active={decal.IsActive} forced={decal.ForceOnVIP} position=[{decal.Position}] angles=[{decal.Angles}] width={DecalRules.Number(decal.Width)} height={DecalRules.Number(decal.Height)} depth={decal.Depth}");
            await Initialization;
            if (!IsEnabled)
            { DebugEvent("DB_SAVE_REJECT", "database not enabled"); return null; }

            try
            {
                using var connection = Core.Database.GetConnection(_connectionName);
                connection.Open();

                var existing = await connection.GetAsync<MapDecal>(decal.Id);
                if (existing != null)
                {
                    bool updated = await connection.UpdateAsync(decal);
                    DebugEvent("DB_UPDATE", $"id={decal.Id} updated={updated}");
                    if (updated) return decal.Id;
                }
                else
                {
                    object result = await connection.InsertAsync(decal);
                    var id = Convert.ToUInt64(result);
                    DebugEvent("DB_INSERT", $"id={id} uniqId={decal.DecalId}");
                    return id;
                }
            }
            catch (Exception ex)
            {
                DebugEvent("DB_SAVE_ERROR", $"id={decal.Id} type={ex.GetType().Name}; see existing error log");
                Core.Logger.LogError(ex, "Failed to save decal for map {Map}", decal.Map);
            }

            return null;
        }

        public async Task<bool> DeleteDecal(ulong id)
        {
            await Initialization;
            if (!IsEnabled)
                return false;

            try
            {
                using var connection = Core.Database.GetConnection(_connectionName);
                connection.Open();

                bool deleted = await connection.DeleteAsync(new MapDecal { Id = id });
                DebugEvent("DB_DELETE", $"id={id} deleted={deleted}");
                return deleted;
            }
            catch (Exception ex)
            {
                Core.Logger.LogError(ex, "Failed to delete decal {Id}", id);
                return false;
            }

        }
    }
}