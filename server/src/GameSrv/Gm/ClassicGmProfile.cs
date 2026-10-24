using M2Server.Classic;

namespace GameSrv.Gm;

/// <summary>Opaque deployment identity, persisted in this world's own volume.</summary>
public static class ClassicGmProfile
{
    public const string Project = "Mir2Classic176";
    public const string Contract = "classic-gm-profile/v1";
    public const string ProjectHeader = "X-Mir2-Project";
    public const string InstanceHeader = "X-Mir2-Instance";
    public static readonly string[] Actions = new[] { "add_gold", "level_up", "set_hp_lock", "set_mp_lock", "add_item", "set_skill_level", "repair_equipment", "set_pet_level", "teleport_map" };

    public static string LoadInstance(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream);
            writer.Write(Guid.NewGuid().ToString("N")); writer.Flush(); stream.Flush(true);
        }
        catch (IOException) when (File.Exists(path)) { }
        if (!Guid.TryParse(File.ReadAllText(path).Trim(), out var id) || id == Guid.Empty)
            throw new InvalidDataException("Classic GM instance identity is invalid; retain an existing identity.");
        return id.ToString("N");
    }

    public static bool Matches(string project, string instance, string expected) =>
        project == Project && Guid.TryParse(instance, out var supplied) && Guid.TryParse(expected, out var actual) && supplied != Guid.Empty && supplied == actual;

    public static GmServerInfo Describe(string instance) => new()
    {
        Project = Project, Contract = Contract, Scope = "cn-shanda-176-2003-launch", InstanceId = instance,
        CatalogSha256 = ClassicCatalog.Sha256, MaximumLevel = ClassicCatalog.MaximumLevel,
        ItemCount = ClassicCatalog.ItemCount, SkillCount = ClassicCatalog.SkillCount, MapCount = ClassicCatalog.MapCount,
        SupportedActions = Actions.ToArray()
    };
}
