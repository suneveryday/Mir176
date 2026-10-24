using System.Text.Json;

namespace SystemModule.Data
{
    // Owned by the character snapshot. Never write a separate NPC queue to the database.
    public static class WeaponUpgradeEscrow
    {
        private static readonly object Gate = new();
        private static readonly Dictionary<string, Dictionary<string, WeaponUpgradeInfo>> Records = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, long> Revisions = new(StringComparer.OrdinalIgnoreCase);
        private static readonly JsonSerializerOptions Options = new() { IncludeFields = true };

        public static void Restore(string character, string json, long revision = 0)
        {
            var entries = string.IsNullOrEmpty(json)
                ? new Dictionary<string, WeaponUpgradeInfo>()
                : JsonSerializer.Deserialize<Dictionary<string, WeaponUpgradeInfo>>(json, Options);
            if (entries == null) throw new InvalidDataException("Invalid weapon escrow snapshot");
            lock (Gate) { Records[character] = entries; Revisions[character] = revision; }
        }

        public static long NextRevision(string character)
        {
            lock (Gate)
            {
                Revisions.TryGetValue(character, out long revision);
                return Revisions[character] = checked(revision + 1);
            }
        }

        public static string Capture(string character)
        {
            lock (Gate) return JsonSerializer.Serialize(Records.TryGetValue(character, out var entries)
                ? entries : new Dictionary<string, WeaponUpgradeInfo>(), Options);
        }

        public static WeaponUpgradeInfo? Get(string character, string npc)
        {
            lock (Gate) return Records.TryGetValue(character, out var entries) && entries.TryGetValue(npc, out var value)
                ? JsonSerializer.Deserialize<WeaponUpgradeInfo>(JsonSerializer.Serialize(value, Options), Options) : null;
        }

        public static void Put(string character, string npc, WeaponUpgradeInfo value)
        {
            lock (Gate)
            {
                if (!Records.TryGetValue(character, out var entries)) Records[character] = entries = new();
                entries[npc] = value;
            }
        }

        public static void Remove(string character, string npc)
        {
            lock (Gate) if (Records.TryGetValue(character, out var entries)) entries.Remove(npc);
        }
    }
}
