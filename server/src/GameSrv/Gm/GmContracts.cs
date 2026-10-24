namespace GameSrv.Gm;

public sealed class GmCommand
{
    public string CommandId { get; set; } = "";
    public string Character { get; set; } = "";
    public string Action { get; set; } = "";
    public long Amount { get; set; }
    public bool? Enabled { get; set; }
    public int? ItemIndex { get; set; }
    public string ItemName { get; set; }
    public int? SkillId { get; set; }
    public int? SkillLevel { get; set; }
    public string PetId { get; set; }
    public int? PetLevel { get; set; }
    public string MapId { get; set; }
    public string MapName { get; set; }
    public string Query { get; set; }
    public string MatchMode { get; set; }
}

public sealed class GmResult
{
    public GmServerInfo Server { get; set; }
    public bool Success { get; set; }
    public string Error { get; set; } = "";
    public string Message { get; set; } = "";
    public string Character { get; set; } = "";
    public List<GmMap> Maps { get; set; } = new();
    public string MapId { get; set; } = "";
    public string MapName { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public List<GmItem> Items { get; set; } = new();
    public int TotalMatches { get; set; }
    public List<GmSkill> Skills { get; set; } = new();
    public List<GmEquipment> Equipment { get; set; } = new();
    public List<GmPet> Pets { get; set; } = new();
    public List<GmAttribute> CharacterAttributes { get; set; } = new();
    public string PetEffectLifetime { get; set; } = "current_pet";
    public bool CanModifyPets { get; set; }
    public int BagUsed { get; set; }
    public int BagCapacity { get; set; }
    public int PlayerId { get; set; }
    public int Gold { get; set; }
    public int Level { get; set; }
    public int Hp { get; set; }
    public int MaxHp { get; set; }
    public bool HpLocked { get; set; }
    public bool MpLocked { get; set; }
    public bool UnlimitedTeleportScrolls { get; set; }
    public int Mp { get; set; }
    public int MaxMp { get; set; }
    public string Persistence { get; set; } = "";
    public string Status { get; set; } = "";
    public string CommandId { get; set; } = "";
}

public sealed class GmServerInfo
{
    public string Project { get; set; } = "";
    public string Contract { get; set; } = "";
    public string Scope { get; set; } = "";
    public string InstanceId { get; set; } = "";
    public string CatalogSha256 { get; set; } = "";
    public int MaximumLevel { get; set; }
    public int ItemCount { get; set; }
    public int SkillCount { get; set; }
    public int MapCount { get; set; }
    public string[] SupportedActions { get; set; } = Array.Empty<string>();
}

public sealed class GmItem
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
}

public sealed class GmSkill
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Level { get; set; }
    public int MaxLevel { get; set; }
}

public sealed class GmAttribute
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
}
public sealed class GmEquipment
{
    public int Slot { get; set; }
    public string SlotName { get; set; } = "";
    public string Name { get; set; } = "";
    public int ItemIndex { get; set; }
    public int MakeIndex { get; set; }
    public bool Equipped { get; set; }
    public bool Effective { get; set; }
    public List<GmAttribute> Attributes { get; set; } = new();
}
public sealed class GmPet
{
    public string Id { get; set; } = "";
    public int ActorId { get; set; }
    public string Name { get; set; } = "";
    public int Level { get; set; }
    public int MaxLevel { get; set; } = 7;
    public int Hp { get; set; }
    public int MaxHp { get; set; }
    public int AttackMin { get; set; }
    public int AttackMax { get; set; }
}

public sealed class GmMap
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public bool CanTeleport { get; set; }
    public string UnavailableReason { get; set; } = "";
}
