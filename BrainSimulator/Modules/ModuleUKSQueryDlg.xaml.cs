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


using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Input;
using UKS;
using UKS.Helpers;

namespace BrainSimulator.Modules;

public partial class ModuleUKSQueryDlg : ModuleBaseDlg
{
    private static readonly TimeSpan NormalRequeryInterval = TimeSpan.FromSeconds(3);
    DispatcherTimer requeryTimer = new DispatcherTimer();

    public ModuleUKSQueryDlg()
    {
        InitializeComponent();
        requeryTimer.Interval = NormalRequeryInterval;
        requeryTimer.Tick += RequeryTimer_Tick;
        requeryTimer.Start();
    }

    private void RequeryTimer_Tick(object sender, EventArgs e)
    {
        requeryTimer.Interval = NormalRequeryInterval;
        if (CBAutoRefresh.IsChecked == false) return;
        QueryForAttributes();
        QueryByAttributes();
    }

    //List<Thought> result = new();


    // Draw gets called to draw the dialog when it needs refreshing
    public override bool Draw(bool checkDrawTimer)
    {
        if (!base.Draw(checkDrawTimer)) return false;
        return true;
    }

    private void BtnWhy_Click(object sender, RoutedEventArgs e)
    {
        DelayRequeryForExplanation();
        ModuleUKSQuery UKSQuery = (ModuleUKSQuery)ParentModule;
        UKS.UKS theUKS = UKSQuery.theUKS;
        Thought? source = theUKS.Labeled(sourceText.Text);
        if (source is null)
        {
            resultText.Text = "Why? — source not found";
            return;
        }
        List<Link> links = theUKS.GetAttributes(source);
        List<Link> whyNot = theUKS.WhyNot()
            .Where(link => link is not null)
            .ToList();
        if (whyNot.Count > 0)
        {
            resultText.Text = "Why not?" + Environment.NewLine + FormatWhyNot(whyNot);
            return;
        }

        List<Link> why = theUKS.Why()
            .Where(link => link is not null)
            .ToList();
        if (why.Count > 0)
        {
            resultText.Text = "Why?" + Environment.NewLine + FormatWhy(why);
            return;
        }

        Link? match = null;
        if (!string.IsNullOrWhiteSpace(targetText.Text))
            match = links.FirstOrDefault(l => l.To?.Label == targetText.Text);
        if (match is null && !string.IsNullOrWhiteSpace(typeText.Text))
            match = links.FirstOrDefault(l =>
                string.Equals(l.LinkType?.Label, typeText.Text, StringComparison.OrdinalIgnoreCase));
        // An inherited attribute's evidence starts with the is-a chain, so it has more than one link.
        match ??= links.FirstOrDefault(l => theUKS.GetEvidence(l).Count > 1);

        if (match is null)
        {
            resultText.Text = "Why? — no explainable inherited link";
            return;
        }

        List<Link> evidence = theUKS.GetEvidence(match);
        resultText.Text = "Why? " + (evidence.Count == 0 ? "the query computed it"
            : string.Join(", ", evidence.Select(FormatLink)));
    }

    private static string FormatWhy(IEnumerable<Link> links) =>
        FormatConditionalExplanation(links, conditionWasVerified: true);

    private static string FormatWhyNot(IEnumerable<Link> links) =>
        FormatConditionalExplanation(links, conditionWasVerified: false);

    private static string FormatConditionalExplanation(IEnumerable<Link> links, bool conditionWasVerified)
    {
        List<Link> conditionLinks = links.Where(link => link is not null).ToList();
        Link ifLink = conditionLinks.FirstOrDefault(link =>
            string.Equals(link.LinkType?.Label, "IF", StringComparison.OrdinalIgnoreCase) &&
            link.From is Link && link.To is Link);

        if (ifLink?.From is not Link result || ifLink.To is not Link condition)
            return FormatLinks(conditionLinks);

        string inclusion = conditionWasVerified ? "included" : "not included";
        string verification = conditionWasVerified ? "verified" : "not verified";
        return $"{Capitalize(FormatClause(result))} {inclusion} because " +
            $"{FormatClause(condition)} was {verification}.";
    }

    private static string FormatLinks(IEnumerable<Link> links) =>
        string.Join(Environment.NewLine, links
            .Where(link => link is not null)
            .Select(FormatLink)
            .Distinct());

    private static string FormatLink(Link link) =>
        $"[{FormatThought(link.From)} → {link.LinkType?.Label ?? "?"} → {FormatThought(link.To)}]";

    private static string FormatThought(Thought thought)
    {
        if (thought is null) return "?";
        return thought is Link link ? FormatLink(link) : thought.Label;
    }

