using MapDecals;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace MapDecals.Menus;

/// <summary>
/// Server-driven Custom HUD; reuses the supplied utils-* layout without JavaScript.
/// Each player owns a private HUD entity and server-side action table. Never trust ButtonId.
/// All engine access and in-memory mutations happen on the game thread.
/// </summary>
internal sealed class PanoramaDecalMenu : IDisposable
{
    private sealed record Row(string Label, Action Click, string Kind = "k-submenu", bool On = false);
    private sealed class Session(ulong steamId, CHandle<CCSCustomHudLayout> handle)
    {
        internal readonly ulong SteamId = steamId;
        internal readonly CHandle<CCSCustomHudLayout> Handle = handle;
        internal CCSCustomHudLayout? Hud => Handle.IsValid ? Handle.Value : null;
        internal List<Row> Rows = [];
        internal string Title = "";
        internal Action? Back;
        internal int Page;
        internal long LastClick;
        internal bool Busy;
        internal Func<string, bool>? Input;
        internal long InputDeadline;
    }

    private readonly Dictionary<int, Session> _sessions = [];
    private readonly HashSet<ulong> _editing = [];
    private readonly Guid _chatHook;
    private bool _disposed;

    internal PanoramaDecalMenu()
    {
        Plugin.Core.Event.OnCustomHudClicked += OnClick;
        _chatHook = Plugin.Core.Command.HookClientChat(OnChat);
    }

    internal static bool CanManage(IPlayer player)
    {
        var permission = Plugin.Config.CurrentValue.PlaceDecalCommands.Permission;
        return player.IsValid && !player.IsFakeClient && (string.IsNullOrWhiteSpace(permission)
            || Plugin.Core.Permission.PlayerHasPermission(player.SteamID, permission));
    }

    private static string T(IPlayer p, string key) => Plugin.Core.Translation.GetPlayerLocalizer(p)[key];
    private static void Message(IPlayer p, string key) => p.SendChat("[MapDecals] " + T(p, key));
    private bool Valid(IPlayer p, Session s) => !_disposed && Plugin.Running && CanManage(p)
        && p.SteamID == s.SteamId && _sessions.TryGetValue(p.PlayerID, out var current) && ReferenceEquals(s, current);

    internal void OpenMain(IPlayer p)
    {
        Plugin._applyingDecal.TryRemove(p.SteamID, out _);
        Close(p.PlayerID);
        Show(p, T(p, "cc.menu.mainmenu.title"), [
            new(T(p, "cc.menu.placedecal.option"), () => OpenMaterials(p)),
            new(T(p, "cc.menu.editdecal.option"), () => OpenList(p))
        ]);
    }

    private void OpenMaterials(IPlayer p) => Show(p, T(p, "cc.menu.placedecal.title"),
        Plugin.Config.CurrentValue.Props.Where(x => !string.IsNullOrWhiteSpace(x.Material))
            .Select(x => new Row(x.Name, () => BeginPlacement(p, x))).ToList(), () => OpenMain(p));

    private void BeginPlacement(IPlayer p, ConfigDecals material)
    {
        if (p.PlayerPawn is not { } pawn || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE)
        { Message(p, "cc.command.mustbealive"); return; }
        Close(p.PlayerID); // Release cursor: the player must be able to aim and ping.
        Plugin._applyingDecal[p.SteamID] = material;
        Message(p, "cc.placedcal.pinghelper");
        Message(p, "cc.panorama.cancelplacement");
    }

    private void OpenList(IPlayer p) => Show(p, T(p, "cc.menu.editdecals.title"),
        Plugin._mapDecals.Select(x => new Row($"#{x.Id} {x.DecalName}", () => OpenEdit(p, x))).ToList(), () => OpenMain(p));

    internal void OpenEdit(IPlayer p, MapDecal d)
    {
        if (!Plugin._mapDecals.Contains(d)) { OpenList(p); return; }
        void Refresh() => OpenEdit(p, d);
        Show(p, $"#{d.Id} {d.DecalName}", [
            new(T(p, "cc.menu.editdecal.isactive"), () => Persist(p, d, x => x.IsActive = !x.IsActive), "k-toggle", d.IsActive),
            new(T(p, "cc.menu.editdecal.name"), () => Prompt(p, "cc.menu.editdecal.name.hint", value =>
            {
                if (!DecalRules.ValidName(value) || Plugin._mapDecals.Any(x => x != d && x.DecalName.Equals(value, StringComparison.OrdinalIgnoreCase))) return false;
                Persist(p, d, x => x.DecalName = value); return true;
            }, Refresh)),
            new(T(p, "cc.menu.editdecal.position"), () => OpenCoordinates(p, d)),
            new(T(p, "cc.panorama.size"), () => OpenSize(p, d)),
            new(T(p, "cc.menu.editdecal.forceonvip"), () => Persist(p, d, x => x.ForceOnVIP = !x.ForceOnVIP), "k-toggle", d.ForceOnVIP),
            new(T(p, "cc.menu.editdecal.delete"), () => Show(p, T(p, "cc.panorama.confirm"), [
                new(T(p, "cc.menu.editdecal.delete"), () => Delete(p, d)),
                new(T(p, "cc.panorama.cancel"), Refresh)
            ], Refresh))
        ], () => OpenList(p));
    }

