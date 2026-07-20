/*
 * Brain Simulator Thought
 *
 * Copyright (c) 2026 Charles Simon
 *
 * This file is part of Brain Simulator Thought and is licensed under
 * the MIT License. You may use, copy, modify, merge, publish, distribute,
 * sublicense, and/or sell copies of this software under the terms of
 * the MIT License.
 *
 * See the LICENSE file in the project root for full license information.
 */

using Pluralize.NET;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Windows.Media.Imaging;
using UKS;

namespace BrainSimulator.Modules;

public class ModuleTextIn : ModuleBase
{
    public override void Fire()
    {
        Init();
        UpdateDialog();
    }

    public override void Initialize()
    {
    }

    public override void UKSInitializedNotification()
    {
        theUKS.GetOrAddThought("LanguageElement", "Thought");

        theUKS.GetOrAddThought("Phrase", "LanguageElement");
        theUKS.GetOrAddThought("Word", "LanguageElement");
        theUKS.GetOrAddThought("WordType", "Word");
        theUKS.GetOrAddThought("Sentence", "LanguageElement");
        theUKS.GetOrAddThought("Template", "LanguageElement");
        theUKS.GetOrAddThought("hasWords", "LinkType");
        theUKS.GetOrAddThought("means", "LinkType");
        theUKS.GetOrAddThought("spelled", "LinkType");
        theUKS.GetOrAddThought("pronounced", "LinkType");
    }

    public string SubmitText(string text)
    {
        string answer = null;
        string trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;

        //find the key words in the text 
        var keywords = FindKeywords(trimmed);

        //find known phrases and replace the meaning in the keyword list
        var parameters = FindAndMapTemplates(keywords);

        //now convert words to meanings
        if (parameters?.Count > 2)
        {
            bool isQuery = parameters.FindFirst(x => x.Label.Contains(":??")) is not null;
            Link l = BuildLink(parameters[0..3]);
            //Statement or query?  Submit
            if (isQuery)
            {
                var results = theUKS.SearchForRelationships(l);
                var words = ConvertRelationshipsToWords(results);
                answer = string.Join(" ", words.Select(x => x.Label[2..]));
                if (dlg is ModuleTextInDlg d)
                {
                    d.Answer(answer);
                }
            }
            else
            {
                theUKS.AddStatement(l.From, l.LinkType, l.To);
                if (dlg is ModuleTextInDlg d)
                {
                    d.AddParsedOutput(l);
                }
            }
        }
        else
        {
            //could not find a template, so just use the keywords as parameters
        }
        return answer;
    }

    List<Thought> ConvertRelationshipsToWords(List<Link> result)
    {
        List<Thought> retVal = new();
        if (result.Count == 0) return retVal;

        foreach (Link link in result)
        {
            AddWordForMeaning(retVal, link.From);
            AddWordForMeaning(retVal, link.LinkType);
            AddWordForMeaning(retVal, link.To);
        }

        return retVal;
    }

    private void AddWordForMeaning(List<Thought> words, Thought meaning)
    {
        if (meaning is null)
        {
            words.Add(theUKS.GetOrAddThought("w:null", "word"));
            return;
        }

        Thought word = meaning.LinksFrom
            .FirstOrDefault(x => x.LinkType?.Label == "means" &&
                                 x.From is not null &&
                                 x.From.HasAncestor("word"))
            ?.From;

        if (word is not null)
        {
            words.Add(word);
            return;
        }

        // Then try a phrase: p:is|a -> means -> is-a
        Thought phrase = meaning.LinksFrom
            .FirstOrDefault(x => x.LinkType?.Label == "means" &&
                                 x.From is not null &&
                                 x.From.HasAncestor("phrase"))
            ?.From;

        if (phrase is not null)
        {
            SeqElement phraseSequence =
                phrase.GetTargetOfFirstLinkOfType("contains") as SeqElement ??
                phrase.GetTargetOfFirstLinkOfType("hasWords") as SeqElement;

            if (phraseSequence is not null)
            {
                words.AddRange(theUKS.FlattenSequence(phraseSequence));
                return;
            }
        }

        // Fallback: invent a word if no word or phrase exists yet
        word = theUKS.GetOrAddThought("w:" + meaning.Label, "word");
        word.AddLink("means", meaning);
        words.Add(word);
    }