    private static string FormatClause(Link link)
    {
        if (string.Equals(link.LinkType?.Label, "AND", StringComparison.OrdinalIgnoreCase) &&
            link.From is Link left && link.To is Link right)
            return $"{FormatClause(left)} and {FormatClause(right)}";

        return $"{FormatThought(link.From)} {NaturalPredicate(link.LinkType)} {FormatThought(link.To)}";
    }

    private static string NaturalPredicate(Thought linkType)
    {
        string predicate = linkType?.Label ?? "?";
        if (linkType?.HasProperty("isPossible") == true)
            predicate = string.Join(" ", predicate.Split('.').Where(part => part != "?"));
        string normalized = predicate.ToLowerInvariant();
        if (normalized == "is-a") return "is a";
        if (normalized is "is" or "has" or "can" or "owns" or "goes" or "does" or "must" or "should" or "will")
            return predicate;
        if (predicate.EndsWith("s", StringComparison.OrdinalIgnoreCase)) return predicate;
        if (predicate.EndsWith("y", StringComparison.OrdinalIgnoreCase) && predicate.Length > 1 &&
            !"aeiou".Contains(char.ToLowerInvariant(predicate[^2])))
            return predicate[..^1] + "ies";
        if (predicate.EndsWith("ch", StringComparison.OrdinalIgnoreCase) ||
            predicate.EndsWith("sh", StringComparison.OrdinalIgnoreCase) ||
            predicate.EndsWith("x", StringComparison.OrdinalIgnoreCase) ||
            predicate.EndsWith("z", StringComparison.OrdinalIgnoreCase) ||
            predicate.EndsWith("o", StringComparison.OrdinalIgnoreCase))
            return predicate + "es";
        return predicate + "s";
    }

    private static string Capitalize(string text) =>
        string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private void DelayRequeryForExplanation()
    {
        if (!requeryTimer.IsEnabled) return;
        requeryTimer.Stop();
        requeryTimer.Interval = TimeSpan.FromSeconds(5);
        requeryTimer.Start();
    }

