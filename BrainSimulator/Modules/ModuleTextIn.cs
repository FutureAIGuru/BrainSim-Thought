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
using System.Linq;
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
        theUKS.GetOrAddThought("Language", "Thought");

        theUKS.GetOrAddThought("Phrase", "language");
        theUKS.GetOrAddThought("EnglishWord", "language");
        theUKS.GetOrAddThought("SpanishWord", "language");
        theUKS.GetOrAddThought("means", "LinkType");
        theUKS.GetOrAddThought("contains", "LinkType");
        theUKS.GetOrAddThought("w:no", "EnglishWord").AddLink("means", theUKS.Labeled("no"));
        theUKS.GetOrAddThought("w:not", "EnglishWord").AddLink("means", theUKS.Labeled("not"));
        theUKS.GetOrAddThought("w:can", "EnglishWord").AddLink("means", theUKS.Labeled("can"));

        Thought t = theUKS.GetOrAddThought("p:is|a", "phrase");
        theUKS.AddSequenceAndLink(t, "contains", new List<Thought> { theUKS.GetOrAddThought("w:is", "EnglishWord"), theUKS.GetOrAddThought("w:a", "EnglishWord") });
        t.AddLink("means", "is-a");
        theUKS.GetOrAddThought("w:has", "LinkType").AddLink("means", "has");

        //spanish experiment
        Thought t1 = theUKS.GetOrAddThought("p:es|un", "phrase");
        theUKS.AddSequenceAndLink(t1, "contains", new List<Thought> { theUKS.GetOrAddThought("w:es", "SpanishWord"), theUKS.GetOrAddThought("w:un", "SpanishWord") });
        t1.AddLink("means", "is-a");

        if (dlg is ModuleTextInDlg ti1)
            ti1.AddParsedOutput(null);
    }

    public void SubmitText(string text)
    {
        string trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return;
        bool isStatement = true;

        if (trimmed.EndsWith("?"))
        {
            trimmed = trimmed[..^1];
            isStatement = false;
        }
        //find the key words in the text 
        var keywords = FindKeywords(trimmed);
        var language = FindWorkingLanguage(keywords);
        FindPhrases(keywords);

        Link l = BuildLink(keywords);
        if (l.From is null) return;

        if (isStatement)
        {
            Link r = theUKS.AddStatement(l.From, l.LinkType, l.To);
            if (r.LinkType.Label == "is-a" && r.Weight == 1) r.Weight = 0.5f;
            else if (r.Weight == 1) r.Weight = .05f; //initialize a low weight for new info
            if (dlg is ModuleTextInDlg ti1) ti1.AddParsedOutput(l);
        }
        else
        {
            if (dlg is ModuleTextInDlg ti)
            {
                //special case for is-a
                if (l.LinkType.Label == "is-a")
                {
                    if (l.From.HasAncestor(l.To))
                        ti.Answer("is true.");
                    else
                        ti.Answer("is false.");
                }
                else
                {

                    var attributes = theUKS.GetAllLinks(new List<Thought> { l.From });
                    if (attributes.Where(x => x.LinkType == l.LinkType && x.To == l.To).ToList().Count > 0)
                        ti.Answer("is true.");
                    else
                        ti.Answer("is false.");
                }
            }
        }
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
            meanings.Add(t.LinksTo.Where(x => x.LinkType.Label == "means").Select(x => x.To).ToList());
        }
        for (int i = 0; i < meanings.Count; i++)
        {
            List<Thought> meaning = meanings[i];
            if (meaning.Count == 0)
            {
                Thought newMeaning = null;
                //hack to find an existing meaning with a different word
                //if (i == 2)
                //{
                //    Thought from = meanings[0][0];
                //    Thought linkType = meanings[1][0];
                //    Thought to = from.LinksTo.FindFirst(x => x.LinkType == linkType)?.To;
                //    if (to?.Label != "Unknown")
                //        newMeaning = to;
                //}
                if (newMeaning is null)
                    newMeaning = theUKS.GetOrAddThought(keyWords[i].Label[2..]);
                meaning.Add(newMeaning);
                keyWords[i].AddLink("means", newMeaning);
            }
        }
        if (meanings.Count == 3)
        {
            l.From = meanings[0][0];
            l.LinkType = meanings[1][0];
            l.To = meanings[2][0];
        }
        return l;
    }

    private void FindPhrases(List<Thought> keywords)
    {
        if (keywords.Count < 3) return;
        // For simplicity, let's assume a phrase is just a combination of keywords
        // In a real implementation, you would have more complex logic to determine phrases
        for (int i = 0; i < keywords.Count - 1; i++)
        {
            var keyword1 = keywords[i];
            var keyword2 = keywords[i + 1];
            var result = theUKS.HasSequence2(new List<Thought> { keyword1, keyword2 }, "contains", true, true);
            if (result.Count > 0)
            {
                keywords[i] = result[0].result;
                keywords.RemoveAt(i + 1);
            }
        }
        //hack to handle numerics for has 4 legs and negatives
        //assuming that word 1 is the link type
        string label1 = keywords[1].Label[2..];
        string label2 = keywords[2].Label[2..];
        bool numeric = false;
        if (int.TryParse(label2, out int val)) numeric = true;
        if (numeric || label2 == "not" || label2 == "no" || label1 == "can")
        {
            //make sure the individual words have meanings
            if (numeric) keywords[2].AddLink("means", theUKS.Labeled(val.ToString()));
            
            Thought tLinkType = theUKS.CreateThoughtFromMultipleAttributes(label1 + " " + label2, true);
            Thought newPhrase = theUKS.GetOrAddThought("p:" + tLinkType.Label.Replace(".", "|"), "Phrase");
            newPhrase.AddLink("means", tLinkType);
            keywords[1] = newPhrase;
            keywords.RemoveAt(2);
        }

        List<string> wordsToIgnore = new() { "a", "an", "the", "el", "la", "los", "las", "un", "una" };
        for (int i = 0; i < keywords.Count; i++)
        {
            Thought word = keywords[i];
            string labelx = word.Label[2..];
            if (wordsToIgnore.Contains(labelx))
                keywords.Remove(word);
        }
    }

    private List<Thought> FindKeywords(string trimmed)
    {
        var retVal = new List<Thought>();
        var words = trimmed.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 3) return retVal;

        var language = "EnglishWord";
        if (words.Contains("es")) language = "SpanishWOrd";

        IPluralize pluralizer = new Pluralizer();

        for (int i = 0; i < words.Length; i++)
        {
            string word = words[i];
            if (char.IsLower(words[i][0]) && language == "EnglishWord")
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
}