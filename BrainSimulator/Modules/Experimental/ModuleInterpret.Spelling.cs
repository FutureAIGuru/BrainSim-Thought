#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using UKS;

namespace BrainSimulator.Modules;

// Misspellings: a spelling with no exact match is looked up approximately (the same letters jumbled,
// or one missing or extra) before it is taken as a new word. One clear candidate is read in its place
// and the reply says so; a doubtful one is asked about, and the answer decides.
public sealed partial class ModuleInterpret
{
    private const float SureCorrection = 0.85f;
    private const float PossibleCorrection = 0.65f;

    // Spellings read as other words while reading the current input, for the reply.
    private readonly List<(string Typed, Thought Word)> corrections = new();
    // This session's answers to "Did you mean...?": accepted corrections, and spellings that are words.
    private readonly Dictionary<string, Thought> acceptedCorrections = new();
    private readonly HashSet<string> notMisspelled = new();
    // The question asked about a doubtful spelling, while the system waits for yes or no.
    private (string Sentence, string Typed, Thought Suggestion)? pendingCorrection;

    private sealed class UncertainSpellingException(string typed, Thought suggestion)
        : FormatException($"\"{typed}\" may be a misspelling.")
    {
        public string Typed { get; } = typed;
        public Thought Suggestion { get; } = suggestion;
    }

    /// <summary>
    /// The word a spelling with no exact match stands for, if it is close to one: the only candidate
    /// at SureCorrection or above. Null if nothing is close (a new word).
    /// </summary>
    /// <exception cref="UncertainSpellingException">A candidate is close but not clearly the one.</exception>
    private Thought? CorrectedSpelling(string spelling)
    {
        if (notMisspelled.Contains(spelling)) return null;
        if (!acceptedCorrections.TryGetValue(spelling, out Thought? word))
        {
            List<(Thought word, float confidence)> close = ModuleWord.FindWordsCloseTo(theUKS, spelling);
            if (close.Count == 0 || close[0].confidence < PossibleCorrection) return null;
            bool clear = close[0].confidence >= SureCorrection && (close.Count == 1 || close[1].confidence < SureCorrection);
            if (!clear) throw new UncertainSpellingException(spelling, close[0].word);
            word = close[0].word;
        }
        corrections.Add((spelling, word));
        return word;
    }

    // The note added to a reply when a spelling was read as another word.
    private string CorrectionNote() => corrections.Count == 0 ? "" :
        " (I read " + string.Join(", ", corrections.Select(correction => $"\"{correction.Typed}\" as \"{Spelling(correction.Word)}\"")) + ".)";

    /// <summary>
    /// Takes an input as the answer to "Did you mean...?" if it is one: a lone word meaning
    /// affirmative or negative. Yes reads the sentence again with the suggested word; no reads it
    /// with the spelling as a word of its own. Anything else is not an answer.
    /// </summary>
    private bool TryToAnswerCorrection(string input, bool rememberUnknownWords, out string report)
    {
        report = "";
        if (pendingCorrection is not var (sentence, typed, suggestion)) return false;
        pendingCorrection = null;
        string[] tokens = input.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim('.', ',', '!', '?').ToLowerInvariant()).Where(token => token.Length > 0).ToArray();
        if (tokens.Length != 1) return false;
        string? reply = ModuleWord.FindWordsBySpelling(theUKS, tokens[0]).FirstOrDefault()?
            .GetTargetOfFirstLinkOfType(means)?.Label.ToLowerInvariant();
        if (reply == "affirmative") acceptedCorrections[typed] = suggestion;
        else if (reply == "negative") notMisspelled.Add(typed);
        else return false;
        report = Input(sentence, rememberUnknownWords);
        return true;
    }
}