    private void OpenCoordinates(IPlayer p, MapDecal d)
    {
        if (!Vector.TryDeserialize(d.Position, out var position) || !QAngle.TryDeserialize(d.Angles, out var angles)) return;
        var values = new[] { position.X, position.Y, position.Z, angles.X, angles.Y, angles.Z };
        var keys = new[] { "position.x", "position.y", "position.z", "angle.x", "angle.y", "angle.z" };
        var rows = new List<Row>();
        for (int i = 0; i < keys.Length; i++)
        {
            var axis = i;
            rows.Add(new(T(p, "cc.menu.editdecal." + keys[i]) + ": " + DecalRules.Number(values[i]),
                () => Prompt(p, "cc.menu.editdecal.position.hint", text =>
                {
                    var limit = axis < 3 ? 32768f : 360f;
                    if (!DecalRules.TryNumber(text, -limit, limit, out var number)) return false;
                    Persist(p, d, x =>
                    {
                        // Read the latest record, not stale coordinates captured when opening the menu.
                        if (axis < 3 && Vector.TryDeserialize(x.Position, out var pos))
                        {
                            if (axis == 0) pos.X = number; else if (axis == 1) pos.Y = number; else pos.Z = number;
                            x.Position = DecalRules.Triple(pos.X, pos.Y, pos.Z);
                        }
                        else if (QAngle.TryDeserialize(x.Angles, out var ang))
                        {
                            if (axis == 3) ang.X = number; else if (axis == 4) ang.Y = number; else ang.Z = number;
                            x.Angles = DecalRules.Triple(ang.X, ang.Y, ang.Z);
                        }
                    }); return true;
                }, () => OpenCoordinates(p, d))));
        }
        Show(p, T(p, "cc.menu.editdecal.position"), rows, () => OpenEdit(p, d));
    }

    private void OpenSize(IPlayer p, MapDecal d)
    {
        Row NumberRow(string key, float value, float min, float max, Action<MapDecal, float> apply) =>
            new(T(p, key) + ": " + DecalRules.Number(value), () => Prompt(p, "cc.panorama.number", text =>
            {
                if (!DecalRules.TryNumber(text, min, max, out var number)) return false;
                Persist(p, d, x => apply(x, number)); return true;
            }, () => OpenSize(p, d)));
        Show(p, T(p, "cc.panorama.size"), [
            NumberRow("cc.menu.editdecal.width", d.Width, 1, 4096, (x, n) => x.Width = n),
            NumberRow("cc.menu.editdecal.height", d.Height, 1, 4096, (x, n) => x.Height = n),
            NumberRow("cc.menu.editdecal.depth", d.Depth, 1, 256, (x, n) => x.Depth = (int)n)
        ], () => OpenEdit(p, d));
    }

    private void Show(IPlayer p, string title, List<Row> rows, Action? back = null)
    {
        if (!CanManage(p)) return;
        try
        {
            if (!_sessions.TryGetValue(p.PlayerID, out var s) || s.SteamId != p.SteamID || s.Hud == null)
            {
                Close(p.PlayerID);
                var hud = Plugin.Core.EntitySystem.CreateEntity<CCSCustomHudLayout>();
                hud.StrLayout = Plugin.Config.CurrentValue.PanoramaLayout;
                hud.StrLayoutUpdated();
                hud.DispatchSpawn();
                hud.SetTransmitState(false);
                hud.SetTransmitState(true, p.PlayerID);
                s = new(p.SteamID, Plugin.Core.EntitySystem.GetRefEHandle(hud));
                _sessions[p.PlayerID] = s;
            }
            s.Rows = rows; s.Title = title; s.Page = 0; s.Back = back; s.Input = null;
            Render(p, s);
        }
        catch (Exception ex)
        {
            Plugin.Core.Logger.LogError(ex, "Could not open Panorama menu");
            Close(p.PlayerID);
            Message(p, "cc.panorama.unavailable");
        }
    }

