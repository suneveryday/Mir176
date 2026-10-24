using OpenMir2;
using OpenMir2.Packets.ClientPackets;
using SystemModule;
using GameSrv.Gm;
using M2Server.Player;
using System.Threading.Tasks;

namespace GameSrv.Word;

public partial class WorldServer
{
    private readonly ConcurrentQueue<(GmCommand Command, TaskCompletionSource<GmResult> Completion)> _gmCommands = new();
    private int _gmQueueCount;
    private static readonly ConcurrentQueue<(PlayObject Player, long Revision)> GmSaveAcks = new();

    public Task<GmResult> GmExecuteAsync(GmCommand command, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<GmResult>(cancellationToken);
        if (command == null) return Task.FromResult(GmFailure("invalid_command", "Command required."));
        var copy = new GmCommand { CommandId = command.CommandId, Character = command.Character, Action = command.Action, Amount = command.Amount, Enabled = command.Enabled, ItemIndex = command.ItemIndex, ItemName = command.ItemName, SkillId = command.SkillId, SkillLevel = command.SkillLevel, PetId = command.PetId, PetLevel = command.PetLevel, Query = command.Query, MatchMode = command.MatchMode, MapId = command.MapId, MapName = command.MapName };
        if (Interlocked.Increment(ref _gmQueueCount) > 128)
        {
            Interlocked.Decrement(ref _gmQueueCount);
            return Task.FromResult(GmFailure("world_busy", "Command queue is full."));
        }
        var completion = new TaskCompletionSource<GmResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _gmCommands.Enqueue((copy, completion));
        // A caller timing out after enqueue must retry the SAME command ID: cancellation cannot undo an accepted command.
        return completion.Task;
    }

    public static void NotifyGmSaveAcknowledged(SystemModule.Actors.IPlayerActor player, long revision)
    {
        if (player is PlayObject gmPlayer && gmPlayer.GmState.Commands.Count > 0) GmSaveAcks.Enqueue((gmPlayer, revision));
    }

    private void ProcessGmCommands()
    {
        // ACKs carry the exact actor instance, never only a reusable character name.
        while (GmSaveAcks.TryDequeue(out var ack))
            ack.Player.GmAcknowledgedRevision = Math.Max(ack.Player.GmAcknowledgedRevision, ack.Revision);
        for (int i = 0; i < 16 && _gmCommands.TryDequeue(out var queued); i++)
        {
            Interlocked.Decrement(ref _gmQueueCount);
            try
            {
                var command = queued.Command;
                if (command.Action == "list_maps")
                {
                    queued.Completion.TrySetResult(GmMapCatalogue());
                    continue;
                }
                if (command.Action == "search_items")
                {
                    queued.Completion.TrySetResult(SearchGmItems(command));
                    continue;
                }
                if (string.IsNullOrWhiteSpace(command.Character) || command.Character.Length > 30)
                {
                    queued.Completion.TrySetResult(GmFailure("invalid_command", "Exact character name required."));
                    continue;
                }
                var player = PlayObjectList.OfType<PlayObject>().FirstOrDefault(p => string.Equals(p.ChrName, command.Character, StringComparison.OrdinalIgnoreCase));
                if (player == null || player.Ghost)
                {
                    queued.Completion.TrySetResult(GmFailure("character_offline", "Log this character into the game first."));
                    continue;
                }
                var result = ApplyGmCommand(player, command);
                if (result.Success && command.Action != "inspect" && result.Status == "applied_pending_save") SaveHumanRcd(player);
                queued.Completion.TrySetResult(result);
            }
            catch (Exception)
            {
                // Do not leak connection strings, tokens, or character content in HTTP errors.
                queued.Completion.TrySetResult(GmFailure("server_error", "Command could not be confirmed; retry the same command ID."));
            }
        }
    }

    private static GmResult GmFailure(string error, string message) => new() { Error = error, Message = message, Status = "rejected", Persistence = "not_applied" };

