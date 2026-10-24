using GameSrv.Gm;
using M2Server;
using M2Server.Player;
using OpenMir2;
using OpenMir2.Packets.ClientPackets;
using SystemModule;

namespace GameSrv.Word;

public partial class WorldServer
{
    private static GmResult RepairGmEquipment(PlayObject player, GmCommand command, out Action notify)
    {
        notify = null;
        if (command.Amount != 0 || command.Enabled != null)
            return GmFailure("invalid_command", "repair_equipment takes no additional parameters.");
        var damaged = new List<(UserItem Item, ushort Durability)>();
        for (int slot = 0; slot < player.UseItems.Length; slot++)
        {
            var item = player.UseItems[slot];
            if (item == null || item.Index == 0 || item.DuraMax == 0 || item.Dura >= item.DuraMax) continue;
            var standard = SystemShare.ItemSystem.GetStdItem(item.Index);
            // Durability represents remaining quantity/fuel for these consumables.
            if (standard == null || standard.StdMode is 25 or 28 or 29 or 30 || !M2Share.CheckUserItems(slot, standard)) continue;
            damaged.Add((item, item.Dura));
        }
        if (damaged.Count == 0)
            return GmFailure("equipment_intact", "No damaged wearable equipment to repair.");
        try
        {
            foreach (var entry in damaged) entry.Item.Dura = entry.Item.DuraMax;
            // Broken equipment starts contributing its attributes again.
            player.RecalcAbilitys();
        }
        catch
        {
            foreach (var entry in damaged) entry.Item.Dura = entry.Durability;
            player.RecalcAbilitys();
            throw;
        }
        notify = () =>
        {
            player.SendUseItems();
            player.SendMsg(Messages.RM_ABILITY, 0, 0, 0, 0);
            player.SendMsg(Messages.RM_SUBABILITY, 0, 0, 0, 0);
            player.HealthSpellChanged();
        };
        return null;
    }
}
