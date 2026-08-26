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

    // BtnAddLink_Click is called when the AddLink button is clicked or ENTER is pressed in one of the textboxes
    private void BtnAddLink_Click(object sender, RoutedEventArgs e)
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

        TimeSpan timeToLive = TimeSpan.MaxValue;
        string timeToLiveText = ((ComboBoxItem)timeToLiveCombo.SelectedItem).Content.ToString();
        switch (timeToLiveText)
        {
            case "Eternal": timeToLive = TimeSpan.MaxValue; break;
            case "1 hr": timeToLive = TimeSpan.FromHours(1); break;
            case "5 min": timeToLive = TimeSpan.FromMinutes(5); break;
            case "1 min": timeToLive = TimeSpan.FromMinutes(1); break;
            case "30 sec": timeToLive = TimeSpan.FromSeconds(30); break;
            case "10 sec": timeToLive = TimeSpan.FromSeconds(10); break;
        }
        float confidence = (float)confidenceSlider.Value;

        //hack for "dogs are animals"
        IPluralize pluralizer = new Pluralizer();
        if (pluralizer.IsPlural(fromString) && pluralizer.IsPlural(toString) && linkTypeString == "are")
            linkTypeString = "is-a";


        //handle source though which is itself a link
        var sourceParts = UKSStatement.Singular(fromString.Split(" ", StringSplitOptions.RemoveEmptyEntries));
        if (sourceParts.Length == 3)
        {
            sourceParts[1] = ModuleUKSStatement.ConditionalLinkType(linkTypeString, sourceParts[1]);
            Link r2 = new()
            {
                From = UKSStatement.theUKS.GetOrAddThought(sourceParts[0]),
                LinkType = UKSStatement.theUKS.GetOrAddThought(sourceParts[1]),
                To = UKSStatement.theUKS.GetOrAddThought(sourceParts[2])
            };
            var existing = UKSStatement.theUKS.GetLinks(r2);
            if (existing.Count == 0)
                tSource = UKSStatement.theUKS.AddStatement(sourceParts[0], sourceParts[1], sourceParts[2]);
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
            tSource = UKSStatement.theUKS.CreateThoughtFromMultipleAttributes(fromString, false);
        }

        //handle target though which is itself a link
        var targetParts = UKSStatement.Singular(toString.Split(" ", StringSplitOptions.RemoveEmptyEntries));
        if (targetParts.Length == 3 && !targetParts[0].StartsWith("^"))
        {
            targetParts[1] = ModuleUKSStatement.ConditionalLinkType(linkTypeString, targetParts[1]);
            Link r2 = new()
            {
                From = UKSStatement.theUKS.GetOrAddThought(targetParts[0]),
                LinkType = UKSStatement.theUKS.GetOrAddThought(targetParts[1]),
                To = UKSStatement.theUKS.GetOrAddThought(targetParts[2])
            };
            var existing = UKSStatement.theUKS.GetLinks(r2);
            if (existing.Count == 0)
                tTarget = UKSStatement.theUKS.AddStatement(targetParts[0], targetParts[1], targetParts[2]);
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
            tTarget = UKSStatement.theUKS.CreateThoughtFromMultipleAttributes(toString, false);
        }

        Thought tLink = UKSStatement.theUKS.CreateThoughtFromMultipleAttributes(linkTypeString, true);

        bool isNewStatement = UKSStatement.theUKS.GetLink(tSource, tLink, tTarget) is null;
        bool sourceWasNeverFired = tSource.LastFiredTime == DateTime.MinValue;
        bool linkTypeWasNeverFired = tLink.LastFiredTime == DateTime.MinValue;
        bool targetWasNeverFired = tTarget?.LastFiredTime == DateTime.MinValue;
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
            if (sourceWasNeverFired) r1.From.TimeToLive = timeToLive;
            if (linkTypeWasNeverFired) r1.LinkType.TimeToLive = timeToLive;
            if (targetWasNeverFired && r1.To is not null) r1.To.TimeToLive = timeToLive;
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
                Thought t1 = UKSStatement.theUKS.InsertElement((SeqElement)theSequence, r1);
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
        sourceCombo.Visibility = Visibility.Hidden;
        if (cbi.Content.ToString() == "<New>")
        {
            var sourceParts = UKSStatement.Singular(sourceText.Text.Split(" ", StringSplitOptions.RemoveEmptyEntries));
            if (sourceParts.Length == 3)
            {
                sourceParts[1] = ModuleUKSStatement.ConditionalLinkType(linkText.Text, sourceParts[1]);
                tSource = UKSStatement.theUKS.AddStatement(sourceParts[0], sourceParts[1], sourceParts[2]);
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
        targetCombo.Visibility = Visibility.Hidden;
        if (cbi.Content.ToString() == "<New>")
        {
            var targetParts = UKSStatement.Singular(targetText.Text.Split(" ", StringSplitOptions.RemoveEmptyEntries));
            if (targetParts.Length == 3)
            {
                targetParts[1] = ModuleUKSStatement.ConditionalLinkType(linkText.Text, targetParts[1]);
                tTarget = UKSStatement.theUKS.AddStatement(targetParts[0], targetParts[1], targetParts[2]);
                targetText.Text = tTarget.ToString();
            }
        }
        else
        {
            targetText.Text = cbi.Content.ToString();
            tTarget = (Thought)cbi.Content;
        }
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
            List<Thought> tl = ModuleUKSStatement.ThoughtListFromString(text);
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
