using GameSrv.Gm;
using M2Server.Monster;
using M2Server.Player;
using OpenMir2;
using OpenMir2.Data;
using OpenMir2.Enums;
using SystemModule;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace GameSrv.Word;

public partial class WorldServer
{
    private sealed class GmPetIdentity { public readonly Guid Value = Guid.NewGuid(); }
    // An actor number can be reused; a selected pet token identifies this exact live instance.
    private static readonly ConditionalWeakTable<MonsterObject, GmPetIdentity> GmPetIdentities = new();
    private static Guid GmPetId(MonsterObject pet) => GmPetIdentities.GetValue(pet, _ => new()).Value;
    private static bool CanGmModifyPets(PlayObject player) => player.Job is PlayerJob.Taoist or PlayerJob.Wizard;
    private static IEnumerable<MonsterObject> ActiveGmPets(PlayObject player) => player.SlaveList == null ? Enumerable.Empty<MonsterObject>() :
        player.SlaveList.OfType<MonsterObject>().Where(p => ReferenceEquals(p.Master, player) && !p.Ghost && !p.Death && p.WAbil.HP > 0 && p.Envir != null);

    private static List<GmPet> GmPetSnapshot(PlayObject player) => ActiveGmPets(player).Select(p => new GmPet
    {
        Id = GmPetId(p).ToString("D"), ActorId = p.ActorId, Name = p.ChrName ?? "", Level = p.SlaveExpLevel,
        Hp = p.WAbil.HP, MaxHp = p.WAbil.MaxHP, AttackMin = HUtil32.LoByte(p.WAbil.DC), AttackMax = HUtil32.HiByte(p.WAbil.DC)
    }).ToList();

    private static GmResult SetGmPetLevel(PlayObject player, GmCommand command, out Action notify)
    {
        notify = null;
        if (command.Amount != 0 || command.Enabled != null || command.PetLevel is not (>= 0 and <= 7) || !Guid.TryParse(command.PetId, out var id) || id == Guid.Empty)
            return GmFailure("invalid_command", "An exact current pet and level 0 to 7 are required.");
        if (!CanGmModifyPets(player)) return GmFailure("pet_job_required", "Only a Taoist or Wizard can modify their pet.");
        var pet = ActiveGmPets(player).FirstOrDefault(p => GmPetId(p) == id);
        if (pet == null || pet.Abil.MaxHP == 0) return GmFailure("pet_unavailable", "The selected living pet is no longer owned by this character; read again.");
        if (pet.SlaveExpLevel == command.PetLevel.Value) return GmFailure("pet_level_unchanged", "The pet already has that level.");
        byte oldLevel = pet.SlaveExpLevel; int oldKills = pet.KillMonCount; var oldAbility = pet.WAbil.Clone();
        try
        {
            pet.SlaveExpLevel = (byte)command.PetLevel.Value;
            pet.KillMonCount = 0;
            pet.RecalcAbilitys();
            // Changing experience level changes maxima, and does not heal the pet.
            pet.WAbil.HP = Math.Min(oldAbility.HP, pet.WAbil.MaxHP);
            pet.WAbil.MP = Math.Min(oldAbility.MP, pet.WAbil.MaxMP);
        }
        catch { pet.SlaveExpLevel = oldLevel; pet.KillMonCount = oldKills; pet.WAbil = oldAbility; throw; }
        notify = () => { pet.RefNameColor(); pet.HealthSpellChanged(); };
        return null;
    }

    private static GmAttribute Attr(string name, object value) => new() { Name = name, Value = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "" };
    private static string Range(ushort value) => HUtil32.LoByte(value) + "–" + HUtil32.HiByte(value);
    private static List<GmAttribute> GmAbilitySnapshot(Ability a) => new()
    {
        Attr("防御", Range(a.AC)), Attr("魔御", Range(a.MAC)), Attr("攻击", Range(a.DC)), Attr("魔法", Range(a.MC)), Attr("道术", Range(a.SC)),
        Attr("生命", a.HP + " / " + a.MaxHP), Attr("魔力", a.MP + " / " + a.MaxMP)
    };

