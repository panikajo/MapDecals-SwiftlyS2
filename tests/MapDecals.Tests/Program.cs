using System.Globalization;
using Dapper;
using FluentMigrator.Runner;
using MapDecals;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

int assertions = 0;
void Check(bool condition, string label)
{
    assertions++;
    if (!condition) throw new Exception("FAILED: " + label);
}
foreach (var (count, expected) in new[] { (0, 1), (1, 1), (6, 1), (7, 2), (12, 2), (13, 3), (1201, 201) })
    Check(DecalRules.PageCount(count) == expected, $"pagination {count}");
foreach (bool permitted in new[] { false, true })
foreach (bool forced in new[] { false, true })
foreach (bool preference in new[] { false, true })
    Check(DecalRules.IsVisible(permitted, forced, preference) == (permitted && (forced || preference)), "visibility truth table");
foreach (string culture in new[] { "en-US", "ru-RU", "uk-UA" })
{
    CultureInfo.CurrentCulture = new CultureInfo(culture);
    Check(DecalRules.Triple(1.25f, -2.5f, 0) == "1.25 -2.5 0", "invariant coordinates " + culture);
    Check(DecalRules.TryNumber("1.25", -10, 10, out var value) && value == 1.25f, "decimal point " + culture);
    foreach (var bad in new[] { "NaN", "Infinity", "-Infinity", "1,25", "1e38", "", "hello" })
        Check(!DecalRules.TryNumber(bad, -32768, 32768, out _), "reject " + bad);
}
Check(DecalRules.TryNumber("-32768", -32768, 32768, out _), "coordinate lower bound");
Check(DecalRules.TryNumber("32768", -32768, 32768, out _), "coordinate upper bound");
Check(!DecalRules.TryNumber("32769", -32768, 32768, out _), "coordinate overflow");
Check(DecalRules.ValidName("Декаль у входа"), "unicode name");
foreach (var name in new[] { "", "ab", "   ", "bad\nname", new string('x', 65) })
    Check(!DecalRules.ValidName(name), "invalid name");
Check(DecalRules.ValidName(new string('x', 64)), "name maximum length");

// Exercise the real migration against a temporary SQLite database, not a fake in-memory schema.
string db = Path.Combine(Path.GetTempPath(), "mapdecals-tests-" + Guid.NewGuid() + ".db");
try
{
    var connectionString = "Data Source=" + db;
    using var provider = new ServiceCollection().AddFluentMigratorCore()
        .ConfigureRunner(r => r.AddSQLite().WithGlobalConnectionString(connectionString)
            .ScanIn(typeof(CreateMapDecalsTable).Assembly).For.Migrations()).BuildServiceProvider();
    using var scope = provider.CreateScope();
    var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
    runner.MigrateUp();
    runner.MigrateUp(); // Idempotent schema startup.
    using var connection = new SqliteConnection(connectionString);
    connection.Open();
    Check(connection.QuerySingle<long>("SELECT COUNT(*) FROM pragma_table_info('cc_mapdecals')") == 11, "migration columns");
    long id = connection.QuerySingle<long>("""
        INSERT INTO cc_mapdecals(map,decal_id,decal_name,position,angles)
        VALUES (@Map,@DecalId,@Name,@Position,@Angles); SELECT last_insert_rowid();
        """, new { Map = "de_dust2", DecalId = "test", Name = "Тестовая декаль", Position = "1.25 -2.5 3", Angles = "90 180 0" });
    Check(id > 0, "insert identity");
    Check(connection.QuerySingle<long>("SELECT is_active FROM cc_mapdecals WHERE id=@id", new { id }) == 1, "active default");
    Check(connection.QuerySingle<string>("SELECT decal_name FROM cc_mapdecals WHERE id=@id", new { id }) == "Тестовая декаль", "unicode storage");
    Check(connection.QuerySingle<long>("SELECT COUNT(*) FROM cc_mapdecals WHERE map=@map", new { map = "de_inferno" }) == 0, "map isolation");
    Check(connection.Execute("UPDATE cc_mapdecals SET width=256, force_on_vip=1 WHERE id=@id", new { id }) == 1, "edit record");
    Check(connection.QuerySingle<float>("SELECT width FROM cc_mapdecals WHERE id=@id", new { id }) == 256, "persist size");
    Check(connection.Execute("DELETE FROM cc_mapdecals WHERE id=@id", new { id }) == 1, "delete record");
    Check(connection.QuerySingle<long>("SELECT COUNT(*) FROM cc_mapdecals") == 0, "empty after delete");
}
finally { SqliteConnection.ClearAllPools(); if (File.Exists(db)) File.Delete(db); }
// Diagnostic paths must remain relative; checks must not silently rewrite actual config.
Check(DiagnosticPaths.CompiledMaterial("materials/mapdecals/2.vmat") == "materials/mapdecals/2.vmat_c", "compiled material suffix");
Check(DiagnosticPaths.CompiledMaterial(@"materials\mapdecals\2.vmat") == "materials/mapdecals/2.vmat_c", "normalize diagnostic separators");
Check(DiagnosticPaths.CompiledMaterial("materials/Logo.VMAT_C") == "materials/Logo.VMAT_C", "preserve case and compiled suffix");
foreach (var path in new string?[] { null, "", " ", "C:/materials/x.vmat", "/materials/x.vmat", "materials/../x.vmat", "materials/./x.vmat", "materials/x.png", "other/x.vmat", "materials/x\n.vmat" })
    Check(DiagnosticPaths.CompiledMaterial(path) == null, "reject unsafe/unrecognized diagnostic path");
