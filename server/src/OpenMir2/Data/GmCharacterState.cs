using System.Collections.Generic;

namespace OpenMir2.Data;

// Stored atomically with the complete character snapshot. Never sent to the legacy client.
public sealed class GmCharacterState
{
    public bool HpLocked { get; set; }
    public bool MpLocked { get; set; }
    public bool UnlimitedTeleportScrolls { get; set; }
    public Dictionary<string, GmCommandReceipt> Commands { get; set; } = new();
}

public sealed class GmCommandReceipt
{
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
    public long Revision { get; set; }
}
