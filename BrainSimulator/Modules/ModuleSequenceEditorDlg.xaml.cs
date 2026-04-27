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
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UKS;

namespace BrainSimulator.Modules;

public partial class ModuleSequenceEditorDlg : ModuleBaseDlg
{
    private bool isEditingSequence = true; // Track whether we're editing a sequence or context
    private string undoContent = ""; // Store content for undo

    public ModuleSequenceEditorDlg()
    {
        InitializeComponent();
        sequenceNameInput.KeyDown += SequenceNameInput_KeyDown;
        saveButton.Click += SaveButton_Click;
        replacePatternInput.KeyDown += ReplacePatternInput_KeyDown;
        sequenceContentInput.KeyDown += SequenceContentInput_KeyDown;
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

    private bool IsSequence(Thought thought)
    {
        // A sequence has a link to a SeqElement (e.g., "steps", "spelled", etc.)
        foreach (Link link in thought.LinksTo)
        {
            if (link.To is SeqElement)
                return true;
        }
        return false;
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

        // Try to find the thought
        Thought thought = parent.theUKS.Labeled(sequenceName);
        if (thought == null)
        {
            SetStatus("Thought not found");
            sequenceContentInput.Text = "";
            return;
        }

        // Determine if it's a sequence or context
        if (IsSequence(thought))
        {
            isEditingSequence = true;
            LoadSequenceContent(thought, parent);
        }
        else
        {
            isEditingSequence = false;
            LoadContextContent(thought, parent);
            sequenceNameInput.Text = "";
        }
    }

    private void LoadSequenceContent(Thought thought, ModuleSequenceEditor parent)
    {
        // Find the first sequence element
        Thought sequenceThought = parent.theUKS.Labeled(thought.Label + "-seq0");
        if (sequenceThought == null)
        {
            // Try to find any SeqElement link
            foreach (Link link in thought.LinksTo)
            {
                if (link.To is SeqElement)
                {
                    sequenceThought = link.To;
                    break;
                }
            }
        }

        if (sequenceThought is not SeqElement firstElement)
        {
            SetStatus("Sequence element not found");
            sequenceContentInput.Text = "";
            return;
        }

        // Flatten the sequence and display it
        List<Thought> elements = parent.theUKS.FlattenSequence(firstElement);
        StringBuilder sb = new StringBuilder();

        for (int i = 0; i < elements.Count; i++)
        {
            sb.AppendLine(elements[i]?.ToString() ?? "");
        }

        sequenceContentInput.Text = sb.ToString();
        SetStatus($"Loaded sequence with {elements.Count} elements");
    }

    private void LoadContextContent(Thought context, ModuleSequenceEditor parent)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine(context.Label);

        // Get all children (contexts within this context)
        var children = context.Children.Where(c => c.Label.StartsWith("c")).OrderBy(c => c.Label).ToList();

        foreach (var child in children)
        {
            sb.AppendLine($"  {child.Label}");
            
            // Get all "has" links
            var hasLinks = child.LinksTo.Where(l => l.LinkType?.Label == "has").ToList();
            foreach (var hasLink in hasLinks)
            {
                sb.AppendLine($"    [{child.Label}→has→{hasLink.To}]");
            }

            // Get all "response" links
            var responseLinks = child.LinksTo.Where(l => l.LinkType?.Label == "response").ToList();
            foreach (var responseLink in responseLinks)
            {
                sb.AppendLine($"    [{child.Label}→response→{responseLink.To}]");
            }
        }

        sequenceContentInput.Text = sb.ToString();
        SetStatus($"Loaded context with {children.Count} child contexts");
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        ModuleSequenceEditor parent = (ModuleSequenceEditor)base.ParentModule;
        if (parent?.theUKS == null) return;

        string sequenceName = sequenceNameInput.Text?.Trim();
        if (string.IsNullOrEmpty(sequenceName))
        {
            isEditingSequence = false;
        }
        else
            isEditingSequence = true;

        string content = sequenceContentInput.Text?.Trim();
        if (string.IsNullOrEmpty(content))
        {
            SetStatus("No content to save");
            return;
        }

        string[] lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        
        // Use the saved state to determine how to save
        if (isEditingSequence)
        {
            SaveSequence(parent, sequenceName, lines);
        }
        else
        {
            SaveContext(parent, sequenceName, lines);
        }
    }

