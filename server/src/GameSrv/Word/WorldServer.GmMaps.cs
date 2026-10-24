using GameSrv.Gm;
using M2Server;
using M2Server.Player;
using SystemModule;
using SystemModule.Maps;

namespace GameSrv.Word;

public partial class WorldServer
{
    private static string GmMapName(IEnvirnoment map) => string.IsNullOrEmpty(map.MapDesc) ? map.MapName : map.MapDesc;
    private static bool GmMapAvailable(IEnvirnoment map) => map.ServerIndex == M2Share.ServerIndex && map.Width > 0 && map.Height > 0;
    private static GmResult GmMapCatalogue() => new()
    {
        Success = true,
        Maps = (SystemShare.MapMgr?.Maps ?? Array.Empty<IEnvirnoment>()).Where(m => m != null && !string.IsNullOrEmpty(m.MapName))
            .Where(m => M2Server.Classic.ClassicCatalog.AllowsMap(m.MapName))
            .OrderBy(m => GmMapName(m), StringComparer.Ordinal).ThenBy(m => m.MapName, StringComparer.Ordinal)
            .Select(m => new GmMap { Id = m.MapName, Name = GmMapName(m), Width = m.Width, Height = m.Height,
                CanTeleport = GmMapAvailable(m), UnavailableReason = m.ServerIndex != M2Share.ServerIndex ? "地图不在当前服务" : m.Width <= 0 || m.Height <= 0 ? "地图未加载" : "" }).ToList()
    };

    private static GmResult GmTeleport(PlayObject player, GmCommand command)
    {
        if (command.Amount != 0 || command.Enabled != null || string.IsNullOrWhiteSpace(command.MapId) || command.MapId.Length > 64 || command.MapId.Any(char.IsControl) || command.MapName == null)
            return GmFailure("invalid_command", "Select an exact existing map.");
        var map = SystemShare.MapMgr?.FindMap(command.MapId);
        if (!M2Server.Classic.ClassicCatalog.AllowsMap(command.MapId))
            return GmFailure("map_not_found", "Map is outside the classic catalog.");
        if (map == null || map.MapName != command.MapId || GmMapName(map) != command.MapName)
            return GmFailure("map_not_found", "Map catalogue changed; refresh and select again.");
        if (!GmMapAvailable(map)) return GmFailure("map_unavailable", "The map is not loaded on this server.");
        if (!TryGmLanding(map, out short x, out short y)) return GmFailure("map_no_landing", "No free walkable landing position was found.");
        var oldMap = player.Envir; string oldName = player.MapName, oldFile = player.MapFileName;
        short oldX = player.CurrX, oldY = player.CurrY;
        try { player.SpaceMove(map.MapName, x, y, 0); }
        catch when (ReferenceEquals(player.Envir, map) && player.CurrX == x && player.CurrY == y && map.IsValidObject(x, y, 0, player))
        {
            // The position already changed. Record it so a notification error cannot cause a second teleport on retry.
            LogService.Warn("GM teleport client notification failed after movement.");
        }
        if (!ReferenceEquals(player.Envir, map) || player.CurrX != x || player.CurrY != y || !map.IsValidObject(x, y, 0, player))
        {
            if (!ReferenceEquals(player.Envir, oldMap) || player.CurrX != oldX || player.CurrY != oldY)
            {
                player.Envir.DeleteFromMap(player.CurrX, player.CurrY, player.CellType, player.ActorId, player);
                player.Envir = oldMap; player.CurrX = oldX; player.CurrY = oldY;
                oldMap.AddMapObject(oldX, oldY, player.CellType, player.ActorId, player);
                player.SendMsg(OpenMir2.Messages.RM_CLEAROBJECTS, 0, 0, 0, 0);
                player.SendMsg(OpenMir2.Messages.RM_CHANGEMAP, 0, 0, 0, 0, oldFile);
            }
            player.MapName = oldName; player.MapFileName = oldFile;
            return GmFailure("map_move_failed", "Movement failed; the character was not teleported to the selected position.");
        }
        return null;
    }

    private static bool TryGmLanding(IEnvirnoment map, out short x, out short y)
    {
        x = y = 0;
        // Uniform random probes handle normal maps without scanning them on the world thread.
        for (int i = 0; i < 256; i++)
        {
            short px = (short)Random.Shared.Next(map.Width), py = (short)Random.Shared.Next(map.Height);
            if (map.CanWalk(px, py, false) && !map.GetXyHuman(px, py)) { x = px; y = py; return true; }
        }
        // Cover sparse/tiny maps too. Bound the fallback work on unusually large maps.
        int cells = map.Width * map.Height, start = Random.Shared.Next(cells);
        for (int i = 0; i < Math.Min(cells, 1_000_000); i++)
        {
            int cell = (int)(((long)start + i) % cells);
            short px = (short)(cell % map.Width), py = (short)(cell / map.Width);
            if (map.CanWalk(px, py, false) && !map.GetXyHuman(px, py)) { x = px; y = py; return true; }
        }
        return false;
    }
}