    private void BtnLinks_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b)
        {
            if (b.Content.ToString() == "Add")
            {
                string newText = typeText1.Text + "," + targetText1.Text;
                if (!queryText1.Text.Contains(newText))
                {
                    if (!string.IsNullOrEmpty(queryText1.Text))
                        queryText1.Text += "\n";
                    queryText1.Text += newText;
                }
                QueryByAttributes();
            }
            if (b.Content.ToString() == "New")
            {
                string newText = typeText1.Text + "," + targetText1.Text;
                queryText1.Text = newText;
                QueryByAttributes();
            }
            if (b.Content.ToString() == "Clear")
            {
                queryText1.Text = "";
                resultText1.Text = "";
            }
            if (b.Content.ToString() == "Query")
            {
                QueryForAttributes();
            }
        }
    }
    private void QueryForAttributes()
    {
        string source = sourceText.Text;
        string type = typeText.Text;
        string target = targetText.Text;
        string filter = filterText?.Text ?? "";

        List<Thought> thoughts;
        List<Link> links;
        ModuleUKSQuery UKSQuery = (ModuleUKSQuery)ParentModule;
        var results1 = UKSQuery.GetAttributes(source, type, target, filter, out thoughts, out links);

        if (results1 is not null)
        {
            OutputResults(results1, target == "", source == "");
        }
        else if (thoughts.Count > 0)
            OutputResults(thoughts);
        else
            OutputResults(links, target == "", source == "");
    }

    //query by attributes
    private void QueryByAttributes()
    {
        ModuleUKSQuery UKSQuery = (ModuleUKSQuery)ParentModule;
        var theUKS = UKSQuery.theUKS;

        Thought ancestor = theUKS.Labeled(ancestorText1.Text);
        if (ancestor is null)
            ancestor = theUKS.Labeled("Thought");

        //build the query object
        Thought queryThought = CreateTheQueryThought();
        if (queryThought is null)
        {
            SetStatus("Could not create query");
            return;
        }

        var allResults = theUKS.SearchByAttributes(queryThought, ancestor);

        if (allResults.Count == 0)
        {
            resultText1.Text = "<No Results>";
            queryThought.Delete();
            return;
        }

        resultText1.Text = "";
        foreach (var result1 in allResults)
        {
            resultText1.Text += result1.t.Label + "   " + result1.conf.ToString("0.00") + "\n";
        }

        if (allResults.Count > 0 &&
            (allResults.Count == 1 || allResults[0].conf > allResults[1].conf))
        {
            UpdateMostRecent(allResults[0].t);
        }

        queryThought.Delete();
    }

    private Thought CreateTheQueryThought()
    {
        ModuleUKSQuery UKSQuery = (ModuleUKSQuery)ParentModule;
        Thought queryThought = new Thought(UKSQuery.theUKS) { Label = "theQuery" };
        string[] rels = queryText1.Text.Split('\n');
        foreach (string s in rels)
        {
            string[] relParams = s.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (relParams.Length > 1)
            {
                Thought linkType = UKSQuery.theUKS.CreateThoughtFromMultipleAttributes(relParams[0], true, ModuleUKSStatement.Singularize);
                Thought relTarget = UKSQuery.theUKS.CreateThoughtFromMultipleAttributes(relParams[1], false, ModuleUKSStatement.Singularize);
                if (linkType is null)
                {
                    resultText1.Text = $"<{relParams[0]} not found>";
                    return null;
                }
                if (relTarget is null)
                {
                    resultText1.Text = $"<{relParams[1]} not found>";
                    return null;
                }

                //put target
                if (linkType.Label == "can")
                {
                    relTarget.AddParent("Action");
                    relTarget.RemoveParent("Unknown");
                }

                float conf = .9f;
                if (relParams.Length > 2) float.TryParse(relParams[2], out conf);
                Thought r1 = queryThought.AddLink(linkType, relTarget);
                r1.Weight = conf;
            }
        }

        return queryThought;
    }

    void UpdateMostRecent(Thought t)
    {
        ModuleUKSQuery UKSQuery = (ModuleUKSQuery)ParentModule;
        Thought mostRecent = UKSQuery.theUKS.GetOrAddThought("mostRecent", "LinkType");
        //delete any previous mostRecent links
        mostRecent.RemoveLinks("is");
        mostRecent.AddLink("is", t);
    }

    private void OutputResults<T>(IReadOnlyList<T> r, bool noSource = false, bool noTarget = false)
    {
        string resultString = "";
        if (r is null || r.Count == 0)
        {
            resultString = "No Results";
            resultText.Text = resultString;
            return;
        }
        foreach (var r1 in r)
        {
            if (r1 is ValueTuple<Link, float> tuple)
            {
                var r3 = tuple.Item1;
                var conf = tuple.Item2;
                if ((r3.To as Link)?.LinkType?.Label == "NXT")
                {
                    ModuleUKSQuery UKSQuery = (ModuleUKSQuery)ParentModule;
                    var theUKS = UKSQuery.theUKS;
                    var seq = theUKS.FlattenSequence((SeqElement)r3.To);
                    foreach (Thought t in seq) resultString += t.Label + " ";
                    resultString += $"{conf.ToString("0.00")}\n";
                }
                else
                    resultString += $"{r3.From.ToString()} {r3.LinkType.ToString()} {r3.To.ToString()}  ({conf.ToString("0.00")})\n";
            }
            else if (r1 is Link r2)
            {
                if (noSource && fullCB.IsChecked == false)
                    resultString += $"{r2.LinkType?.ToString()} {r2.To?.ToString()}  ({r2.Weight.ToString("0.00")})\n";
                else if (noTarget && fullCB.IsChecked == false)
                    resultString += $"{r2.From.ToString()} {r2.LinkType.ToString()}  ({r2.Weight.ToString("0.00")})\n";
                else
                {
                    Thought theSource = GetNonInstance(r2.From);
                    if (fullCB.IsChecked == true)
                        resultString += $"{theSource.Label} ";
                    resultString += $"{r2.LinkType.ToString()} {r2.To.ToString()}  ({r2.Weight.ToString("0.00")})\n";
                }
            }
            else
                resultString += r1.ToString() + "\n";
        }
        resultText.Text = resultString;
    }


    private Thought GetNonInstance(Thought source)
    {
        Thought theSource = source;
        while (theSource.HasProperty("isInstance")) theSource = theSource.Parents[0];
        return theSource;
    }

    private void Text_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            string newText = typeText1.Text + "," + targetText1.Text;
            queryText1.Text = newText;
            QueryByAttributes();
        }
    }
    // thoughtText_TextChanged is called when the thought textbox changes
    private void Text_TextChanged(object sender, TextChangedEventArgs e)
    {
        CheckThoughtExistence(sender);
    }
    //copied from UKSStatementDlg.cs
    private Thought CheckThoughtExistence(object sender)
    {
        if (sender is TextBox tb)
        {
            string text = tb.Text.Trim();

            if (text == "" && !tb.Name.Contains("arget") && tb.Name != "typeText")
            {
                tb.Background = new SolidColorBrush(Colors.Pink);
                SetStatus("Source and type cannot be empty");
                return null;
            }
            List<Thought> tl = ModuleUKSStatement.ThoughtListFromString(MainWindow.theUKS, text);
            if (tl is null || tl.Count == 0)
            {
                tb.Background = new SolidColorBrush(Colors.LemonChiffon);
                SetStatus("");
                return null;
            }
            tb.Background = new SolidColorBrush(Colors.White);
            SetStatus("");
            return tl[0];
        }
        return null;
    }

}
