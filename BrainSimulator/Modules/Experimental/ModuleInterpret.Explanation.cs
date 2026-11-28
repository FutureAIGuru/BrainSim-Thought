#nullable enable

using System.Collections.Generic;
using System.Linq;
using UKS;

namespace BrainSimulator.Modules;

// Explaining a yes/no answer from the rules the UKS evaluated to reach it: the conditions that held
// (UKS.Why) give "because", the ones that failed (UKS.WhyNot) say why the answer is not known. A
// failed condition is either contradicted (Fido is inside, so not outside) or simply not known.
public sealed partial class ModuleInterpret
{
    // English frames for the explanations. They are fixed text for now; later they may be templates.
    private const string BecauseFrame = "{0}, because {1}.";
    private const string RuleFrame = "{0} if {1}";
    private const string ButFrame = "{0}, but {1}.";
    private const string NotKnownFrame = "I don't know whether {0}";
    private const string AndWord = " and ";

    // A rule bearing on the question just asked: its result, its condition's leaves (the clauses an
    // AND joins), which held, and for each that failed, a fact that contradicts it (or null).
    private sealed record RuleEvidence(Link Result, List<Link> Conditions, List<Link> Held, List<(Link Condition, Link? Contradiction)> Failed);

    // The evidence for the yes/no question executed last in this sentence.
    private List<RuleEvidence> evidence = new();

    /// <summary>
    /// Records why a yes/no question was answered as it was: for each rule whose result is what was
    /// asked, which of its conditions held and which failed (and what contradicts those). Must be
    /// called right after the query, while UKS.Why and WhyNot still describe it.
    /// </summary>
    private void RecordEvidence(Link query)
    {
        var held = new HashSet<Link>(theUKS.Why(), ReferenceEqualityComparer.Instance);
        var failed = new HashSet<Link>(theUKS.WhyNot(), ReferenceEqualityComparer.Instance);
        evidence = RulesFor(query).Select(rule =>
        {
            List<Link> conditions = Leaves(rule.Condition).ToList();
            return new RuleEvidence(rule.Result, conditions, conditions.Where(held.Contains).ToList(),
                conditions.Where(failed.Contains).Select(condition => (condition, Contradiction(condition))).ToList());
        }).ToList();
    }

    // The stored rules whose result is the relationship a yes/no question asks about: possibilities
    // ([fido->is.?->wet]) on the subject with an IF to their condition.
    private IEnumerable<(Link Result, Link Condition)> RulesFor(Link query)
    {
        if (query.From is null || query.To is null || theUKS.GetPlainLinkType(query.LinkType) is not Thought type ||
            theUKS.Labeled(type.Label + ".?") is not Thought possible) yield break;
        foreach (Link result in query.From.LinksTo.Where(link => link.LinkType == possible && link.To == query.To))
            foreach (Link rule in result.LinksTo.Where(link => link.LinkType?.HasProperty("isConditional") == true))
                if (rule.To is Link condition) yield return (result, condition);
    }

    // The clauses of a condition: those an AND joins, at any depth.
    private static IEnumerable<Link> Leaves(Link condition) =>
        condition.LinkType?.HasProperty("isConjunction") == true
            ? new[] { condition.From, condition.To }.OfType<Link>().SelectMany(Leaves)
            : new[] { condition };

    // What is known that rules a condition out: a value exclusive with it ("inside" for "outside"),
    // or its explicit negation (is.not).
    private Link? Contradiction(Link condition)
    {
        if (Asserted(condition) is not Link assumed || assumed.From is null) return null;
        List<Link> known = theUKS.GetAttributes(assumed.From).ToList();
        Thought? negation = theUKS.Labeled(assumed.LinkType.Label + ".not");
        return known.FirstOrDefault(fact => theUKS.LinksAreExclusive(assumed, fact)) ??
            known.FirstOrDefault(fact => negation is not null && fact.LinkType == negation && fact.To == assumed.To);
    }

    // A possibility as the plain relationship it describes ([fido->located.?->outside] → located).
    private Link? Asserted(Link possibility) =>
        theUKS.GetPlainLinkType(possibility.LinkType) is Thought type ? new Link(possibility.From, type, possibility.To) : null;

    private string Clause(Link? fact) => fact is null ? "" : GenerateClause(fact) ?? fact.ToString();

    private string Conditions(RuleEvidence rule) => string.Join(AndWord, rule.Conditions.Select(condition => Clause(Asserted(condition))));

    /// <summary>
    /// One answer to a question: on an assumption, said with it ("Fido is wet if Fido is outside.");
    /// otherwise with the reason a rule gives, if one does.
    /// </summary>
    private string Answered(Link result, Thought question)
    {
        string answer = GenerateSentence(result, question) ?? result.ToString();
        return assumption is not null && Asserted(assumption) is Link assumed
            ? string.Format(RuleFrame, answer.TrimEnd('.'), Clause(assumed)) + "."
            : WithReason(answer, result);
    }

    /// <summary>
    /// A yes answer with its reason: "Fido is wet, because Fido is outside." Unchanged when no rule
    /// with all its conditions met supports it (the fact is simply stored, or inherited).
    /// </summary>
    private string WithReason(string answer, Link result)
    {
        RuleEvidence? rule = evidence.FirstOrDefault(rule => rule.Result.To == result.To && rule.Held.Count == rule.Conditions.Count);
        return rule is null ? answer : string.Format(BecauseFrame, answer.TrimEnd('.'), Conditions(rule));
    }

    /// <summary>
    /// Why an answer is not known, from each rule that could have given it: "Fido is wet if Fido is
    /// outside, but Fido is inside." or "..., but I don't know whether Fido is outside." Empty when
    /// no rule bears on the question.
    /// </summary>
    private string WhyNotKnown() => string.Join(" ", evidence.Where(rule => rule.Failed.Count > 0).Select(rule =>
        string.Format(ButFrame, string.Format(RuleFrame, Clause(Asserted(rule.Result)), Conditions(rule)),
            string.Join(AndWord, rule.Failed.Select(failed => failed.Contradiction is Link known
                ? Clause(known) : string.Format(NotKnownFrame, Clause(Asserted(failed.Condition))))))));
}
