using OpenMir2;
using OpenMir2.Packets.ClientPackets;

namespace M2Server.Player;

public partial class PlayObject
{
    public OpenMir2.Data.GmCharacterState GmState { get; set; } = new();
    public long GmAcknowledgedRevision { get; set; }

    // Semantic item types, not names or bundles: random scroll (2), town scroll (3).
    public bool RetainGmTeleportScroll(OpenMir2.Data.StdItem item) => false;

    public ClientItem GetGmEquipmentItem(UserItem item)
    {
        var standard = SystemModule.SystemShare.ItemSystem.GetStdItem(item.Index);
        if (standard == null) return null;
        var result = new ClientItem();
        SystemModule.SystemShare.ItemSystem.GetUpgradeStdItem(standard, item, ref result);
        ApplyItemParametersByJob(item, ref result);
        result.Item.Name = M2Server.Items.CustomItemSystem.GetItemName(item);
        result.MakeIndex = item.MakeIndex; result.Dura = item.Dura; result.DuraMax = item.DuraMax;
        return result;
    }

    // Uses the same skill notification and passive recalculation as ordinary training.
    public void RefreshGmSkill(UserMagic skill)
    {
        CheckSeeHealGauge(skill);
        RecalcAbilitys();
        SendMsg(Messages.RM_MAGIC_LVEXP, 0, skill.MagIdx, skill.Level, skill.TranPoint);
        SendMsg(Messages.RM_ABILITY, 0, 0, 0, 0);
    }
}