    private static void Class(CCSCustomHudLayout hud, string panel, string name, bool enabled) =>
        hud.SetHasClass(panel, name, enabled ? EHudPanelClassStatus_t.k_eHudPanelClassStatus_HasClass
            : EHudPanelClassStatus_t.k_eHudPanelClassStatus_DoesNotHaveClass);

    private static void Render(IPlayer p, Session s)
    {
        var hud = s.Hud;
        if (hud == null) return;
        s.Page = Math.Clamp(s.Page, 0, DecalRules.PageCount(s.Rows.Count) - 1);
        hud.SetDialogVariableString("utils-title", "title", s.Title);
        hud.SetDialogVariableString("utils-desc", "desc", T(p, "cc.panorama.description"));
        hud.SetDialogVariableString("utils-hint", "hint", T(p, "cc.panorama.hint"));
        hud.SetDialogVariableString("utils-page", "page", $"{s.Page + 1} / {DecalRules.PageCount(s.Rows.Count)}");
        Class(hud, "utils-dialog", "utils-dismissed", false);
        Class(hud, "utils-back-anchor", "utils-hide", s.Back == null);
        Class(hud, "utils-pagination", "utils-hide", s.Rows.Count <= DecalRules.PageSize);
        for (int i = 0; i < DecalRules.PageSize; i++)
        {
            int index = s.Page * DecalRules.PageSize + i;
            Row? row = index < s.Rows.Count ? s.Rows[index] : null;
            var panel = $"utils-item-{i}";
            Class(hud, $"utils-slot-{i}", "utils-hide", row == null);
            foreach (var kind in new[] { "k-empty", "k-submenu", "k-toggle", "k-value" })
                Class(hud, panel, kind, row?.Kind == kind);
            Class(hud, panel, "is-on", row?.On == true);
            hud.SetDialogVariableString(panel, "label", row?.Label ?? "");
            hud.SetDialogVariableString(panel, "value", "");
        }
        hud.SetInputCaptureEnabledForPlayer(p.PlayerID, true);
    }

    private void OnClick(IOnCustomHudClickedEvent e)
    {
        if (!_sessions.TryGetValue(e.PlayerId, out var s) || s.Busy || s.Input != null || s.Hud is not { } hud
            || !e.CustomHudLayout.IsValid || hud.Index != e.CustomHudLayout.Index) return;
        var p = Plugin.Core.PlayerManager.GetPlayer(e.PlayerId);
        if (p == null || !Valid(p, s)) { Close(e.PlayerId); return; }
        // Client messages are untrusted, including rapid repeated clicks and hidden controls.
        if (Environment.TickCount64 - s.LastClick < 180) return;
        s.LastClick = Environment.TickCount64;
        try
        {
            if (e.ButtonId == "utils-closeBtn") { Close(e.PlayerId); return; }
            if (e.ButtonId == "utils-back") { s.Back?.Invoke(); return; }
            if (e.ButtonId == "utils-prev" && s.Page > 0) { s.Page--; Render(p, s); return; }
            if (e.ButtonId == "utils-next" && s.Page + 1 < DecalRules.PageCount(s.Rows.Count)) { s.Page++; Render(p, s); return; }
            const string prefix = "utils-item-";
            if (!e.ButtonId.StartsWith(prefix, StringComparison.Ordinal)
                || !int.TryParse(e.ButtonId.AsSpan(prefix.Length), out var slot) || slot is < 0 or >= DecalRules.PageSize) return;
            int index = s.Page * DecalRules.PageSize + slot;
            if (index < s.Rows.Count) s.Rows[index].Click();
        }
        catch (Exception ex) { Plugin.Core.Logger.LogError(ex, "Panorama action failed"); Close(e.PlayerId); }
    }

    private void Prompt(IPlayer p, string hint, Func<string, bool> input, Action back)
    {
        if (!_sessions.TryGetValue(p.PlayerID, out var s) || s.Hud is not { } hud) return;
        s.Input = input; s.Back = back; s.InputDeadline = Environment.TickCount64 + 60000;
        Class(hud, "utils-dialog", "utils-dismissed", true);
        hud.SetInputCaptureEnabledForPlayer(p.PlayerID, false);
        Message(p, hint);
        Message(p, "cc.panorama.inputhint");
    }

