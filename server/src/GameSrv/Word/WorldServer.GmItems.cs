using GameSrv.Gm;
using SystemModule;

namespace GameSrv.Word;

public partial class WorldServer
{
    internal static GmResult SearchGmItems(GmCommand command)
    {
        string query = command.Query?.Trim();
        string mode = command.MatchMode ?? "contains";
        if (string.IsNullOrEmpty(query) || query.Length > 64 || query.Any(char.IsControl) ||
            (mode != "contains" && mode != "exact" && mode != "prefix" && mode != "suffix" && mode != "wildcard"))
            return GmFailure("invalid_command", "A query of 1 to 64 characters and a supported match mode are required.");
        if (SystemShare.ItemSystem == null) return GmFailure("world_busy", "Item catalogue is not ready.");
        var result = new GmResult { Success = true, Status = "catalogue", Persistence = "not_applied" };
        for (int index = 1; index <= Math.Min(ushort.MaxValue, SystemShare.ItemSystem.ItemCount); index++)
        {
            var item = SystemShare.ItemSystem.GetStdItem((ushort)index);
            if (item == null || string.IsNullOrWhiteSpace(item.Name)) continue;
            if (!M2Server.Classic.ClassicCatalog.AllowsItem(index, item.Name)) continue;
            bool matches = mode switch
            {
                "exact" => item.Name.Equals(query, StringComparison.OrdinalIgnoreCase),
                "prefix" => item.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase),
                "suffix" => item.Name.EndsWith(query, StringComparison.OrdinalIgnoreCase),
                "wildcard" => GmWildcardMatches(item.Name, query),
                _ => item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            };
            if (!matches) continue;
            result.TotalMatches++;
            if (result.Items.Count < 200) result.Items.Add(new GmItem { Index = index, Name = item.Name });
        }
        return result;
    }

    // Bounded dynamic programming: '*' matches any sequence and '?' one character.
    private static bool GmWildcardMatches(string text, string pattern)
    {
        bool[] previous = new bool[text.Length + 1];
        previous[0] = true;
        foreach (char token in pattern)
        {
            var next = new bool[text.Length + 1];
            next[0] = token == '*' && previous[0];
            for (int i = 1; i <= text.Length; i++)
                next[i] = token == '*' ? previous[i] || next[i - 1] : previous[i - 1] &&
                    (token == '?' || char.ToUpperInvariant(token) == char.ToUpperInvariant(text[i - 1]));
            previous = next;
        }
        return previous[text.Length];
    }
}
