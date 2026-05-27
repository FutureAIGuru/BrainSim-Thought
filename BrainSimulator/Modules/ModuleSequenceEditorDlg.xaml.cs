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
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UKS;

namespace BrainSimulator.Modules;

public partial class ModuleSequenceEditorDlg : ModuleBaseDlg
{
    private string undoContent = ""; // Store content for undo
    private UKS.UKS theUKS; // Store reference to UKS
    private bool isHighlightedByCode = false; // Track if selection was made by our code
    private string lastHighlightedText = null; // Track the last highlighted text
    private bool _isTaskNameChangingInternally = false; // Track internal changes for autocomplete
    private int _previousTaskNameLength = 0; // Track previous length for autocomplete

    public ModuleSequenceEditorDlg()
    {
        InitializeComponent();
        sequenceNameInput.KeyDown += SequenceNameInput_KeyDown;
        sequenceNameInput.PreviewKeyDown += SequenceNameInput_PreviewKeyDown;
        sequenceNameInput.TextChanged += SequenceNameInput_TextChanged;
        saveButton.Click += SaveButton_Click;
        replacePatternInput.KeyDown += ReplacePatternInput_KeyDown;
        sequenceContentInput.KeyDown += SequenceContentInput_KeyDown;
        sequenceContentInput.TextChanged += SequenceContentInput_TextChanged;
        sequenceContentInput.Loaded += SequenceContentInput_Loaded;
        sequenceContentInput.SelectionChanged += SequenceContentInput_SelectionChanged;
        sequenceContentInput.PreviewMouseWheel += SequenceContentInput_PreviewMouseWheel;
    }


    private void SequenceNameInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var tb = sequenceNameInput as TextBox;
        if (tb is null) return;