    /// <summary>
    /// Finds and applies templates to map input word sequences to output link structures.
    /// Templates support wildcards (w:??) that are captured and referenced in outputs.
    /// </summary>
    private List<Thought> FindAndMapTemplates(List<Thought> keywords)
    {
        // Try to match templates, starting with longest sequences first
        for (int length = keywords.Count; length >= 2; length--)
        {
            for (int i = 0; i <= keywords.Count - length; i++)
            {
                var subsequence = keywords.GetRange(i, length);

                // Use HasSequenceByActivation to find matching templates
                //var matchResults = theUKS.HasSequence2(subsequence, "hasWords", true, true);
                var matchResults = theUKS.FindSequencesByActivation(subsequence, "TemplateSequenceSearch");

                if (matchResults.Count > 0)
                {
                    // Found a matching template
                    foreach (var match in matchResults)
                    {
                        Thought template = match.seqNode.LinksFrom.FindFirst(x => x.LinkType?.Label == "hasWords")?.From;
                        // Get the captured wildcards from the match
                        var inputParams = theUKS.FlattenSequence(template.GetTargetOfFirstLinkOfType("hasWords") as SeqElement);
                        // Generate the output link
                        SeqElement outputPattern = template.GetTargetOfFirstLinkOfType("outputs") as SeqElement;
                        var outputParams = theUKS.FlattenSequence(outputPattern);
                        // Fill in parameters in the output template
                        for (int j = 0; j < outputParams.Count; j++)
                        {
                            if (outputParams[j].Label.Contains("??")) //is-a paramword
                            {
                                if (int.TryParse(outputParams[j].Label[4..], out int index))
                                {
                                    index--; //make is zero-based
                                    int inputPos = inputParams.Select((word, i) => new { word, index = i })
                                        .Where(x => x.word.Label == "w:??")
                                        .ElementAt(index).index;
                                    outputParams[j] = keywords[inputPos];
                                }
                            }
                        }
                        return outputParams;
                    }
                }
            }
        }

        return null;
    }


    private List<Thought> FindKeywords(string trimmed)
    {
        var retVal = new List<Thought>();
        var words = trimmed.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 3) return retVal;

        var language = "word";
        if (words.Contains("es")) language = "SpanishWOrd";

        IPluralize pluralizer = new Pluralizer();

        for (int i = 0; i < words.Length; i++)
        {
            string word = words[i];
            if (char.IsLower(words[i][0]) && language == "word")
                word = pluralizer.Singularize(words[i]);
            Thought theWord = theUKS.GetOrAddThought("w:" + word, language);
            retVal.Add(theWord);
        }
        //hack for dogs are mammals
        if (retVal[1].Label == "w:is" && !pluralizer.IsSingular(words[2]))
        {
            retVal[1] = theUKS.Labeled("w:is");
            retVal.Insert(2, theUKS.Labeled("w:a"));
        }
        return retVal;
    }

    private Thought FindWorkingLanguage(List<Thought> keyWords)
    {
        if (keyWords is null || keyWords.Count == 0) return null;

        var parentCounts = new Dictionary<Thought, int>();
        foreach (var word in keyWords)
        {
            foreach (var parent in word.Parents)
            {
                if (parent is null) continue;
                parentCounts[parent] = parentCounts.TryGetValue(parent, out int c) ? c + 1 : 1;
            }
        }

        Thought language = parentCounts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key.Label)
            .Select(kv => kv.Key)
            .FirstOrDefault();
        return language;
    }
    private Link BuildLink(List<Thought> keyWords)
    {
        Link l = new();
        if (keyWords.Count < 3) return l;
        //special hack to add new meanings
        if (keyWords[1].Label == "w:mean")
        {
            if (keyWords.Count == 3)
            {
                l.From = keyWords[0];
                l.LinkType = "means";
                l.To = keyWords[2].LinksTo.FindFirst(x => x.LinkType.Label == "means")?.To;
            }
            return l;
        }

        List<List<Thought>> meanings = new();
        foreach (Thought t in keyWords)
        {
            if (t.HasAncestor("word"))
                meanings.Add(t.LinksTo.Where(x => x.LinkType.Label == "means").Select(x => x.To).ToList());
            else
                meanings.Add(new List<Thought> { t });
        }

        List<Link> attributes = new();
        for (int i = 0; i < meanings.Count; i++)
        {
            List<Thought> meaning = meanings[i];
            if (meaning.Count == 0)
            {
                Thought newMeaning = null;

                if (newMeaning is null)
                    newMeaning = theUKS.GetOrAddThought(keyWords[i].Label[2..]);
                meaning.Add(newMeaning);
                keyWords[i].AddLink("means", newMeaning);
            }
            else if (meaning.Count > 1)
            {
                //choose the best meaning for this context
                Thought bestMeaning = null;
                DateTime bestFiredTime = DateTime.MinValue;
                foreach (Thought t in meaning)
                {
                    DateTime recentFiredTime = t.LinksFrom.Max(x => x.LastFiredTime);
                    DateTime recentFiredTime1 = t.LinksFrom.Max(x => x.From.LastFiredTime);
                    if (recentFiredTime1 > recentFiredTime)
                        recentFiredTime = recentFiredTime1;
                    if (recentFiredTime > bestFiredTime )
                    {
                        bestMeaning = t;
                        bestFiredTime = recentFiredTime;
                    }
                }
                if (bestMeaning is not null)
                    meaning.RemoveAll(x => x != bestMeaning);
            }
            if (meaning.Count > 0)
                attributes.AddRange(theUKS.GetAttributes(meaning[0]));
        }
        if (meanings.Count == 3)
        {
            for (int i = 0; i < 3; i++)
                if (meanings[i][0] == theUKS.Labeled("null")) meanings[i][0] = null;
            l.From = meanings[0][0];
            l.LinkType = meanings[1][0];
            l.To = meanings[2][0];
        }
        return l;
    }
}