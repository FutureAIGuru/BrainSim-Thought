#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using UKS;

namespace BrainSimulator.Modules;

// Pronouns: every pronoun means one stand-in, pronounAntecedent (BasicWords), which is replaced by
// what it refers to before anything is acted on. The antecedent is the first thing the sentence is
// about, found by following its meanings' sources (no counting); in a sentence about nothing else
// ("Is he cold?"), what the previous understood sentence was about. Gender and number are not checked.
public sealed partial class ModuleInterpret
{
    // What the last understood sentence was about, for a pronoun in the next one.
    private Thought? lastSubject;

    /// <summary>
    /// The meanings with each pronoun replaced by its antecedent, which is also returned (null when
    /// the sentence has no pronoun). Remembers what this sentence is about for the next one.
    /// </summary>
    /// <exception cref="FormatException">A pronoun has nothing to refer to.</exception>
    private List<Thought> WithAntecedents(List<Thought> meanings, out Thought? antecedent)
    {
        antecedent = null;
        Thought? standIn = theUKS.Labeled("pronounAntecedent");
        Thought? subject = meanings.SelectMany(meaning => Subjects(meaning, standIn)).FirstOrDefault();
        if (standIn is not null && meanings.Any(meaning => Mentions(meaning, standIn)))
        {
            antecedent = subject ?? lastSubject ?? throw new FormatException("I don't know what the pronoun refers to.");
            Thought replacement = antecedent;
            meanings = meanings.Select(meaning => Substituted(meaning, standIn, replacement)).ToList();
        }
        lastSubject = subject ?? antecedent ?? lastSubject;
        return meanings;
    }

    // What a meaning is about, outermost first: the source of a relationship, and of a relationship
    // nested in it ([[fido->is->cold]->IF->[he->...]] is about fido). A described referent is itself
    // a subject (it is resolved where it is used); the pronoun stand-in is not.
    private IEnumerable<Thought> Subjects(Thought? meaning, Thought? standIn)
    {
        if (meaning is null || ReferenceEquals(meaning, standIn)) yield break;
        if (meaning is not Link relationship || IsReferentDescriptor(meaning)) { yield return meaning; yield break; }
        foreach (Thought subject in Subjects(relationship.From, standIn)) yield return subject;
        if (relationship.To is Link nested && !IsReferentDescriptor(nested))
            foreach (Thought subject in Subjects(nested, standIn)) yield return subject;
    }

    // A meaning with one Thought replaced, at any depth (a new Link where anything changed).
    private Thought Substituted(Thought meaning, Thought from, Thought to)
    {
        if (ReferenceEquals(meaning, from)) return to;
        if (meaning is not Link relationship) return meaning;
        Thought? source = relationship.From is null ? null : Substituted(relationship.From, from, to);
        Thought? target = relationship.To is null ? null : Substituted(relationship.To, from, to);
        return ReferenceEquals(source, relationship.From) && ReferenceEquals(target, relationship.To)
            ? relationship : new Link(source, relationship.LinkType, target);
    }

    private static string PronounReport(Thought antecedent) => $"\n\nPronoun\nrefers to {antecedent}";
}
