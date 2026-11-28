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
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using UKS;
using UKS.Helpers;

namespace BrainSimulator.Modules;

public partial class ModuleUKSStatementDlg : ModuleBaseDlg
{
    // Constructor of the ModuleUKSStatement dialog
    public ModuleUKSStatementDlg()
    {
        InitializeComponent();
    }

    // Draw gets called to draw the dialog when it needs refreshing
    public override bool Draw(bool checkDrawTimer)
    {
        if (!base.Draw(checkDrawTimer)) return false;

        return true;
    }


    //these get the data back from the combobox selection 
    Thought tSource = null;
    Thought tTarget = null;

    // BtnAddLink_Click is called when the AddLink button is clicked or ENTER is pressed in one of the textboxes.
    // Whatever it changes is one undoable step.
    private void BtnAddLink_Click(object sender, RoutedEventArgs e)
    {
        ModuleUKSStatement UKSStatement = (ModuleUKSStatement)ParentModule;
        UKSStatement.theUKS.ChangeUndoably($"[{sourceText.Text}->{linkText.Text}->{targetText.Text}]", AddLink);
    }

    private void AddLink()
    {
        ModuleUKSStatement UKSStatement = (ModuleUKSStatement)ParentModule;
        string fromString = sourceText.Text;
        string toString = targetText.Text;
        string linkTypeString = linkText.Text;

        //Special case for [This,is-a,dog]
        if (fromString.ToLower() == "this")
        {
            if (linkTypeString.ToLower().Contains("called"))
            {
                Thought mostRecent = UKSStatement.theUKS.Labeled("mostRecent");
                if (mostRecent is null)
                {
                    SetStatus("'This' is not defined at this time");
                    return;
                }
                Thought mostRecentTarget = mostRecent.LinksTo.FindFirst(x => x.LinkType.Label == "is").To;
                mostRecentTarget.Label = toString;
            }
            return;
        }

        if (!CheckAddLinkFieldsFilled()) return;

        const long cps = UKS.UKS.CyclesPerSecond;
        long timeToLive = Thought.Forever;
        string timeToLiveText = ((ComboBoxItem)timeToLiveCombo.SelectedItem).Content.ToString();
        switch (timeToLiveText)
        {
            case "Eternal": timeToLive = Thought.Forever; break;
            case "1 hr": timeToLive = 3600 * cps; break;
            case "5 min": timeToLive = 300 * cps; break;
            case "1 min": timeToLive = 60 * cps; break;
            case "30 sec": timeToLive = 30 * cps; break;
            case "10 sec": timeToLive = 10 * cps; break;
        }
        float confidence = (float)confidenceSlider.Value;

        //hack for "dogs are animals"
        IPluralize pluralizer = new Pluralizer();
        if (pluralizer.IsPlural(fromString) && pluralizer.IsPlural(toString) && linkTypeString == "are")
            linkTypeString = "is-a";

        // Initialize the dialog's conventional capabilities in the active store. Loading
        // a network does not necessarily run CreateInitialStructure before entering a rule.
        Thought possibleMarker = UKSStatement.theUKS.GetOrAddThought("?", "Property");
        possibleMarker.AddLink("hasProperty", UKSStatement.theUKS.GetOrAddThought("isPossible", "Property"));
        UKSStatement.theUKS.GetOrAddThought("IF", "ClauseType").AddLink("hasProperty",
            UKSStatement.theUKS.GetOrAddThought("isConditional", "Property"));
        UKSStatement.theUKS.GetOrAddThought("AND", "ClauseType").AddLink("hasProperty",
            UKSStatement.theUKS.GetOrAddThought("isConjunction", "Property"));
        Thought tLink = UKSStatement.theUKS.CreateThoughtFromMultipleAttributes(linkTypeString, true, ModuleUKSStatement.Singularize);
        bool conditional = tLink?.HasProperty("isConditional") == true || tLink?.HasProperty("isConjunction") == true;
        if (conditional)
        {
            tSource = UKSStatement.theUKS.ProcessSingleLineWithNesting(fromString, true);
            tTarget = UKSStatement.theUKS.ProcessSingleLineWithNesting(toString, true);
            if (tSource is not Link || tTarget is not Link)
            {
                SetStatus("IF requires a Relationship or condition expression in both From and To");
                tSource = null;
                tTarget = null;
                return;
            }
        }

        //handle source though which is itself a link
        string sourceInput = fromString.Trim().Trim('[', ']').Replace("→", "->");
        var sourceParts = sourceInput.Contains("->")
            ? sourceInput.Split("->", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : UKSStatement.Singular(sourceInput.Split(" ", StringSplitOptions.RemoveEmptyEntries));
        if (!sourceInput.Contains("->") && sourceParts.Length > 3)
            sourceParts = new[] { sourceParts[0], string.Join(" ", sourceParts.Skip(1).Take(sourceParts.Length - 2)), sourceParts[^1] };
        if (tSource is null && sourceParts.Length == 3 && !sourceParts[0].StartsWith("^"))
        {
            sourceParts[1] = ModuleUKSStatement.ConditionalLinkType(linkTypeString, sourceParts[1]);
            Link r2 = new(UKSStatement.theUKS)
            {
                From = UKSStatement.theUKS.CreateThoughtFromMultipleAttributes(sourceParts[0], false, ModuleUKSStatement.Singularize),
                LinkType = UKSStatement.theUKS.CreateThoughtFromMultipleAttributes(sourceParts[1], true, ModuleUKSStatement.Singularize),
                To = UKSStatement.theUKS.CreateThoughtFromMultipleAttributes(sourceParts[2], false, ModuleUKSStatement.Singularize)
            };
            List<Link> existing = UKSStatement.theUKS.GetLink(r2.From, r2.LinkType, r2.To) is Link found ? [found] : [];
            if (conditional)
                tSource = r2;
            else if (existing.Count == 0)
                tSource = UKSStatement.theUKS.AddStatement(r2.From, r2.LinkType, r2.To);
            else
            {
                // multiple matches, create a dropdown in the UI to select which one?
                sourceCombo.Visibility = Visibility.Visible;
                sourceCombo.Items.Clear();
                ComboBoxItem cbi = new ComboBoxItem { Content = "<New>", ToolTip = "Create a new Link" };
                cbi.PreviewMouseLeftButtonUp += SourceComboItem_Clicked;
                sourceCombo.Items.Add(cbi);
                sourceCombo.SelectedIndex = 0;
                //sourceCombo.IsDropDownOpen = true;
                Dispatcher.BeginInvoke(DispatcherPriority.Input, () => sourceCombo.IsDropDownOpen = true);
                foreach (var t in existing)
                {
                    string toolTipText = "";
                    foreach (var r in t.LinksTo.Where(x => x.LinkType.Label != "is-a"))
                        toolTipText += r.ToString() + "\n";
                    if (!string.IsNullOrEmpty(toolTipText))
                        toolTipText = toolTipText[..^1];

                    cbi = new()
                    {
                        Content = t,
                        ToolTip = toolTipText,
                    };
                    cbi.PreviewMouseLeftButtonUp += SourceComboItem_Clicked;
                    sourceCombo.Items.Add(cbi);
                }
                return;
            }
        }

        if (tSource is null)
        {
            tSource = UKSStatement.theUKS.CreateThoughtFromMultipleAttributes(fromString, false, ModuleUKSStatement.Singularize);
        }

        //handle target though which is itself a link
        string targetInput = toString.Trim().Trim('[', ']').Replace("→", "->");
        var targetParts = targetInput.Contains("->")
            ? targetInput.Split("->", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : UKSStatement.Singular(targetInput.Split(" ", StringSplitOptions.RemoveEmptyEntries));
        if (!targetInput.Contains("->") && targetParts.Length > 3)
            targetParts = new[] { targetParts[0], string.Join(" ", targetParts.Skip(1).Take(targetParts.Length - 2)), targetParts[^1] };
        if (tTarget is null && targetParts.Length == 3 && !targetParts[0].StartsWith("^"))
        {
            targetParts[1] = ModuleUKSStatement.ConditionalLinkType(linkTypeString, targetParts[1]);
            Link r2 = new(UKSStatement.theUKS)
            {
                From = UKSStatement.theUKS.CreateThoughtFromMultipleAttributes(targetParts[0], false, ModuleUKSStatement.Singularize),
                LinkType = UKSStatement.theUKS.CreateThoughtFromMultipleAttributes(targetParts[1], true, ModuleUKSStatement.Singularize),
                To = UKSStatement.theUKS.CreateThoughtFromMultipleAttributes(targetParts[2], false, ModuleUKSStatement.Singularize)
            };
            List<Link> existing = UKSStatement.theUKS.GetLink(r2.From, r2.LinkType, r2.To) is Link found ? [found] : [];
            if (conditional)
                tTarget = r2;
            else if (existing.Count == 0)
                tTarget = UKSStatement.theUKS.AddStatement(r2.From, r2.LinkType, r2.To);
            else
            {
                // multiple matches, create a dropdown in the UI to select which one?
                targetCombo.Visibility = Visibility.Visible;
                targetCombo.Items.Clear();
                ComboBoxItem cbi = new ComboBoxItem { Content = "<New>", ToolTip = "Create a new Link" };
                cbi.PreviewMouseLeftButtonUp += TargetComboItem_Clicked;
                targetCombo.Items.Add(cbi);
                targetCombo.SelectedIndex = 0;
                //targetCombo.IsDropDownOpen = true;
                Dispatcher.BeginInvoke(DispatcherPriority.Input, () => targetCombo.IsDropDownOpen = true);
                foreach (var t in existing)
                {
                    string toolTipText = "";
                    foreach (var r in t.LinksTo.Where(x => x.LinkType.Label != "is-a"))
                        toolTipText += r.ToString() + "\n";
                    if (!string.IsNullOrEmpty(toolTipText))
                        toolTipText = toolTipText[..^1];

                    cbi = new()
                    {
                        Content = t,
                        ToolTip = toolTipText,
                    };
                    cbi.PreviewMouseLeftButtonUp += TargetComboItem_Clicked;
                    targetCombo.Items.Add(cbi);
                }
                return;
            }
        }

        if (tTarget is null)
        {
            tTarget = UKSStatement.theUKS.CreateThoughtFromMultipleAttributes(toString, false, ModuleUKSStatement.Singularize);
        }

        bool isNewStatement = UKSStatement.theUKS.GetLink(tSource, tLink, tTarget) is null;
        // Thoughts this statement created share its time to live (AddLink runs as one transaction).
        UKS.UKS store = UKSStatement.theUKS;
        bool sourceIsNew = store.CreatedInCurrentChange(tSource);
        bool linkTypeIsNew = store.CreatedInCurrentChange(tLink);
        bool targetIsNew = tTarget is not null && store.CreatedInCurrentChange(tTarget);
        var r1 = UKSStatement.theUKS.AddStatement(tSource, tLink, tTarget);
        //Link r1 = UKSStatement.AddTheLink(tSource, linkTypeString, toString);

        //set the timeToLive
        if (r1 is not null && setConfCB.IsChecked == true)
        {
            if (isNewStatement)
            {
                r1.Weight = confidence;
                r1.TimeToLive = timeToLive;
            }
            if (sourceIsNew) r1.From.TimeToLive = timeToLive;
            if (linkTypeIsNew) r1.LinkType.TimeToLive = timeToLive;
            if (targetIsNew && r1.To is not null) r1.To.TimeToLive = timeToLive;
        }
        if (r1 is not null && eventCB.IsChecked == true)
        {
            Thought subject = r1.From;
            UKSStatement.theUKS.GetOrAddThought("events", "LinkType");
            Thought previousEvents = subject.LinksTo.FindFirst(x => x.LinkType.Label == "events");
            Thought theSequence = subject.LinksTo.FindFirst(x => x.LinkType.Label == "events")?.To;
            if (theSequence is null)
            {
                Thought t1 = UKSStatement.theUKS.CreateFirstElement(subject.Label, r1);
                subject.RemoveLinks("events");
                subject.AddLink("events", t1);
            }
            else
            {
                Thought t1 = UKSStatement.theUKS.InsertAfter((SeqElement)theSequence, r1, atBeginning: true);
            }
        }

        SetTextbosBackground(targetText);
        SetTextbosBackground(sourceText);
        SetTextbosBackground(linkText);

        tSource = null;
        tTarget = null;
    }

    private void SourceComboItem_Clicked(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not ComboBoxItem cbi) return;
        ModuleUKSStatement UKSStatement = (ModuleUKSStatement)ParentModule;
        UKSStatement.theUKS.ChangeUndoably(cbi.Content.ToString(), () => SourceComboItem_Choose(cbi));
    }

    private void SourceComboItem_Choose(ComboBoxItem cbi)
    {
        ModuleUKSStatement UKSStatement = (ModuleUKSStatement)ParentModule;
        sourceCombo.Visibility = Visibility.Hidden;
        if (cbi.Content.ToString() == "<New>")
        {
            string input = sourceText.Text.Trim().Trim('[', ']').Replace("→", "->");
            var sourceParts = input.Contains("->")
                ? input.Split("->", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : UKSStatement.Singular(input.Split(" ", StringSplitOptions.RemoveEmptyEntries));
            if (!input.Contains("->") && sourceParts.Length > 3)
                sourceParts = new[] { sourceParts[0], string.Join(" ", sourceParts.Skip(1).Take(sourceParts.Length - 2)), sourceParts[^1] };
            if (sourceParts.Length == 3)
            {
                tSource = UKSStatement.theUKS.AddStatement(
                    UKSStatement.theUKS.CreateThoughtFromMultipleAttributes(sourceParts[0], false, ModuleUKSStatement.Singularize),
                    UKSStatement.theUKS.CreateThoughtFromMultipleAttributes(sourceParts[1], true, ModuleUKSStatement.Singularize),
                    UKSStatement.theUKS.CreateThoughtFromMultipleAttributes(sourceParts[2], false, ModuleUKSStatement.Singularize));
                sourceText.Text = tSource.ToString();
            }
        }
        else
        {
            sourceText.Text = cbi.Content.ToString();
            tSource = (Thought)cbi.Content;
        }
    }
    private void TargetComboItem_Clicked(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not ComboBoxItem cbi) return;
        ModuleUKSStatement UKSStatement = (ModuleUKSStatement)ParentModule;
        UKSStatement.theUKS.ChangeUndoably(cbi.Content.ToString(), () => TargetComboItem_Choose(cbi));
    }

    private void TargetComboItem_Choose(ComboBoxItem cbi)
    {
        ModuleUKSStatement UKSStatement = (ModuleUKSStatement)ParentModule;
        targetCombo.Visibility = Visibility.Hidden;
        if (cbi.Content.ToString() == "<New>")
        {
            string input = targetText.Text.Trim().Trim('[', ']').Replace("→", "->");
            var targetParts = input.Contains("->")
                ? input.Split("->", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : UKSStatement.Singular(input.Split(" ", StringSplitOptions.RemoveEmptyEntries));
            if (!input.Contains("->") && targetParts.Length > 3)
                targetParts = new[] { targetParts[0], string.Join(" ", targetParts.Skip(1).Take(targetParts.Length - 2)), targetParts[^1] };
            if (targetParts.Length == 3)
            {
                tTarget = UKSStatement.theUKS.AddStatement(
                    UKSStatement.theUKS.CreateThoughtFromMultipleAttributes(targetParts[0], false, ModuleUKSStatement.Singularize),
                    UKSStatement.theUKS.CreateThoughtFromMultipleAttributes(targetParts[1], true, ModuleUKSStatement.Singularize),
                    UKSStatement.theUKS.CreateThoughtFromMultipleAttributes(targetParts[2], false, ModuleUKSStatement.Singularize));
                targetText.Text = tTarget.ToString();
            }
        }
        else
        {
            targetText.Text = cbi.Content.ToString();
            tTarget = (Thought)cbi.Content;
        }
    }

    // Undoes the most recent change to the UKS, made here or in another dialog.
    private void BtnUndo_Click(object sender, RoutedEventArgs e)
    {
        ModuleUKSStatement UKSStatement = (ModuleUKSStatement)ParentModule;
        SetStatus(UKSStatement.theUKS.UndoLast() is string undone ? $"Undone: {undone}" : "Nothing to undo.");
    }

    // Check for thought existence and set background color of the textbox and the error message accordingly.
    private bool SetTextbosBackground(object sender)
    {
        if (sender is TextBox tb)
        {
            string text = tb.Text.Trim();

            if (text == "" && !tb.Name.Contains("arget"))
            {
                tb.Background = new SolidColorBrush(Colors.Pink);
                SetStatus("Source and type cannot be empty");
                return false;
            }
            List<Thought> tl = ModuleUKSStatement.ThoughtListFromString(MainWindow.theUKS, text);
            if (tl is null || tl.Count == 0)
            {
                tb.Background = new SolidColorBrush(Colors.LemonChiffon);
                SetStatus("OK");
                return false;
            }
            tb.Background = new SolidColorBrush(Colors.White);
            SetStatus("OK");
            return true;
        }
        return false;
    }


    // TheGrid_SizeChanged is called when the dialog is sized
    private void TheGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        Draw(false);
    }

    // thoughtText_TextChanged is called when the thought textbox changes
    private void Text_TextChanged(object sender, TextChangedEventArgs e)
    {
        SetTextbosBackground(sender);
    }

    // Check for parent existence and set background color of the textbox and the error message accordingly.
    private bool CheckAddLinkFieldsFilled()
    {
        SetStatus("OK");
        ModuleUKSStatement UKSStatement = (ModuleUKSStatement)ParentModule;

        if (sourceText.Text == "")
        {
            SetStatus("Source not provided");
            return false;
        }
        if (linkText.Text == "")
        {
            SetStatus("Type not provided");
            return false;
        }
        return true;
    }
}
