#nullable enable

using System.Collections.Generic;
using System.Linq;
using UKS;

namespace BrainSimulator.Modules;

// Learning by match / capture / compete / consolidate (prune comes later). Nothing here is about words:
// it deals in patterns, their weights, whether a reading succeeded, and what each pattern stated, so it
// can move into the UKS once it proves itself.
//  - Capture: a new pattern starts weak and plastic, from a single presentation.
//  - Compete: patterns reading the same input compete by weight; only the winner's reading is acted on.
//    Losing costs nothing (the loser may be right elsewhere), and merely matching is not evidence.
//  - Strengthen: a winner whose reading succeeds moves halfway to 1.
//  - Consolidate: past ConsolidationWeight a pattern stops being plastic. Its weight is not fixed by this.
public sealed partial class ModuleInterpret
{
    private const float InitialPatternWeight = 0.25f;
    private const float ConsolidationWeight = 0.8f;

    // A new pattern, captured from one presentation: weak and plastic.
    private static void Capture(Thought pattern)
    {
        pattern.Weight = InitialPatternWeight;
        pattern.isPlastic = true;
    }

    // A pattern whose reading succeeded moves halfway to certainty, and consolidates once strong enough.
    private static void Strengthen(Thought pattern)
    {
        pattern.Weight += (1 - pattern.Weight) / 2;
        if (pattern.isPlastic && pattern.Weight >= ConsolidationWeight) pattern.isPlastic = false;
    }

    // Patterns in competition for the same input, strongest first. Ties keep their order.
    private static List<Thought> ByWeight(IEnumerable<Thought> patterns) =>
        patterns.OrderByDescending(pattern => pattern.Weight).ToList();

    /// <summary>
    /// The meanings of the readings that lost: those attached to rival matches, except any the
    /// winner means too. They are not acted on.
    /// </summary>
    private HashSet<Thought> LosingMeanings(IEnumerable<Link> rivals, Link winner)
    {
        var won = new HashSet<Thought>(winner.LinksTo.Where(link => link.LinkType == means).Select(link => link.To));
        return new HashSet<Thought>(rivals.Where(rival => !ReferenceEquals(rival, winner))
            .SelectMany(rival => rival.LinksTo.Where(link => link.LinkType == means).Select(link => link.To))
            .Where(meaning => !won.Contains(meaning)));
    }

    // Records the pattern that stated each fact (statedBy), so its evidence can be traced later.
    private void RecordSource(IEnumerable<Link> facts, Thought pattern)
    {
        Thought statedBy = theUKS.GetOrAddThought("statedBy", "LinkType");
        foreach (Link fact in facts) fact.AddLink(statedBy, pattern);
    }
}