    private HookResult OnChat(int playerId, string text, bool teamOnly)
    {
        if (text.Trim().Equals("!cancel", StringComparison.OrdinalIgnoreCase))
        {
            var sender = Plugin.Core.PlayerManager.GetPlayer(playerId);
            if (sender is { IsValid: true } && Plugin._applyingDecal.TryRemove(sender.SteamID, out _))
            { Message(sender, "cc.panorama.cancelled"); return HookResult.Stop; }
        }
        if (!_sessions.TryGetValue(playerId, out var s) || s.Input == null) return HookResult.Continue;
        var p = Plugin.Core.PlayerManager.GetPlayer(playerId);
        if (p == null || !Valid(p, s)) { Close(playerId); return HookResult.Continue; }
        if (Environment.TickCount64 > s.InputDeadline) { Close(playerId); return HookResult.Continue; }
        text = text.Trim();
        if (text.Equals("!cancel", StringComparison.OrdinalIgnoreCase))
        { s.Input = null; s.Back?.Invoke(); return HookResult.Stop; }
        // Allow chat commands, including reopening the menu, without treating them as input.
        if (text.StartsWith('!') || text.StartsWith('/')) return HookResult.Continue;
        var input = s.Input;
        s.Input = null;
        if (!input(text)) { s.Input = input; Message(p, "cc.panorama.invalid"); }
        return HookResult.Stop;
    }

    private void Persist(IPlayer p, MapDecal d, Action<MapDecal> change)
    {
        if (!_sessions.TryGetValue(p.PlayerID, out var s) || !Valid(p, s) || !Plugin._mapDecals.Contains(d) || !_editing.Add(d.Id)) return;
        s.Busy = true;
        var next = Copy(d);
        change(next);
        var generation = Plugin.MapGeneration;
        _ = Task.Run(() => SaveAsync(p, s, d, next, generation));
    }

    private async Task SaveAsync(IPlayer p, Session s, MapDecal d, MapDecal next, int generation)
    {
        var id = await Plugin.Database.SaveDecal(next);
        Plugin.Core.Scheduler.NextWorldUpdate(() =>
        {
            if (_disposed || !Plugin.Running || generation != Plugin.MapGeneration) return;
            _editing.Remove(d.Id);
            s.Busy = false;
            if (id != null && Plugin._mapDecals.Contains(d))
            {
                // Commit in-memory state only after persistence succeeds.
                d.DecalName = next.DecalName; d.Position = next.Position; d.Angles = next.Angles;
                d.Width = next.Width; d.Height = next.Height; d.Depth = next.Depth;
                d.ForceOnVIP = next.ForceOnVIP; d.IsActive = next.IsActive;
                d.DeleteEntity();
                if (d.IsActive) d.ToggleEntity();
            }
            if (!Valid(p, s)) return;
            if (id == null) Message(p, "cc.panorama.savefailed");
            OpenEdit(p, d);
        });
    }

    private void Delete(IPlayer p, MapDecal d)
    {
        if (!_sessions.TryGetValue(p.PlayerID, out var s) || !Valid(p, s) || !Plugin._mapDecals.Contains(d) || !_editing.Add(d.Id)) return;
        s.Busy = true;
        var generation = Plugin.MapGeneration;
        _ = Task.Run(() => DeleteAsync(p, s, d, generation));
    }

    private async Task DeleteAsync(IPlayer p, Session s, MapDecal d, int generation)
    {
        var deleted = await Plugin.Database.DeleteDecal(d.Id);
        Plugin.Core.Scheduler.NextWorldUpdate(() =>
        {
            if (_disposed || !Plugin.Running || generation != Plugin.MapGeneration) return;
            _editing.Remove(d.Id); s.Busy = false;
            if (deleted) { Plugin._mapDecals.Remove(d); d.DeleteEntity(); }
            if (!Valid(p, s)) return;
            if (!deleted) Message(p, "cc.panorama.savefailed");
            OpenList(p);
        });
    }

    private static MapDecal Copy(MapDecal d) => new()
    {
        Id = d.Id, Map = d.Map, DecalId = d.DecalId, DecalName = d.DecalName,
        Position = d.Position, Angles = d.Angles, Depth = d.Depth, Width = d.Width, Height = d.Height,
        ForceOnVIP = d.ForceOnVIP, IsActive = d.IsActive
    };

    internal void Close(int playerId)
    {
        if (_sessions.Remove(playerId, out var s))
        {
            Plugin._applyingDecal.TryRemove(s.SteamId, out _);
            if (s.Hud is { IsValid: true } hud)
            {
                hud.SetInputCaptureEnabledForPlayer(playerId, false);
                hud.Despawn();
            }
        }
    }

    internal void Reset()
    {
        foreach (var id in _sessions.Keys.ToArray()) Close(id);
        _editing.Clear();
    }

    public void Dispose()
    {
        _disposed = true;
        Plugin.Core.Event.OnCustomHudClicked -= OnClick;
        Plugin.Core.Command.UnhookClientChat(_chatHook);
        Reset();
    }
}