        // Allow text changes when keys like backspace, delete are pressed
        if (e.Key == Key.Back || e.Key == Key.Delete)
        {
            _isTaskNameChangingInternally = true;
            int caretIndex = tb.CaretIndex;
            if (e.Key == Key.Back) caretIndex--;
            if (caretIndex < 0) caretIndex = 0;
            tb.Text = tb.Text.Substring(0, caretIndex);
            tb.CaretIndex = caretIndex;
            e.Handled = true;
            _isTaskNameChangingInternally = false;
            if (e.Key == Key.Back)
                SequenceNameInput_TextChanged(null, null);
        }
    }

    private void SequenceNameInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isTaskNameChangingInternally)
            return;

        var tb = sequenceNameInput as TextBox;
        if (tb == null) return;

        string searchText = sequenceNameInput.Text;

        // Track only the unselected (typed) portion
        int actualTypedLength = tb.SelectionStart;

        // Check if we're deleting text
        bool isDeleting = actualTypedLength < _previousTaskNameLength;

        // Only autocomplete if text is being added (not deleted)
        if (string.IsNullOrEmpty(searchText) || isDeleting)
        {
            _previousTaskNameLength = actualTypedLength;
            return;
        }

        // Update previous length before autocomplete might change it
        _previousTaskNameLength = actualTypedLength;

        if (theUKS == null)
            return;

        // Get all children of "Task" excluding "Variable"
        Thought taskThought = theUKS.Labeled("Task");
        if (taskThought == null)
            return;

        // Get the text that was actually typed (without selection)
        string typedText = searchText.Substring(0, actualTypedLength);

        var suggestion = taskThought.Children
            .Where(t => t.Label.ToLower() != "variable" &&
                       t.Label.StartsWith(typedText, System.StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Label)
            .Select(t => t.Label)
            .FirstOrDefault();

        if (suggestion != null && !suggestion.Equals(searchText, System.StringComparison.OrdinalIgnoreCase))
        {
            int caretIndex = tb.CaretIndex;
            _isTaskNameChangingInternally = true;
            sequenceNameInput.Text = suggestion;
            tb.CaretIndex = caretIndex;
            tb.SelectionStart = caretIndex;
            tb.SelectionLength = suggestion.Length - caretIndex;
            tb.SelectionOpacity = .4;
            _isTaskNameChangingInternally = false;
        }
    }

    public override bool Draw(bool checkDrawTimer)
    {
        if (!base.Draw(checkDrawTimer)) return false;
        //this has a timer so that no matter how often you might call draw, the dialog
        //only updates 10x per second
        ModuleSequenceEditor parent = (ModuleSequenceEditor)base.ParentModule;
        if (theUKS is null)
            theUKS = parent?.theUKS;

        // Load saved font size if available
        string sizeString = parent?.GetSavedDlgAttribute("fontSize");
        if (!string.IsNullOrEmpty(sizeString) && int.TryParse(sizeString, out int fontSize) && fontSize > 0)
        {
            if (Math.Abs(sequenceContentInput.FontSize - fontSize) > 0.1)
                sequenceContentInput.FontSize = fontSize;
        }

        // Get the ModuleAlgorithm to highlight last executed step
        if (MainWindow.theWindow != null)
        {
            ModuleAlgorithm algorithmModule = MainWindow.theWindow.activeModules
                .OfType<ModuleAlgorithm>()
                .FirstOrDefault();

            if (algorithmModule?.LastExecutedStep?.VLU is not null)
            {
                string currentLabel = null;

                // Get the label from the VLU (could be Link or Thought)
                if (algorithmModule.LastExecutedStep.VLU is Link link)
                {
                    currentLabel = link.ToString();
                }
                else if (algorithmModule.LastExecutedStep.VLU is Thought thought)
                {
                    currentLabel = thought.Label;
                }

                // Only highlight if the text has changed
                if (!string.IsNullOrEmpty(currentLabel) && currentLabel != lastHighlightedText)
                {
                    HighlightTextInSequence(currentLabel);
                    lastHighlightedText = currentLabel;
                }
            }
            else
            {
                // Clear selection only if we previously had something highlighted
                if (lastHighlightedText != null)
                {
                    ClearSelection();
                    lastHighlightedText = null;
                }
            }
        }

        return true;
    }
    private void SequenceContentInput_SelectionChanged(object sender, RoutedEventArgs e)
    {
        // If selection changed and it wasn't us, clear our flag
        if (!isHighlightedByCode)
        {
            // User made a selection, so we shouldn't clear it
        }
        else
        {
            // Reset flag after our programmatic selection is done
            isHighlightedByCode = false;
        }
    }

    private void ClearSelection()
    {
        if (isHighlightedByCode && sequenceContentInput.SelectionLength > 0)
        {
            sequenceContentInput.Select(0, 0);
            isHighlightedByCode = false;
        }
    }

    private void HighlightTextInSequence(string textToHighlight)
    {
        string content = sequenceContentInput.Text;
        if (string.IsNullOrEmpty(content))
            return;

        // Find the text in the content
        int index = content.IndexOf(textToHighlight, StringComparison.OrdinalIgnoreCase);

        if (index >= 0)
        {
            IInputElement previouslyFocusedElement = Keyboard.FocusedElement;
            sequenceContentInput.Focus();
            // Select the text WITHOUT taking focus
            sequenceContentInput.Select(index, textToHighlight.Length);

            sequenceContentInput.ScrollToLine(sequenceContentInput.GetLineIndexFromCharacterIndex(index));
            // Mark that we're making this selection
            isHighlightedByCode = true;
            previouslyFocusedElement?.Focus();
        }
        else
            ClearSelection();
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
        if (theUKS == null) return;

        string taskName = sequenceNameInput.Text?.Trim();
        if (string.IsNullOrEmpty(taskName))
        {
            SetStatus("No task name specified");
            return;
        }

        // Try to find the task
        Thought taskThought = theUKS.Labeled(taskName);
        if (taskThought == null)
        {
            SetStatus("Task not found");
            sequenceContentInput.Text = "";
            return;
        }

        // Load the entire task with all its children
        LoadTaskContent(taskThought);
    }

    private void LoadTaskContent(Thought task)
    {
        StringBuilder sb = new StringBuilder();
        int itemCount = 0;

        if (task.Children.Count == 0)
        {
            string sequenceContent = FormatSequence(task, task);
            sb.Append(sequenceContent);
        }
        foreach (var child in task.Children)
        {
            if (itemCount > 0)
                sb.AppendLine(); // Blank line between items

            // Determine if this child is a sequence or context
            if (IsSequence(child))
            {
                // Display as sequence with ^ prefix
                string sequenceContent = FormatSequence(child, task);
                sb.Append(sequenceContent);
            }
            else
            {
                // Display as context
                string contextContent = FormatContext(child, task);
                sb.Append(contextContent);
            }

            itemCount++;
        }

        sequenceContentInput.Text = sb.ToString();
        SetStatus($"Loaded task '{task.Label}' with {itemCount} items");
    }

    private string FormatSequence(Thought sequence, Thought task)
    {
        StringBuilder sb = new StringBuilder();

        // Add sequence header with ^ prefix
        sb.AppendLine($"{sequence.Label}^");

        // Find the first sequence element
        Thought sequenceThought = theUKS.Labeled(sequence.Label + "-seq0");
        if (sequenceThought == null)
        {
            // Try to find any SeqElement link
            foreach (Link link in sequence.LinksTo)
            {
                if (link.To is SeqElement)
                {
                    sequenceThought = link.To;
                    break;
                }
            }
        }

        if (sequenceThought is SeqElement firstElement)
        {
            // Flatten the sequence and display it
            List<Thought> elements = theUKS.FlattenSequence(firstElement);

            foreach (var element in elements)
            {
                string elementStr = element?.ToString() ?? "";

                // Check if this element is a global reference (from a different task)
                if (element is Thought elementThought && !IsLink(element))
                {
                    if (IsExternalToTask(element, task))
                    {
                        // This is a global reference, prefix with $
                        elementStr = "$" + elementStr;
                    }
                }

                sb.AppendLine($"  {elementStr}");
            }
        }

        return sb.ToString();
    }

    private bool IsLink(Thought thought)
    {
        return thought is Link;
    }

    private bool IsExternalToTask(Thought t, Thought task)
    {
        if (t.Parents.Contains(task)) return false;
        return true;
    }
    private string FormatContext(Thought context, Thought task)
    {
        StringBuilder sb = new StringBuilder();

        // Add context header
        sb.AppendLine(context.Label);

        foreach (var child in context.Children)
        {
            sb.AppendLine($"  {child.Label}");

            // Get all "has" links - unwrap them to show just the inner condition
            var hasLinks = child.LinksTo.Where(l => l.LinkType?.Label == "has").ToList();
            foreach (var hasLink in hasLinks)
            {
                // Extract the inner link from the has relationship
                if (hasLink.To is Link innerLink)
                {
                    string linkLine = $"    [{innerLink.From}→{innerLink.LinkType}→{innerLink.To}]";

                    // Append weight if not 1.0
                    if (Math.Abs(hasLink.Weight - 1.0f) > 0.001f)
                    {
                        linkLine += $" {hasLink.Weight:F2}";
                    }

                    sb.AppendLine(linkLine);
                }
            }

            // Get all "response" links - simplified to just the target name
            var responseLinks = child.LinksTo.Where(l => l.LinkType?.Label == "response").ToList();
            foreach (var responseLink in responseLinks)
            {
                string linkLine = $"    {responseLink.To}";

                // Append weight if not 1.0
                if (Math.Abs(responseLink.Weight - 1.0f) > 0.001f)
                {
                    linkLine += $" {responseLink.Weight:F2}";
                }

                sb.AppendLine(linkLine);
            }
        }

        return sb.ToString();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (theUKS == null) return;

        string taskName = sequenceNameInput.Text?.Trim();
        if (string.IsNullOrEmpty(taskName))
        {
            SetStatus("No task name specified");
            return;
        }

        string content = sequenceContentInput.Text?.Trim();
        if (string.IsNullOrEmpty(content))
        {
            SetStatus("No content to save");
            return;
        }

        // Suppress firing during save
        bool previousSuppressState = theUKS.SuppressFiring;
        try
        {
            theUKS.SuppressFiring = true;

            // Get or create the task
            Thought taskThought = theUKS.Labeled(taskName);
            bool isNewTask = (taskThought == null);

            if (taskThought == null)
            {
                Thought taskType = theUKS.Labeled("Task");
                taskThought = theUKS.GetOrAddThought(taskName, taskType);
            }

            // Delete all existing children
            theUKS.DeleteAllChildrenAndLinks(taskThought);

            // Parse and save the content
            SaveTaskContent(taskThought, content);

            if (isNewTask)
                SetStatus($"Created new task '{taskName}'");
            else
                SetStatus($"Updated task '{taskName}'");
        }
        finally
        {
            theUKS.SuppressFiring = previousSuppressState;
        }
    }

    private void SaveTaskContent(Thought task, string content)
    {
        string[] lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        int lineIndex = 0;
        while (lineIndex < lines.Length)
        {
            string line = lines[lineIndex];
            string trimmedLine = line.Trim();

            if (string.IsNullOrWhiteSpace(trimmedLine))
            {
                lineIndex++;
                continue;
            }

            // Check if this is a sequence (ends with ^)
            if (trimmedLine.EndsWith("^"))
            {
                // Parse sequence
                lineIndex = SaveSequenceChild(task, lines, lineIndex);
            }
            else
            {
                // Parse context
                lineIndex = SaveContextChild(task, lines, lineIndex);
            }
        }
    }

    private int SaveSequenceChild(Thought task, string[] lines, int startIndex)
    {
        string sequenceName = lines[startIndex].Trim().TrimEnd('^');

        // Create the sequence as a child of the task
        Thought sequenceThought = theUKS.GetOrAddThought(sequenceName, task);

        // Collect sequence elements
        List<Thought> sequenceElements = new List<Thought>();
        int currentIndex = startIndex + 1;

        // Read indented lines until we hit a non-indented line or end
        while (currentIndex < lines.Length)
        {
            string line = lines[currentIndex];

            // Check if line is indented (part of this sequence)
            if (line.StartsWith("  ") || line.StartsWith("\t"))
            {
                string trimmedLine = line.Trim();
                if (!string.IsNullOrEmpty(trimmedLine))
                {
                    // Check if this is a global reference
                    if (trimmedLine.StartsWith("$"))
                    {
                        // Global reference - look up existing thought, don't create
                        string globalName = trimmedLine.Substring(1); // Remove $
                        Thought globalThought = theUKS.Labeled(globalName);
                        if (globalThought != null)
                        {
                            sequenceElements.Add(globalThought);
                        }
                    }
                    else
                    {
                        // Parse the statement
                        string statement = trimmedLine.Replace("→", "->");
                        Link element = (Link)theUKS.ProcessSingleLineWithNesting(" " + statement);
                        if (element != null)
                        {
                            sequenceElements.Add(element);
                        }
                        else
                        {
                            Thought t = theUKS.GetOrAddThought(trimmedLine, task);
                            sequenceElements.Add(t);
                        }
                    }
                }
                currentIndex++;
            }
            else
            {
                // End of this sequence
                break;
            }
        }

        // Create the sequence if we have elements
        if (sequenceElements.Count > 0)
        {
            // Delete old sequence if it exists
            Thought oldSeq = theUKS.Labeled(sequenceName + "-seq0");
            if (oldSeq is SeqElement oldSeqElement)
            {
                theUKS.DeleteSequence(oldSeqElement);
            }

            // Create new sequence
            SeqElement newSequence = theUKS.AddSequence(sequenceName, sequenceElements);
            if (newSequence != null)
            {
                newSequence.Label = sequenceName + "-seq0";
                Thought stepsType = theUKS.GetOrAddThought("steps", "LinkType");
                sequenceThought.AddLink(stepsType, newSequence);
            }
        }

        return currentIndex;
    }

    private int SaveContextChild(Thought task, string[] lines, int startIndex)
    {
        string contextName = lines[startIndex].Trim();

        // Create the context as a child of the task
        Thought contextThought = theUKS.GetOrAddThought(contextName, task);

        int currentIndex2 = startIndex + 1;

        // Track current context at each indentation level
        Dictionary<int, Thought> indentationStack = new Dictionary<int, Thought>();
        indentationStack[0] = contextThought;

        Thought currentContext = null;
        int previousIndent = 0;

        // Read until we hit a non-indented line or end
        while (currentIndex2 < lines.Length)
        {
            string line = lines[currentIndex2];

            // Calculate indentation
            int indent = line.Length - line.TrimStart().Length;

            // If no indentation, we've reached the next top-level item
            if (indent == 0)
                break;

            string trimmedLine = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine))
            {
                currentIndex2++;
                continue;
            }

            // Extract weight if present
            float newWeight = 1f;
            int sp = trimmedLine.LastIndexOf(" ");
            if (sp > 0)
            {
                string weightStr = trimmedLine.Substring(sp + 1);
                if (float.TryParse(weightStr, out newWeight))
                {
                    trimmedLine = trimmedLine.Substring(0, sp);
                }
            }

            // Check if this is a condition link (starts with [)
            if (trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]"))
            {
                if (currentContext == null)
                {
                    currentIndex2++;
                    continue;
                }

                // Parse the inner condition link
                string statement = " " + trimmedLine.Replace("→", "->");
                Link innerLink = (Link)theUKS.ProcessSingleLineWithNesting(statement);
                if (innerLink != null)
                {
                    // Create the has link: currentContext→has→innerLink
                    Thought hasType = theUKS.Labeled("has");
                    if (hasType == null)
                    {
                        hasType = theUKS.GetOrAddThought("has", "LinkType");
                    }

                    Link hasLink = currentContext.AddLink(hasType, innerLink);
                    hasLink.Weight = newWeight;
                }
            }
            else
            {
                // This is either a case name or a response reference
                // Determine parent based on indentation
                if (indent == 2)
                {
                    // This is a case (direct child of context)
                    currentContext = theUKS.GetOrAddThought(trimmedLine, contextThought);
                    indentationStack[indent] = currentContext;
                    previousIndent = indent;
                }
                else if (indent > 2)
                {
                    // This is a response reference (deeper indentation)
                    if (currentContext == null)
                    {
                        currentIndex2++;
                        continue;
                    }

                    Thought targetThought = theUKS.Labeled(trimmedLine);
                    if (targetThought == null)
                    {
                        targetThought = theUKS.GetOrAddThought(trimmedLine, task);
                    }

                    Thought responseType = theUKS.Labeled("response");
                    if (responseType == null)
                    {
                        responseType = theUKS.GetOrAddThought("response", "LinkType");
                    }

                    Link x = currentContext.AddLink(responseType, targetThought);
                    x.Weight = newWeight;
                }
            }
            currentIndex2++;
        }

        return currentIndex2;
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
        if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
        {
            PerformUndo();
            e.Handled = true;
        }
    }

    private void PerformReplace()
    {
        //quickie code to clean up lower-case "is" references
        //foreach (var kvp in ThoughtLabels.LabelList)
        //{
        //    if (kvp.Key.Contains(".is", StringComparison.OrdinalIgnoreCase))
        //    {
        //        Thought t = kvp.Value;
        //        string newLabel = t.Label.Replace(".is", ".IS");
        //        t.Label = newLabel;
        //    }
        //}

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

        // Count occurrences before replacing (case-insensitive)
        int replacementCount = 0;
        int index = 0;
        while ((index = content.IndexOf(searchPattern, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            replacementCount++;
            index += searchPattern.Length;
        }

        // Perform case-insensitive string replacement
        // We need to manually replace to maintain the exact replacement string
        StringBuilder result = new StringBuilder();
        int lastIndex = 0;
        index = 0;

        while ((index = content.IndexOf(searchPattern, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            // Append text before the match
            result.Append(content.Substring(lastIndex, index - lastIndex));
            // Append the replacement (maintaining its exact case)
            result.Append(replacePattern);
            // Move past the matched text
            index += searchPattern.Length;
            lastIndex = index;
        }

        // Append remaining text after last match
        result.Append(content.Substring(lastIndex));

        string newContent = result.ToString();

        sequenceContentInput.Text = newContent;
        SetStatus($"Replaced {replacementCount} occurrence(s) of '{searchPattern}' with '{replacePattern}' (Ctrl+Z to undo)");
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

    // Using the mouse-wheel while pressing ctrl key changes the font size
    private void SequenceContentInput_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.GetKeyStates(Key.LeftCtrl) & KeyStates.Down | Keyboard.GetKeyStates(Key.RightCtrl) & KeyStates.Down) != 0)
        {
            if (e.Delta < 0)
            {
                if (sequenceContentInput.FontSize > 2)
                    sequenceContentInput.FontSize -= 1;
            }
            else if (e.Delta > 0)
            {
                sequenceContentInput.FontSize += 1;
            }
            lineNumbersTextBox.FontSize = sequenceContentInput.FontSize;

            // Save the font size
            ModuleSequenceEditor parent = (ModuleSequenceEditor)ParentModule;
            parent?.SetSavedDlgAttribute("fontSize", sequenceContentInput.FontSize.ToString());

            e.Handled = true; // Prevent scrolling
        }
    }

}