Check(DiagnosticLog.Clean("a\r\nb\tc") == @"a\r\nb\tc", "escape multiline diagnostic values");
Check(DiagnosticLog.Clean("bad\u0001value") == "bad?value", "escape diagnostic controls");
Check(DiagnosticLog.Clean(null) == "<null>", "null diagnostic value");
Check(DiagnosticLog.Clean(new string('x', 5000)).Length < 4200, "bound diagnostic line");
var logRoot = Path.Combine(Path.GetTempPath(), "mapdecals-debug-tests-" + Guid.NewGuid());
Directory.CreateDirectory(logRoot);
try
{
    var lines = new List<string>(); var warnings = new List<string>();
    string logPath = Path.Combine(logRoot, "logs", "debug.log");
    var sink = new DiagnosticLog(logPath, lines.Add, warnings.Add, 1);
    sink.Write("TEST", "console-only", false);
    Check(lines.Count == 1 && !File.Exists(logPath), "console-only mode");
    sink.Write("TEST", "first\nentry", true);
    Check(File.ReadAllLines(logPath).Length == 1, "single physical log line");
    Check(File.ReadAllText(logPath).Contains(@"first\nentry"), "file escape retained");
    sink.Write("TEST", "second", true);
    sink.Write("TEST", "third", true);
    sink.Write("TEST", "fourth", true);
    Check(File.Exists(logPath + ".1") && File.Exists(logPath + ".2"), "two rotated archives");
    Check(File.ReadAllText(logPath + ".2").Contains("second"), "oldest archive bounded");
    Check(File.ReadAllText(logPath).Contains("#5 TEST fourth"), "monotonic sequence across rotations");
    Check(warnings.Count == 0, "normal sink no warnings");
    string blocker = Path.Combine(logRoot, "not-a-directory"); File.WriteAllText(blocker, "block");
    var failing = new DiagnosticLog(Path.Combine(blocker, "debug.log"), lines.Add, warnings.Add);
    failing.Write("TEST", "still-console-1", true); failing.Write("TEST", "still-console-2", true);
    Check(warnings.Count == 1, "file failure warns once");
    Check(lines.Last().Contains("still-console-2"), "file failure preserves console output");
    new DiagnosticLog(logPath, _ => throw new Exception(), _ => throw new Exception()).Write("TEST", "safe callback", false);
    Check(true, "diagnostic callbacks cannot break plugin");
}
finally { Directory.Delete(logRoot, true); }
Console.WriteLine($"PASS: {assertions} assertions (validation, pagination, visibility, SQLite migration/CRUD, diagnostic paths/logging/rotation).");
Console.WriteLine("Not covered: CS2 rendering, clicks, resource compilation, network transmission, MySQL/PostgreSQL.");
