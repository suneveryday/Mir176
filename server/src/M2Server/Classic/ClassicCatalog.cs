using System.Text.Json;
using System.Security.Cryptography;

namespace M2Server.Classic;

/// <summary>The classic project accepts only its independently packaged content.</summary>
public static class ClassicCatalog
{
    public const byte MaximumLevel = 48; // Engineering boundary for the legacy int32 XP wire.
    private static IReadOnlyDictionary<int, string> _items;
    private static IReadOnlyDictionary<int, string> _skills;
    private static HashSet<string> _monsters;
    private static HashSet<string> _maps;
    private static IReadOnlyDictionary<string, string> _monsterDisplayNames;
    private static volatile string _sha256;
    public static bool IsLoaded => _sha256 != null;
    public static string Sha256 => _sha256 ?? "";
    public static int ItemCount => _items?.Count ?? 0;
    public static int SkillCount => _skills?.Count ?? 0;
    public static int MapCount => _maps?.Count ?? 0;
    public static bool AllowsItem(int id, string name) => _items != null && _items.TryGetValue(id, out var expected) && expected == name;
    public static bool AllowsSkill(int id, string name) => _skills != null && _skills.TryGetValue(id, out var expected) && expected == name;
    public static bool HasSkill(int id) => _skills != null && _skills.ContainsKey(id);
    public static bool AllowsMap(string id) => _maps != null && id != null && _maps.Contains(id);

    public static void Load(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (root.GetProperty("project").GetString() != "Mir2Classic176" ||
            root.GetProperty("scope").GetString() != "cn-shanda-176-2003-launch" ||
            root.GetProperty("schema_version").GetInt32() != 1)
            throw new InvalidDataException("Classic content identity is invalid.");
        static Dictionary<int, string> ReadIds(JsonElement rows)
        {
            var result = new Dictionary<int, string>();
            foreach (var row in rows.EnumerateArray())
            {
                int id = row.GetProperty("id").GetInt32();
                string name = row.GetProperty("name").GetString();
                if (id is < 1 or > ushort.MaxValue || string.IsNullOrWhiteSpace(name) || !result.TryAdd(id, name))
                    throw new InvalidDataException("Classic content has invalid or duplicate identifiers.");
            }
            return result;
        }
        var items = ReadIds(root.GetProperty("items"));
        var skills = ReadIds(root.GetProperty("skills"));
        var monsters = root.GetProperty("monsters").EnumerateArray().Select(x => x.GetString()).ToHashSet(StringComparer.Ordinal);
        var maps = root.GetProperty("maps").EnumerateArray().Select(x => x.GetString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var displayNames = root.TryGetProperty("monster_display_names", out var aliases)
            ? aliases.EnumerateObject().ToDictionary(x => x.Name, x => x.Value.GetString(), StringComparer.Ordinal)
            : new Dictionary<string, string>();
        if (items.Count < 200 || skills.Count != 33 || monsters.Count < 100 || maps.Count < 200 ||
            monsters.Any(string.IsNullOrWhiteSpace) || maps.Any(string.IsNullOrWhiteSpace) ||
            displayNames.Any(x => !monsters.Contains(x.Key) || string.IsNullOrWhiteSpace(x.Value)))
            throw new InvalidDataException("Classic content is incomplete.");
        _items = items; _skills = skills; _monsters = monsters; _maps = maps; _monsterDisplayNames = displayNames;
        _sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    public static string MonsterDisplayName(string template) =>
        _monsterDisplayNames != null && _monsterDisplayNames.TryGetValue(template, out var name) ? name : template;

    public static void RequireItem(int id, string name)
    {
        if (_items == null || !_items.TryGetValue(id, out var expected) || expected != name)
            throw new InvalidDataException("Item is outside the classic content catalog.");
    }

    public static void RequireSkill(int id, string name)
    {
        if (_skills == null || !_skills.TryGetValue(id, out var expected) || expected != name)
            throw new InvalidDataException("Skill is outside the classic content catalog.");
    }

    public static void RequireMonster(string name)
    {
        if (_monsters == null || !_monsters.Contains(name))
            throw new InvalidDataException("Monster is outside the classic content catalog.");
    }

    public static void RequireMap(string name)
    {
        if (_maps == null || !_maps.Contains(name))
            throw new InvalidDataException("Map is outside the classic content catalog.");
    }
}
