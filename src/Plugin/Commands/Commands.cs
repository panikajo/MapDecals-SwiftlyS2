using MapDecals.Menus;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace MapDecals;

public sealed partial class Plugin
{
    /* ==================== Command Registration ==================== */
    private void RegisterCommands()
    {
        Core.Command.RegisterCommand("mapdecaldebug", OnDebugCommand);
        RegisterCommandWithAliases(Config.CurrentValue.PlaceDecalCommands, OnMapDecalsCommand);
        RegisterCommandWithAliases(Config.CurrentValue.AdToggleCommands, OnDecalToggleCommand);
        // Own-session escape hatch even when the client's Workshop layout did not load.
        Core.Command.RegisterCommand("mapdecalclose", ctx =>
        {
            if (ctx.Sender is not { IsValid: true } player) return;
            Panorama.Close(player.PlayerID);
            _applyingDecal.TryRemove(player.SteamID, out _);
        });
    }

    private static void RegisterCommandWithAliases(CommandConfig config, ICommandService.CommandListener handler)
    {
        if (string.IsNullOrWhiteSpace(config.Command))
            return;

        Core.Command.RegisterCommand(config.Command, handler, permission: config.Permission);

        foreach (var alias in config.Aliases)
        {
            if (!string.IsNullOrWhiteSpace(alias))
                Core.Command.RegisterCommandAlias(config.Command, alias);
        }
    }

    private void OnMapDecalsCommand(ICommandContext ctx)
    {
        var player = ctx.Sender;

        if (player == null || player.PlayerPawn == null)
        {
            ctx.Reply($"{Core.Localizer["cc.decals.prefix"]} {Core.Localizer["cc.command.mustbeingame"]}");
            return;
        }

        var localizer = Core.Translation.GetPlayerLocalizer(player);
        if (player.PlayerPawn.LifeState != (byte)LifeState_t.LIFE_ALIVE)
        {
            ctx.Reply($"{localizer["cc.decals.prefix"]} {localizer["cc.command.mustbealive"]}");
            return;
        }
        DebugEvent("MENU_REQUEST", $"playerSlot={player.PlayerID} pawnLifeState={player.PlayerPawn.LifeState} panorama={Config.CurrentValue.UsePanoramaMenu}");
        _applyingDecal.TryRemove(player.SteamID, out _);
        MenuManager.OpenMainMenu(player, localizer);
    }

    private void OnDecalToggleCommand(ICommandContext ctx)
    {
        var player = ctx.Sender;

        if (player == null || player.PlayerPawn == null)
        {
            ctx.Reply($"{Core.Localizer["cc.decals.prefix"]} {Core.Localizer["cc.command.mustbeingame"]}");
            return;
        }

        if (!_adDecalStatus.TryGetValue(player.PlayerID, out var status))
        {
            ctx.Reply($"{Core.Localizer["cc.decals.prefix"]} {Core.Localizer["cc.command.tryagainlater"]}");
            return;
        }

        _adDecalStatus[player.PlayerID] = !status;
        DebugEvent("PREFERENCE_TOGGLE", $"playerSlot={player.PlayerID} old={status} new={!status}");

        var localizer = Core.Translation.GetPlayerLocalizer(player);

        var msg = !status ? localizer["cc.decaltoggle.on"] : localizer["cc.decaltoggle.off"];
        player.SendChat($"{localizer["cc.decals.prefix"]} {msg}");

        UpdateDecalTransmit(player);
    }
}