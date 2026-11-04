// The input side of ModuleSequenceSegmenterDlg.xaml.cs: turning typed text or a corpus line
// into the learner's observations. On the desktop this lives in the WPF dialog, which the iOS
// build doesn't compile, so the platform-neutral parts are copied here unchanged.
using System.Text.RegularExpressions;
using UKS;

namespace BrainSimulator.Modules;

static class SequenceSegmenterInput
{
    /// <summary>Text entry supplies character ATs and an explicit trailing pause.</summary>
    public static string SubmitLine(ModuleSequenceSegmenter module, string input)
    {
        input = (input ?? string.Empty).TrimEnd();
        int marks = 0;
        while (input.EndsWith('!'))
        {
            marks++;
            input = input[..^1];
        }
        float emphasis = marks switch
        {
            0 => 0,
            1 => 1,
            2 => module.ConsolidationStrength / 2,
            _ => module.ConsolidationStrength,
        };
        List<Thought> observations = InputSymbols(module, input);
        observations.Add(module.InputPause);
        return module.InputReceived(observations, emphasis);
    }

    public static List<Thought> InputSymbols(ModuleSequenceSegmenter module, string input)
    {
        module.PatternRoot = module.theUKS.Labeled("Word");
        module.PatternPrefix = "w:";
        List<Thought> observations = new();
        foreach (char character in input ?? string.Empty)
        {
            if (!char.IsLetter(character)) continue;
            Thought observation = module.theUKS.Labeled(UKS.UKS.GetCharacterLabel(character));
            if (observation is not null) observations.Add(observation);
        }
        return observations;
    }

    /// <summary>
    /// CorpusReader's line handling: text from the first tab on is metadata, punctuation is
    /// stripped, and lines without letters are skipped. Null if the line isn't a phrase.
    /// </summary>
    public static string CorpusPhrase(string line)
    {
        int tab = line.IndexOf('\t');
        string phrase = (tab < 0 ? line : line[..tab]).Trim();
        if (phrase.Length == 0) return null;
        phrase = Regex.Replace(phrase, @"\p{P}", "").Trim();
        return Regex.IsMatch(phrase, @"\p{L}") ? phrase : null;
    }

    public static string BuildRandomPhrase(IReadOnlyList<KeyValuePair<string, double>> vocabulary,
        int wordsPerSequence, Random random)
    {
        if (wordsPerSequence > 1 && vocabulary.Count < 2)
            throw new ArgumentException("At least two words are needed to avoid adjacent repeats.");
        double total = 0;
        foreach (var entry in vocabulary) total += entry.Value;
        string input = string.Empty;
        string previous = null;
        double previousWeight = 0;
        for (int index = 0; index < wordsPerSequence; index++)
        {
            double target = random.NextDouble() * (total - previousWeight);
            string selected = null;
            double selectedWeight = 0;
            foreach (var entry in vocabulary)
            {
                if (entry.Key == previous) continue;
                selected = entry.Key;
                selectedWeight = entry.Value;
                target -= entry.Value;
                if (target < 0) break;
            }
            input += selected;
            previous = selected;
            previousWeight = selectedWeight;
        }
        return input;
    }
}