    internal static GmResult ApplyGmCommand(PlayObject player, GmCommand command)
    {
        if (player.Ghost || player.Death || player.IsRobot || player.BoEmergencyClose || player.BoReconnection || player.SwitchData || player.Envir == null || !player.LoginNoticeOk || player.WAbil.HP == 0)
            return GmFailure("character_unavailable", "Character must be alive, fully logged in, and not disconnecting or transferring.");
        string id = "";
        if (command.Action != "inspect")
        {
            if (!Guid.TryParse(command.CommandId, out var guid) || guid == Guid.Empty) return GmFailure("invalid_command", "A non-empty UUID commandId is required.");
            id = guid.ToString("N");
            if (player.GmState.Commands.TryGetValue(id, out var previous))
            {
                if (previous.Action != command.Action || previous.Amount != command.Amount || previous.Enabled != command.Enabled || previous.ItemIndex != command.ItemIndex || previous.ItemName != command.ItemName || previous.SkillId != command.SkillId || previous.SkillLevel != command.SkillLevel || previous.PetId != command.PetId || previous.PetLevel != command.PetLevel || previous.MapId != command.MapId || previous.MapName != command.MapName)
                    return GmFailure("command_conflict", "This command ID was already used with different parameters.");
                return GmSnapshot(player, id, previous.Revision > 0 && previous.Revision <= player.GmAcknowledgedRevision);
            }
            if (player.GmState.Commands.Count >= 1024) return GmFailure("command_ledger_full", "Durable command ledger is full; no mutation performed.");
        }
        if ((command.Action != "add_item" && (command.ItemIndex != null || command.ItemName != null)) ||
            (command.Action != "set_skill_level" && (command.SkillId != null || command.SkillLevel != null)) ||
            (command.Action != "set_pet_level" && (command.PetId != null || command.PetLevel != null)) ||
            (command.Action != "teleport_map" && (command.MapId != null || command.MapName != null)) ||
            command.Query != null || command.MatchMode != null)
            return GmFailure("invalid_command", "Unexpected action parameters.");
        Action notify = null;
        switch (command.Action)
        {
            case "inspect":
                if (!string.IsNullOrEmpty(command.CommandId))
                {
                    if (!Guid.TryParse(command.CommandId, out var inspectedId) || inspectedId == Guid.Empty)
                        return GmFailure("invalid_command", "A non-empty UUID commandId is required.");
                    string receiptId = inspectedId.ToString("N");
                    if (!player.GmState.Commands.TryGetValue(receiptId, out var receipt))
                        return GmFailure("command_not_found", "This command has not been confirmed; retain it and retry the same command ID.");
                    return GmSnapshot(player, receiptId, receipt.Revision > 0 && receipt.Revision <= player.GmAcknowledgedRevision);
                }
                return GmSnapshot(player, "", player.GmState.Commands.Values.All(r => r.Revision > 0 && r.Revision <= player.GmAcknowledgedRevision));
            case "add_gold":
                if (command.Amount <= 0 || command.Amount > int.MaxValue || command.Enabled != null) return GmFailure("invalid_command", "amount must be a positive integer.");
                if ((long)player.Gold + command.Amount > SystemShare.Config.HumanMaxGold || (long)player.Gold + command.Amount > int.MaxValue)
                    return GmFailure("gold_limit", "Gold would exceed the configured character limit.");
                player.Gold += (int)command.Amount;
                player.GoldChanged();
                break;
            case "set_hp_lock":
                if (command.Enabled == null || command.Amount != 0) return GmFailure("invalid_command", "enabled must be a boolean.");
                player.GmHpLocked = command.Enabled.Value;
                player.GmState.HpLocked = command.Enabled.Value;
                if (command.Enabled.Value) { player.WAbil.HP = player.WAbil.MaxHP; player.Abil.HP = player.Abil.MaxHP; }
                player.HealthSpellChanged();
                break;
            case "teleport_map":
                var teleportError = GmTeleport(player, command);
                if (teleportError != null) return teleportError;
                break;
            case "set_mp_lock":
                if (command.Enabled == null || command.Amount != 0) return GmFailure("invalid_command", "enabled must be a boolean.");
                player.GmMpLocked = command.Enabled.Value;
                player.GmState.MpLocked = command.Enabled.Value;
                if (command.Enabled.Value) { player.WAbil.MP = player.WAbil.MaxMP; player.Abil.MP = player.Abil.MaxMP; }
                notify = () => player.HealthSpellChanged();
                break;
            case "set_unlimited_teleport_scrolls":
                if (command.Enabled == null || command.Amount != 0) return GmFailure("invalid_command", "enabled must be a boolean.");
                if (command.Enabled.Value) return GmFailure("invalid_command", "Unlimited scrolls are unavailable in the classic project.");
                player.GmState.UnlimitedTeleportScrolls = false;
                break;
            case "add_item":
                if (command.Amount != 0 || command.Enabled != null || command.ItemIndex is not (>= 1 and <= ushort.MaxValue) || string.IsNullOrEmpty(command.ItemName))
                    return GmFailure("invalid_command", "An exact catalogue item is required.");
                var item = SystemShare.ItemSystem.GetStdItem((ushort)command.ItemIndex.Value);
                if (!M2Server.Classic.ClassicCatalog.AllowsItem(command.ItemIndex.Value, command.ItemName))
                    return GmFailure("outside_classic_catalog", "Item is outside the classic catalog.");
                if (item == null || !string.Equals(item.Name, command.ItemName, StringComparison.Ordinal))
                    return GmFailure("item_not_found", "Item catalogue changed; search and select again.");
                if (!player.IsEnoughBag()) return GmFailure("bag_full", "Backpack is full; no item added.");
                var userItem = new UserItem { Index = (ushort)command.ItemIndex.Value, MakeIndex = M2Server.M2Share.GetItemNumber(), Dura = item.DuraMax, DuraMax = item.DuraMax };
                if (!player.AddItemToBag(userItem)) return GmFailure("bag_full", "Backpack is full; no item added.");
                notify = () => player.SendAddItem(userItem);
                break;
            case "set_skill_level":
                if (command.Amount != 0 || command.Enabled != null || command.SkillId is not (>= 1 and <= ushort.MaxValue) || command.SkillLevel is not (>= 1 and <= 3))
                    return GmFailure("invalid_command", "A learned skill and target level from 1 to 3 are required.");
                var skill = player.MagicList.FirstOrDefault(m => m.MagIdx == command.SkillId.Value);
                if (!M2Server.Classic.ClassicCatalog.HasSkill(command.SkillId.Value))
                    return GmFailure("outside_classic_catalog", "Skill is outside the classic catalog.");
                if (skill?.Magic == null) return GmFailure("skill_not_learned", "Character has not learned this skill.");
                if (!M2Server.Classic.ClassicCatalog.AllowsSkill(command.SkillId.Value, skill.Magic.MagicName))
                    return GmFailure("outside_classic_catalog", "Skill identity is outside the classic catalog.");
                if (command.SkillLevel.Value <= skill.Level || command.SkillLevel.Value > Math.Min(3, (int)skill.Magic.TrainLv))
                    return GmFailure("skill_level_limit", "Target must exceed current level within the skill limit.");
                skill.Level = (byte)command.SkillLevel.Value;
                skill.TranPoint = 0;
                notify = () => player.RefreshGmSkill(skill);
                break;
            case "set_pet_level":
                var petError = SetGmPetLevel(player, command, out notify);
                if (petError != null) return petError;
                break;
            case "repair_equipment":
                var repairError = RepairGmEquipment(player, command, out notify);
                if (repairError != null) return repairError;
                break;
            case "level_up":
                if (command.Amount < 0 || command.Amount > byte.MaxValue - 1 || command.Enabled != null)
                    return GmFailure("invalid_command", "level_up amount must be from 1 to 254; omitted or zero preserves legacy +1.");
                long levelsToAdd = command.Amount == 0 ? 1 : command.Amount;
                if (player.Abil.Level + levelsToAdd > MessageSettings.MAXUPLEVEL)
                    return GmFailure("level_limit", "Requested increase would exceed the character level limit; no levels added.");
                int oldLevel = player.Abil.Level;
                player.Abil.Level = (byte)(oldLevel + levelsToAdd);
                player.HasLevelUp(oldLevel);
                break;
            default:
                return GmFailure("invalid_command", "Unsupported action.");
        }
        player.GmState.Commands.Add(id, new OpenMir2.Data.GmCommandReceipt { Action = command.Action, Amount = command.Amount, Enabled = command.Enabled, ItemIndex = command.ItemIndex, ItemName = command.ItemName, SkillId = command.SkillId, SkillLevel = command.SkillLevel, PetId = command.PetId, PetLevel = command.PetLevel, MapId = command.MapId, MapName = command.MapName });
        // Record the mutation before notifying the game client. A disconnected socket
        // must never turn a retry into a second grant.
        try { notify?.Invoke(); }
        catch (Exception ex) { LogService.Warn("GM client notification failed: " + ex.GetType().Name); }
        return GmSnapshot(player, id, false);
    }

