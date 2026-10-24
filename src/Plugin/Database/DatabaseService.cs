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
            Initialization = Task.Run(() =>
            {
                try
                {
                    MigrationRunner.RunMigrations(Core.Database, _connectionName);
                    IsEnabled = true;
                }
                catch (Exception ex)
                {
                    Core.Logger.LogError(ex, "Failed to initialize database");
                    IsEnabled = false;
                }
            });
            if (!string.IsNullOrWhiteSpace(map)) LoadMapDecals(map);
        }

        public async Task<List<MapDecal>> GetDecals(string map)
        {
            await Initialization;
            if (!IsEnabled)
                return new();

            try
            {
                using var connection = Core.Database.GetConnection(_connectionName);
                connection.Open();

                return (await connection
                    .SelectAsync<MapDecal>(d => d.Map == map))
                    .ToList();
            }
            catch (Exception ex)
            {
                Core.Logger.LogError(ex, "Failed to load decals for map {Map}", map);
                return new();
            }
        }

        public async Task<ulong?> SaveDecal(MapDecal decal)
        {
            await Initialization;
            if (!IsEnabled)
                return null;

            try
            {
                using var connection = Core.Database.GetConnection(_connectionName);
                connection.Open();

                var existing = await connection.GetAsync<MapDecal>(decal.Id);
                if (existing != null)
                {
                    if (await connection.UpdateAsync(decal)) return decal.Id;
                }
                else
                {
                    object result = await connection.InsertAsync(decal);
                    return Convert.ToUInt64(result);
                }
            }
            catch (Exception ex)
            {
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

                return await connection.DeleteAsync(new MapDecal { Id = id });
            }
            catch (Exception ex)
            {
                Core.Logger.LogError(ex, "Failed to delete decal {Id}", id);
                return false;
            }

        }
    }
}