using M2Server.Actor;
using M2Server.Player;
using OpenMir2;
using OpenMir2.Consts;
using OpenMir2.Enums;
using SystemModule;
using SystemModule.Actors;
using SystemModule.Enums;
namespace M2Server
{
    public enum BuffType : byte
    {
        /// <summary>
        /// 绿毒
        /// </summary>
        GreenPoison = 0,
        /// <summary>
        /// 红毒
        /// </summary>
        RedPoison = 1,
        Defense = 2,
        /// <summary>
        /// 不能走动
        /// </summary>
        CanNotRun = 3,
        /// <summary>
        /// 不能移动
        /// </summary>
        CanNotMove = 4,
        Paralysis = 5,
        ReduceHP = 6,
        Frozen = 7,
        /// <summary>
        /// 魔法隐身
        /// </summary>
        Transparent = 8,
        /// <summary>
        /// 防御力
        /// </summary>
        DefensePower = 9,
        /// <summary>
        /// 魔法防御力
        /// </summary>
        MagicDefensePower = 10,
        /// <summary>
        /// 魔法盾
        /// </summary>
        MagicShield = 11
    }

    // Keep the legacy status array in sync: combat and movement still read it.
    public class ActorBuffSystem
    {
        private readonly Dictionary<int, IList<ActorBuffData>> ActorBuffMap = new();
        private readonly IList<IActor> ActorBuffs = new List<IActor>();
        private readonly object SyncRoot = new();
        public void AddBuff(IActor actor, BuffType buffType, int duraTime, int stateval)
        {
            lock (SyncRoot)
            {
                int slot = (int)buffType;
                if (slot < 0 || slot >= actor.StatusTimeArr.Length || duraTime <= 0) return;
                if (!ActorBuffMap.TryGetValue(actor.ActorId, out var buffs))
                    ActorBuffMap[actor.ActorId] = buffs = new List<ActorBuffData>();
                var buff = buffs.FirstOrDefault(x => x.BuffType == buffType);
                if (buff == null) { buff = new ActorBuffData { ActorId = actor.ActorId, BuffType = buffType }; buffs.Add(buff); }
                // A weaker recast must not shorten an active effect.
                buff.StateTick = Math.Max(actor.StatusTimeArr[slot], Math.Min(ushort.MaxValue, duraTime));
                buff.StateVal = stateval;
                buff.StatusArrTick = HUtil32.GetTickCount();
                actor.StatusTimeArr[slot] = (ushort)buff.StateTick;
                if (!ActorBuffs.Contains(actor)) ActorBuffs.Add(actor);
            }
        }
        public void DoWork(object obj)
        {
            lock (SyncRoot)
            {
                int now = HUtil32.GetTickCount();
                for (int i = ActorBuffs.Count - 1; i >= 0; i--)
                {
                    var actor = ActorBuffs[i];
                    var buffs = ActorBuffMap[actor.ActorId];
                    bool changed = false, recalc = false;
                    if (actor.Ghost) { buffs.Clear(); }
                    for (int j = buffs.Count - 1; j >= 0; j--)
                    {
                        var buff = buffs[j]; int slot = (int)buff.BuffType;
                        // Existing attack, movement, cure and shield-hit paths may shorten it.
                        buff.StateTick = Math.Min(buff.StateTick, actor.StatusTimeArr[slot]);
                        int elapsed = unchecked(now - buff.StatusArrTick);
                        if (buff.StateTick > 0 && buff.StateTick < 60000 && elapsed >= 1000)
                        {
                            int seconds = elapsed / 1000;
                            buff.StateTick = Math.Max(0, buff.StateTick - seconds);
                            buff.StatusArrTick = unchecked(buff.StatusArrTick + seconds * 1000);
                            actor.StatusTimeArr[slot] = (ushort)buff.StateTick;
                        }
                        if (buff.StateTick == 0)
                        {
                            actor.StatusTimeArr[slot] = 0;
                            changed = true;
                            if (buff.BuffType == BuffType.Transparent) { actor.HideMode = false; actor.Transparent = false; }
                            if (buff.BuffType == BuffType.MagicShield && actor is CharacterObject character) character.AbilMagBubbleDefence = false;
                            if (buff.BuffType == BuffType.DefensePower || buff.BuffType == BuffType.MagicDefensePower)
                            {
                                recalc = true;
                                if (actor is IPlayerActor player) player.SysMsg(buff.BuffType == BuffType.DefensePower ? "防御力回复正常." : "魔法防御力回复正常.", MsgColor.Green, MsgType.Hint);
                            }
                            buffs.RemoveAt(j);
                        }
                    }
                    if (!actor.Death && !actor.Ghost && unchecked(now - actor.PoisoningTick) > SystemShare.Config.PosionDecHealthTime)
                    {
                        actor.PoisoningTick = now;
                        var poison = buffs.FirstOrDefault(x => x.BuffType == BuffType.GreenPoison && x.StateTick > 0);
                        if (poison != null)
                        {
                            if (actor.Animal) { var animal = (AnimalObject)actor; animal.MeatQuality = (ushort)Math.Max(0, animal.MeatQuality - 1000); }
                            actor.DamageHealth(poison.StateVal + 1);
                            actor.HealthTick = 0; actor.SpellTick = 0; actor.HealthSpellChanged();
                        }
                    }
                    if (changed) { actor.CharStatus = actor.GetCharStatus(); actor.StatusChanged(); }
                    if (recalc) { actor.RecalcAbilitys(); actor.SendMsg(Messages.RM_ABILITY, 0, 0, 0, 0); }
                    if (buffs.Count == 0) { ActorBuffMap.Remove(actor.ActorId); ActorBuffs.RemoveAt(i); }
                }
            }
        }
        public int GetBuff(IActor actor, BuffType type)
        {
            lock (SyncRoot) return ActorBuffMap.TryGetValue(actor.ActorId, out var buffs) ? buffs.FirstOrDefault(x => x.BuffType == type && x.StateTick > 0 && actor.StatusTimeArr[(int)type] > 0)?.StateVal ?? 0 : 0;
        }
        public int GetBuffStatus(IActor actor)
        {
            int status = 0;
            for (int i = 0; i < actor.StatusTimeArr.Length; i++) if (actor.StatusTimeArr[i] > 0) status |= unchecked((int)(0x80000000u >> i));
            return status;
        }
        public bool HasBuff(IActor actor, BuffType type)
        {
            lock (SyncRoot) return actor.StatusTimeArr[(int)type] > 0 && ActorBuffMap.TryGetValue(actor.ActorId, out var buffs) && buffs.Any(x => x.BuffType == type && x.StateTick > 0);
        }
        public bool TryUpdate(IActor actor, BuffType type, int duraTime)
        {
            lock (SyncRoot)
            {
                if (!HasBuff(actor, type)) return false;
                AddBuff(actor, type, duraTime, GetBuff(actor, type)); return true;
            }
        }
        public void Remove() { }
    }
    public class ActorBuffData
    {
        public int ActorId { get; set; }
        public BuffType BuffType { get; set; }
        public int StateVal { get; set; }
        public int StateTick { get; set; }
        public int StatusArrTick { get; set; }
        public int DestroyTick { get; set; }
    }
}
