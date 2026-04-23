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
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UKS;

namespace BrainSimulator.Modules;

public partial class ModuleSequenceEditorDlg : ModuleBaseDlg
{
    public ModuleSequenceEditorDlg()
    {
        InitializeComponent();
        sequenceNameInput.KeyDown += SequenceNameInput_KeyDown;
        saveButton.Click += SaveButton_Click;
        sequenceContentInput.TextChanged += SequenceContentInput_TextChanged;
        sequenceContentInput.Loaded += SequenceContentInput_Loaded;
    }

    public override bool Draw(bool checkDrawTimer)
    {
        if (!base.Draw(checkDrawTimer)) return false;
        //this has a timer so that no matter how often you might call draw, the dialog
        //only updates 10x per second
        ModuleSequenceEditor parent = (ModuleSequenceEditor)base.ParentModule;
        return true;
    }

    private void TheGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        Draw(false);
    }

    private void SequenceNameInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            LoadSequence();
            e.Handled = true;
        }
    }

    private void SequenceContentInput_Loaded(object sender, RoutedEventArgs e)
    {
        // Synchronize scrolling between line numbers and content
        if (sequenceContentInput.Template?.FindName("PART_ContentHost", sequenceContentInput) is ScrollViewer contentScroll)
        {
            contentScroll.ScrollChanged += ContentScroll_ScrollChanged;
        }
    }

    private void ContentScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // Sync line numbers scrolling with content scrolling
        if (lineNumbersTextBox.Template?.FindName("PART_ContentHost", lineNumbersTextBox) is ScrollViewer lineScroll)
        {
            lineScroll.ScrollToVerticalOffset(e.VerticalOffset);
        }
    }

    private void SequenceContentInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateLineNumbers();
    }

    private void UpdateLineNumbers()
    {
        string content = sequenceContentInput.Text;
        string[] lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        
        StringBuilder lineNumbers = new StringBuilder();
        for (int i = 0; i < lines.Length; i++)
        {
            lineNumbers.AppendLine(i.ToString());
        }
        
        lineNumbersTextBox.Text = lineNumbers.ToString();
    }

    private void LoadSequence()
    {
        ModuleSequenceEditor parent = (ModuleSequenceEditor)base.ParentModule;
        if (parent?.theUKS == null) return;

        string sequenceName = sequenceNameInput.Text?.Trim();
        if (string.IsNullOrEmpty(sequenceName))
        {
            SetStatus("No sequence name specified");
            return;
        }

        // Try to find the sequence
        Thought sequenceThought = parent.theUKS.Labeled(sequenceName + "-seq0");
        if (sequenceThought == null)
        {
            SetStatus("Sequence not found");
            sequenceContentInput.Text = "";
            return;
        }

        // Check if it's actually a sequence element
        if (sequenceThought is not SeqElement firstElement)
        {
            SetStatus("Thought is not a sequence");
            sequenceContentInput.Text = "";
            return;
        }

        // Flatten the sequence and display it (NO line numbers in content)
        List<Thought> elements = parent.theUKS.FlattenSequence(firstElement);
            StringBuilder sb = new StringBuilder();

        for (int i = 0; i < elements.Count; i++)
        {
            sb.AppendLine(elements[i]?.ToString() ?? "");
        }

        sequenceContentInput.Text = sb.ToString();
        SetStatus($"Loaded sequence with {elements.Count} elements");
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        ModuleSequenceEditor parent = (ModuleSequenceEditor)base.ParentModule;
        if (parent?.theUKS == null) return;

        string sequenceName = sequenceNameInput.Text?.Trim();
        if (string.IsNullOrEmpty(sequenceName))
        {
            SetStatus("No sequence name specified");
            return;
        }

        string content = sequenceContentInput.Text?.Trim();
        if (string.IsNullOrEmpty(content))
        {
            SetStatus("No sequence content to save");
            return;
        }

        // Parse the content - each line is a statement (no line numbers to parse!)
        string[] lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        List<Thought> newElements = new List<Thought>();

        foreach (string line in lines)
        {
            string trimmedLine = line.Trim();
            if (string.IsNullOrEmpty(trimmedLine)) continue;

            // Parse the statement
            string statement = trimmedLine.Replace("→", "->");
            Link element = (Link)parent.theUKS.ProcessSingleLine(statement);
            if (element is not null)
            {
                newElements.Add(element);
            }
            else
            {
                Thought t = parent.theUKS.Labeled(statement);
                if (t is null)
                {
                    SetStatus($"Could not parse: {statement}");
                    return;
                }
                newElements.Add(t);
            }
        }

        if (newElements.Count == 0)
        {
            SetStatus("No valid elements found");
            return;
        }

        Thought rootElement = parent.theUKS.Labeled(sequenceName);
        Link linkToChange = null;
        foreach (Link l in rootElement.LinksTo)
            if (l.To is SeqElement s)
            { linkToChange = l;break; }

        // Delete the old sequence if it exists
        Thought oldSequence = parent.theUKS.Labeled(sequenceName + "-seq0");
        if (oldSequence is SeqElement oldSeqElement)
        {
            parent.theUKS.DeleteSequence(oldSeqElement);
        }

        // Create the new sequence
        SeqElement newSequence = parent.theUKS.AddSequence(sequenceName, newElements);
        linkToChange.To = newSequence;

        if (newSequence != null)
        {
            newSequence.Label = sequenceName + "-seq0";
            SetStatus($"Saved sequence with {newElements.Count} elements");
        }
        else
        {
            SetStatus("Failed to create sequence");
        }
    }
}