    private static GmResult GmSnapshot(PlayObject player, string id, bool persisted) => new()
    {
        MapId = player.Envir?.MapName ?? "", MapName = player.Envir?.MapDesc ?? "", X = player.CurrX, Y = player.CurrY,
        Success = true, Character = player.ChrName, PlayerId = player.ActorId, Gold = player.Gold,
        Equipment = GmEquipmentSnapshot(player), CharacterAttributes = GmAbilitySnapshot(player.WAbil),
        Pets = GmPetSnapshot(player), CanModifyPets = CanGmModifyPets(player),
        BagUsed = player.ItemList.Count, BagCapacity = Grobal2.MaxBagItem,
        Skills = player.MagicList.Where(m => m.Magic != null && M2Server.Classic.ClassicCatalog.AllowsSkill(m.MagIdx, m.Magic.MagicName)).Select(m => new GmSkill { Id = m.MagIdx, Name = m.Magic.MagicName ?? "", Level = m.Level, MaxLevel = Math.Min(3, (int)m.Magic.TrainLv) }).OrderBy(m => m.Id).ToList(),
        Level = player.Abil.Level, Hp = player.WAbil.HP, MaxHp = player.WAbil.MaxHP, HpLocked = player.GmHpLocked,
        Mp = player.WAbil.MP, MaxMp = player.WAbil.MaxMP, MpLocked = player.GmMpLocked, UnlimitedTeleportScrolls = player.GmState.UnlimitedTeleportScrolls,
        CommandId = id, Status = persisted ? "persisted" : "applied_pending_save", Persistence = persisted ? "persisted" : "applied_pending_save"
    };
}
