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
using System.Collections.Generic; // added
using System.Linq;                 // added
using System.Windows;
using System.Windows.Controls; // added
using System.Windows.Input;
using System.Windows.Media;    // added
using UKS;

namespace BrainSimulator.Modules;

public partial class ModuleTextInDlg : ModuleBaseDlg
{
    private readonly Dictionary<string, string> _parsedInputHistory = new();

    public ModuleTextInDlg()
    {
        InitializeComponent();
    }

    public override bool Draw(bool checkDrawTimer)
    {
        if (!base.Draw(checkDrawTimer)) return false;

        UpdateAttributesOutput();
        return true;
    }

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        e.Handled = true;
        SubmitCurrentText();
    }

    private void SubmitCurrentText()
    {
        string text = InputBox.Text;
        //InputBox.Clear();
        InputBox.Focus();

        if (ParentModule is ModuleTextIn module)
        {
            module.SubmitText(text);
        }
    }

    public void Answer(string answer)
    {
        string text = InputBox.Text;
        if (text.EndsWith("?")) text = text[..^1];
        text += " " + answer;
        InputBox.Text = text;
    }
    public void AddParsedOutput(Link l)
    {
        if (l == null)
        {
            _parsedInputHistory.Clear();
            ParsedInputBox.Items.Clear(); // add to top
            return;
        }

        string entry = l.ToString();
        if (string.IsNullOrWhiteSpace(entry)) return;

        // remember the original input for this parsed entry
        _parsedInputHistory[entry] = InputBox.Text;

        if (ParsedInputBox.Items.Count == 0 || ParsedInputBox.Items[0].ToString() != entry)
            ParsedInputBox.Items.Insert(0, entry); // add to top
        ParsedInputBox.SelectedIndex = 0;
        int AttributesOutputMax = 6; // limit entries
        while (ParsedInputBox.Items.Count >= AttributesOutputMax)
        {
            var removed = ParsedInputBox.Items[^1]?.ToString();
            ParsedInputBox.Items.RemoveAt(ParsedInputBox.Items.Count - 1);
            if (!string.IsNullOrEmpty(removed))
                _parsedInputHistory.Remove(removed);
        }
    }

    private void ParsedInputBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ParsedInputBox.SelectedItem is null) return;

        string key = ParsedInputBox.SelectedItem switch
        {
            ListBoxItem lbi => lbi.Content?.ToString(),
            _ => ParsedInputBox.SelectedItem.ToString()
        };

        if (string.IsNullOrWhiteSpace(key)) return;

        if (_parsedInputHistory.TryGetValue(key, out var original))
        {
            InputBox.Text = original;
            InputBox.Focus();
        }
    }

    private void TheGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        Draw(false);
    }

    Thought responseLanguage = null;
    private readonly Random _random = new();

    public void SetResponseLanguage(Thought language)
    {
        responseLanguage = language;
    }
    private void UpdateAttributesOutput()
    {
        AttributesOutputBox.Items.Clear();
        if (ParentModule?.theUKS is null) return;
        if (responseLanguage is null) responseLanguage = ParentModule.theUKS.Labeled("EnglishWord");

        string raw = AttributesOfBoxInputBox.Text;
        Thought t = ParentModule.theUKS.Labeled(raw);
        if (t is null) return;

        IPluralize pluralizer = new Pluralizer();

        var attributes = ParentModule.theUKS.GetAllLinks(new List<Thought> { t });
        foreach (Link l in attributes)
        {
            string theType = l.LinkType.Label;
            bool isPlural = false;
            int index = theType.IndexOf(".");
            if (index > -1)
            {
                index++;
                int.TryParse(theType[index..], out int val);
                if (val > 1)
                    isPlural = true;
                theType = theType.Replace(".", " ");
            }

            //            string theTo = l.To.LinksFrom.FindFirst(x=>x.LinkType.Label == "means" && x.From.HasAncestor(responseLanguage))?.From.Label;

            var candidates = l.To.LinksFrom
                .Where(x => x.LinkType.Label == "means" && x.From.HasAncestor(responseLanguage))
                .ToList();
            if (candidates.Count == 0) continue;

            string theTo = candidates[_random.Next(candidates.Count)].From.Label;
            if (theTo is null) continue;
            theTo = theTo[2..];

            if (isPlural)
                theTo = pluralizer.Pluralize(theTo);
            string display = $"{theType} {theTo}";

            float conf = float.IsNaN(l.Weight) ? 0f : Math.Clamp(l.Weight, 0f, 1f);
            Color bar = Color.FromArgb(80, 0, 240, 0); // semi-transparent green
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0.5),
                EndPoint = new Point(1, 0.5),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(bar, 0),
                    new GradientStop(bar, conf),
                    new GradientStop(Color.FromArgb(0, bar.R, bar.G, bar.B), conf),
                    new GradientStop(Color.FromArgb(0, bar.R, bar.G, bar.B), 1)
                }
            };

            var item = new ListBoxItem
            {
                Content = display,
                Background = brush,
                Padding = new Thickness(4, 2, 4, 2)
            };
            AttributesOutputBox.Items.Add(item);
        }
    }
}