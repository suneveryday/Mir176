using System.Runtime.CompilerServices;
using SystemModule.Actors;

namespace M2Server.Classic;

/// <summary>Variant template identity survives a shared historical display name.</summary>
public static class ClassicMonsterIdentity
{
    private sealed record Template(string Name);
    private static readonly ConditionalWeakTable<IMonsterActor, Template> Templates = new();

    public static void Bind(IMonsterActor actor, string template)
    {
        ClassicCatalog.RequireMonster(template);
        Templates.Add(actor, new Template(template));
        actor.ChrName = ClassicCatalog.MonsterDisplayName(template);
    }

    public static string TemplateName(IMonsterActor actor) =>
        Templates.TryGetValue(actor, out var template) ? template.Name : actor.ChrName;
}