    private static List<GmEquipment> GmEquipmentSnapshot(PlayObject player)
    {
        string[] slots = { "衣服", "武器", "右手物品", "项链", "头盔", "左手镯／符", "右手镯", "左戒指", "右戒指", "符／物品", "腰带", "鞋", "宝石" };
        var result = new List<GmEquipment>();
        for (int slot = 0; slot < slots.Length; slot++)
        {
            var entry = new GmEquipment { Slot = slot, SlotName = slots[slot], Name = "未装备" }; result.Add(entry);
            var item = slot < player.UseItems.Length ? player.UseItems[slot] : null;
            if (item == null || item.Index == 0) continue;
            entry.Equipped = true; entry.ItemIndex = item.Index; entry.MakeIndex = item.MakeIndex; entry.Effective = item.Dura > 0;
            var client = player.GetGmEquipmentItem(item);
            if (client == null) { entry.Name = "未知装备（" + item.Index + "）"; entry.Attributes.Add(Attr("状态", "道具库中未找到，保留原装备")); continue; }
            var s = client.Item; var a = entry.Attributes; entry.Name = string.IsNullOrEmpty(s.Name) ? "未命名装备" : s.Name;
            a.Add(Attr("耐久", (item.Dura / 1000m).ToString("0.###", CultureInfo.InvariantCulture) + " / " + (item.DuraMax / 1000m).ToString("0.###", CultureInfo.InvariantCulture)));
            a.Add(Attr("状态", entry.Effective ? "正常" : "已损坏，战斗属性不生效"));
            a.Add(Attr("攻击", Range(s.DC))); a.Add(Attr("魔法", Range(s.MC))); a.Add(Attr("道术", Range(s.SC)));
            switch (s.StdMode)
            {
                case 5: case 6:
                    a.Add(Attr("准确", HUtil32.HiByte(s.AC))); a.Add(Attr("幸运", HUtil32.LoByte(s.AC))); a.Add(Attr("诅咒", HUtil32.LoByte(s.MAC)));
                    a.Add(Attr("攻击速度", SystemShare.ItemSystem.RealAttackSpeed(HUtil32.HiByte(s.MAC)))); break;
                case 19:
                    a.Add(Attr("魔法躲避", HUtil32.HiByte(s.AC))); a.Add(Attr("幸运", HUtil32.HiByte(s.MAC))); a.Add(Attr("诅咒", HUtil32.LoByte(s.MAC)));
                    a.Add(Attr("准确", s.Accurate)); a.Add(Attr("攻击速度", s.AtkSpd)); break;
                case 20: case 24:
                    a.Add(Attr("准确", HUtil32.HiByte(s.AC))); a.Add(Attr("敏捷", HUtil32.HiByte(s.MAC))); a.Add(Attr("攻击速度", s.AtkSpd)); break;
                case 21: case 23:
                    a.Add(Attr(s.StdMode == 21 ? "生命恢复" : "毒物躲避", HUtil32.HiByte(s.AC)));
                    a.Add(Attr(s.StdMode == 21 ? "魔法恢复" : "中毒恢复", HUtil32.HiByte(s.MAC)));
                    a.Add(Attr("攻击速度", HUtil32.LoByte(s.AC) - HUtil32.LoByte(s.MAC) + s.AtkSpd)); break;
                default:
                    a.Add(Attr("防御", Range(s.AC))); a.Add(Attr("魔御", Range(s.MAC))); a.Add(Attr("准确", s.Accurate));
                    a.Add(Attr("敏捷", s.Agility)); a.Add(Attr("攻击速度", s.AtkSpd)); break;
            }
            a.Add(Attr("重量", s.Weight)); a.Add(Attr("生命附加", s.HpAdd)); a.Add(Attr("魔力附加", s.MpAdd)); a.Add(Attr("经验附加", s.ExpAdd));
            a.Add(Attr("魔法躲避附加", s.MgAvoid)); a.Add(Attr("毒物躲避附加", s.ToxAvoid)); a.Add(Attr("减速", s.Slowdown)); a.Add(Attr("毒素", s.Tox));
            a.Add(Attr("强度", s.Strong)); a.Add(Attr("神圣", s.Undead)); a.Add(Attr("特殊能力", s.SpecialPwr));
            if (s.EffType1 != 0) a.Add(Attr("特殊效果1", GmEffect(s.EffType1, s.EffRate1, s.EffValue1)));
            if (s.EffType2 != 0) a.Add(Attr("特殊效果2", GmEffect(s.EffType2, s.EffRate2, s.EffValue2)));
            if (s.ItemSet != 0) a.Add(Attr("套装编号", s.ItemSet));
            a.Add(Attr("佩戴条件", GmRequirement(s.Need, s.NeedLevel)));
            if (!string.IsNullOrWhiteSpace(s.Reference)) a.Add(Attr("说明", s.Reference));
        }
        return result;
    }
    private static string GmEffect(byte type, byte rate, byte value) => type switch
    {
        1 => "腕力 +" + value, 2 => "穿戴负重 +" + value, 3 => "幸运 +" + value, 4 => "背包负重 +" + value,
        5 => "生命恢复 +" + rate + "，魔法恢复 +" + value, _ => "效果 " + type + "，概率 " + rate + "，数值 " + value
    };
    private static string GmRequirement(byte type, byte value) => (type switch { 0 => "等级", 1 => "攻击", 2 => "魔法", 3 => "道术", _ => "条件 " + type }) + " ≥ " + value;
}