    private void SaveSequence(ModuleSequenceEditor parent, string sequenceName, string[] lines)
    {
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
                Thought t = parent.theUKS.GetOrAddThought(statement,"Task");
                newElements.Add(t);
            }
        }

        if (newElements.Count == 0)
        {
            SetStatus("No valid elements found");
            return;
        }

        // Check if this sequence already exists
        Thought rootElement = parent.theUKS.Labeled(sequenceName);
        bool isNewSequence = (rootElement == null);
        
        // Get or create the root element
        //if (rootElement == null)
        {
            rootElement = parent.theUKS.GetOrAddThought(sequenceName, "Task");
        }

        // Find existing sequence link to update
        Link linkToChange = null;
        foreach (Link l in rootElement.LinksTo)
            if (l.To is SeqElement s)
            { 
                linkToChange = l;
                break; 
            }

        // Delete the old sequence if it exists
        Thought oldSequence = parent.theUKS.Labeled(sequenceName + "-seq0");
        if (oldSequence is SeqElement oldSeqElement)
        {
            parent.theUKS.DeleteSequence(oldSeqElement);
        }

        // Create the new sequence
        SeqElement newSequence = parent.theUKS.AddSequence(sequenceName, newElements);
        
        if (newSequence != null)
        {
            newSequence.Label = sequenceName + "-seq0";
            
            // Link the sequence to the root element
            if (linkToChange != null)
            {
                linkToChange.To = newSequence;
            }
            else
            {
                // Create new link if it didn't exist
                Thought stepsType = parent.theUKS.GetOrAddThought("steps", "LinkType");
                rootElement.AddLink(stepsType, newSequence);
            }
            
            if (isNewSequence)
                SetStatus($"Created new sequence '{sequenceName}' with {newElements.Count} elements");
            else
                SetStatus($"Updated sequence '{sequenceName}' with {newElements.Count} elements");
        }
        else
        {
            SetStatus("Failed to create sequence");
        }
    }

    private void SaveContext(ModuleSequenceEditor parent, string contextName, string[] lines)
    {
        if (lines.Length == 0)
        {
            SetStatus("No content to save");
            return;
        }

        // Use the first line as the root context name
        string rootContextName = lines[0].Trim();
        
        // Get or create the root context
        Thought rootContext = parent.theUKS.GetOrAddThought(rootContextName, "Task");
        
        // Track current context at each indentation level
        Dictionary<int, Thought> indentationStack = new Dictionary<int, Thought>();
        indentationStack[0] = rootContext;
        
        Thought currentContext = null;
        int previousIndent = 0;

        for (int i = 1; i < lines.Length; i++) // Skip first line (root context name)
        {
            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            // Calculate indentation level (number of leading spaces)
            int indent = line.Length - line.TrimStart().Length;
            string trimmedLine = line.Trim();

            //is there a weight on the end of this line?
            int sp = trimmedLine.LastIndexOf(" ");
            sp++;
            float newWeight = 1f;
            if (float.TryParse(trimmedLine[sp..], out newWeight))
            { sp--; trimmedLine = trimmedLine[..sp]; }
            else
                newWeight = 1;

            // Determine if this is a context name or a relationship
            if (trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]"))
            {
                // This is a relationship statement
                if (currentContext == null)
                {
                    SetStatus($"Relationship found without context: {trimmedLine}");
                    return;
                }

                // Parse the relationship [subject->linkType->object]
                string statement = trimmedLine;
                statement = statement.Replace("→", "->");

                Link link = (Link)parent.theUKS.ProcessSingleLine(statement);
                if (link == null)
                {
                    SetStatus($"Could not parse relationship: {trimmedLine}");
                    return;
                }
                Link x = link.From.AddLink(link.LinkType, link.To);
                x.Weight = newWeight;
            }
            else
            {
                // This is a context name
                if (indent > previousIndent)
                {
                    // Child of the previous context
                    Thought parentContext = indentationStack.ContainsKey(previousIndent)
                        ? indentationStack[previousIndent]
                        : rootContext;

                    currentContext = parent.theUKS.GetOrAddThought(trimmedLine, parentContext);
                    indentationStack[indent] = currentContext;
                }
                else if (indent == previousIndent)
                {
                    // Sibling of the previous context
                    Thought parentContext = indentationStack.ContainsKey(indent - 2)
                        ? indentationStack[indent - 2]
                        : rootContext;

                    currentContext = parent.theUKS.GetOrAddThought(trimmedLine, parentContext);
                    indentationStack[indent] = currentContext;
                }
                else // indent < previousIndent
                {
                    // Going back up the hierarchy
                    Thought parentContext = indentationStack.ContainsKey(indent - 2)
                        ? indentationStack[indent - 2]
                        : rootContext;

                    currentContext = parent.theUKS.GetOrAddThought(trimmedLine, parentContext);
                    indentationStack[indent] = currentContext;
                }

                previousIndent = indent;
            }
        }

        SetStatus($"Saved context: {rootContextName}");
    }

    private void ReplacePatternInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            PerformReplace();
            e.Handled = true;
        }
    }

    private void SequenceContentInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.X && Keyboard.Modifiers == ModifierKeys.Control)
        {
            PerformUndo();
            e.Handled = true;
        }
    }

    private void PerformReplace()
    {
        string searchPattern = searchPatternInput.Text?.Trim();
        string replacePattern = replacePatternInput.Text?.Trim();

        if (string.IsNullOrEmpty(searchPattern))
        {
            SetStatus("Search pattern is empty");
            return;
        }

        if (string.IsNullOrEmpty(replacePattern))
        {
            SetStatus("Replace pattern is empty");
            return;
        }

        string content = sequenceContentInput.Text;
        if (string.IsNullOrEmpty(content))
        {
            SetStatus("No content to replace");
            return;
        }

        // Store current content for undo
        undoContent = content;

        // Use regex to replace pattern like "c1", "c2", etc. with "newString1", "newString2", etc.
        // Matches the search pattern followed by digits
        var regex = new System.Text.RegularExpressions.Regex($@"\b{System.Text.RegularExpressions.Regex.Escape(searchPattern)}(\d+)\b");
        
        int replacementCount = 0;
        string newContent = regex.Replace(content, match =>
        {
            replacementCount++;
            return replacePattern + match.Groups[1].Value; // Keep the digit part
        });

        sequenceContentInput.Text = newContent;
        SetStatus($"Replaced {replacementCount} occurrence(s) of '{searchPattern}X' with '{replacePattern}X' (Ctrl+X to undo)");
    }

    private void PerformUndo()
    {
        if (!string.IsNullOrEmpty(undoContent))
        {
            sequenceContentInput.Text = undoContent;
            SetStatus("Undo successful");
            undoContent = ""; // Clear undo buffer after using it
        }
        else
        {
            SetStatus("Nothing to undo");
        }
    }
}