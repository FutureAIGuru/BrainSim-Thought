// The logic of ModuleSequenceEditorDlg.xaml.cs. On the desktop the task text format
// (sequences "name^" with indented steps, contexts with cases/conditions/responses) is
// parsed and written by the dialog itself; that is logic, so it lives here, unchanged.
// Editing the text (find and replace, undo) is the app's.
using System.Text;
using UKS;

namespace BrainSimulator.Modules;

public sealed class SequenceEditorState
{
    public List<string> Tasks { get; set; } = new();
    /// <summary>ModuleAlgorithm's current step, which the desktop highlights in the editor.</summary>
    public string Executing { get; set; }
}

public sealed class TaskTextDto
{
    public string Content { get; set; }
    public string Message { get; set; }
}

sealed class SequenceEditorApi : ModuleApi
{
    UKS.UKS theUKS;

    public override string State(ModuleBase m)
    {
        theUKS ??= m.theUKS;
        object vlu = ModuleApis.Active<ModuleAlgorithm>()?.LastExecutedStep?.VLU;
        return Json(new SequenceEditorState
        {
            Tasks = AlgorithmApi.Tasks(theUKS),
            Executing = vlu is null ? null : vlu is Link link ? link.ToString() : ((Thought)vlu).Label,
        }, ApiJson.Default.SequenceEditorState);
    }

    public override string Call(ModuleBase m, string op, ApiArgs args)
    {
        theUKS ??= m.theUKS;
        string taskName = args.Str("task").Trim();
        if (taskName == "") throw new ArgumentException("No task name specified");
        switch (op)
        {
            case "load":
                Thought task = theUKS.Labeled(taskName) ?? throw new ArgumentException("Task not found");
                string content = TaskText(task, out int items);
                return Json(new TaskTextDto { Content = content, Message = $"Loaded task '{task.Label}' with {items} items" }, ApiJson.Default.TaskTextDto);
            case "save":
                string text = args.Str("content").Trim();
                if (text == "") throw new ArgumentException("No content to save");
                return Message(Save(taskName, text));
            default:
                return Unknown(op);
        }
    }

    static bool IsSequence(Thought thought) => thought.LinksTo.Any(link => link.To is SeqElement);

    /// <summary>A task as editable text: its sequences ("name^" and indented steps) and contexts.</summary>
    string TaskText(Thought task, out int itemCount)
    {
        StringBuilder sb = new();
        itemCount = 0;
        if (task.Children.Count == 0)
            sb.Append(FormatSequence(task, task));
        foreach (var child in task.Children)
        {
            if (itemCount > 0) sb.AppendLine();
            sb.Append(IsSequence(child) ? FormatSequence(child, task) : FormatContext(child));
            itemCount++;
        }
        return sb.ToString();
    }

    string FormatSequence(Thought sequence, Thought task)
    {
        StringBuilder sb = new();
        sb.AppendLine($"{sequence.Label}^");
        Thought sequenceThought = theUKS.Labeled(sequence.Label + "-seq0")
            ?? sequence.LinksTo.FirstOrDefault(link => link.To is SeqElement)?.To;
        if (sequenceThought is SeqElement firstElement)
        {
            foreach (var element in theUKS.FlattenSequence(firstElement))
            {
                string elementStr = element?.ToString() ?? "";
                if (element is not null && element is not Link && !element.Parents.Contains(task))
                    elementStr = "$" + elementStr;   // a global reference from another task
                sb.AppendLine($"  {elementStr}");
            }
        }
        return sb.ToString();
    }

    static string FormatContext(Thought context)
    {
        StringBuilder sb = new();
        sb.AppendLine(context.Label);
        foreach (var child in context.Children)
        {
            sb.AppendLine($"  {child.Label}");
            foreach (var hasLink in child.LinksTo.Where(l => l.LinkType?.Label == "has"))
            {
                if (hasLink.To is not Link innerLink) continue;
                string linkLine = $"    [{innerLink.From}→{innerLink.LinkType}→{innerLink.To}]";
                if (Math.Abs(hasLink.Weight - 1.0f) > 0.001f) linkLine += $" {hasLink.Weight:F2}";
                sb.AppendLine(linkLine);
            }
            foreach (var responseLink in child.LinksTo.Where(l => l.LinkType?.Label == "response"))
            {
                string linkLine = $"    {responseLink.To}";
                if (Math.Abs(responseLink.Weight - 1.0f) > 0.001f) linkLine += $" {responseLink.Weight:F2}";
                sb.AppendLine(linkLine);
            }
        }
        return sb.ToString();
    }

    string Save(string taskName, string content)
    {
        bool previousSuppressState = theUKS.SuppressFiring;
        try
        {
            theUKS.SuppressFiring = true;
            Thought taskThought = theUKS.Labeled(taskName);
            bool isNewTask = taskThought is null;
            taskThought ??= theUKS.GetOrAddThought(taskName, theUKS.Labeled("Task"));
            theUKS.DeleteAllChildrenAndLinks(taskThought);
            SaveTaskContent(taskThought, content);
            return isNewTask ? $"Created new task '{taskName}'" : $"Updated task '{taskName}'";
        }
        finally
        {
            theUKS.SuppressFiring = previousSuppressState;
        }
    }

    void SaveTaskContent(Thought task, string content)
    {
        string[] lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        int lineIndex = 0;
        while (lineIndex < lines.Length)
        {
            string trimmedLine = lines[lineIndex].Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine)) { lineIndex++; continue; }
            lineIndex = trimmedLine.EndsWith("^")
                ? SaveSequenceChild(task, lines, lineIndex)
                : SaveContextChild(task, lines, lineIndex);
        }
    }

    int SaveSequenceChild(Thought task, string[] lines, int startIndex)
    {
        string sequenceName = lines[startIndex].Trim().TrimEnd('^');
        Thought sequenceThought = theUKS.GetOrAddThought(sequenceName, task);
        List<Thought> sequenceElements = new();
        int currentIndex = startIndex + 1;
        while (currentIndex < lines.Length && (lines[currentIndex].StartsWith("  ") || lines[currentIndex].StartsWith("\t")))
        {
            string trimmedLine = lines[currentIndex].Trim();
            if (trimmedLine != "")
            {
                if (trimmedLine.StartsWith("$"))
                {
                    Thought globalThought = theUKS.Labeled(trimmedLine[1..]);
                    if (globalThought is not null) sequenceElements.Add(globalThought);
                }
                else
                {
                    string statement = trimmedLine.Replace("→", "->");
                    if (theUKS.ProcessSingleLineWithNesting(" " + statement) is Link element)
                        sequenceElements.Add(element);
                    else
                        sequenceElements.Add(theUKS.GetOrAddThought(trimmedLine, task));
                }
            }
            currentIndex++;
        }

        if (sequenceElements.Count > 0)
        {
            if (theUKS.Labeled(sequenceName + "-seq0") is SeqElement oldSeqElement)
                theUKS.DeleteSequence(oldSeqElement);
            SeqElement newSequence = theUKS.AddSequence(sequenceName, sequenceElements);
            if (newSequence is not null)
            {
                newSequence.Label = sequenceName + "-seq0";
                sequenceThought.AddLink(theUKS.GetOrAddThought("steps", "LinkType"), newSequence);
            }
        }
        return currentIndex;
    }

    int SaveContextChild(Thought task, string[] lines, int startIndex)
    {
        Thought contextThought = theUKS.GetOrAddThought(lines[startIndex].Trim(), task);
        int currentIndex = startIndex + 1;
        Thought currentContext = null;

        while (currentIndex < lines.Length)
        {
            string line = lines[currentIndex];
            int indent = line.Length - line.TrimStart().Length;
            if (indent == 0) break;

            string trimmedLine = line.Trim();
            if (trimmedLine == "") { currentIndex++; continue; }

            float newWeight = 1f;
            int sp = trimmedLine.LastIndexOf(' ');
            if (sp > 0 && float.TryParse(trimmedLine[(sp + 1)..], out newWeight))
                trimmedLine = trimmedLine[..sp];
            else
                newWeight = 1f;

            if (trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]"))
            {
                if (currentContext is not null &&
                    theUKS.ProcessSingleLineWithNesting(" " + trimmedLine.Replace("→", "->")) is Link innerLink)
                {
                    Thought hasType = theUKS.Labeled("has") ?? theUKS.GetOrAddThought("has", "LinkType");
                    currentContext.AddLink(hasType, innerLink).Weight = newWeight;
                }
            }
            else if (indent == 2)
            {
                currentContext = theUKS.GetOrAddThought(trimmedLine, contextThought);
            }
            else if (indent > 2 && currentContext is not null)
            {
                Thought targetThought = theUKS.Labeled(trimmedLine) ?? theUKS.GetOrAddThought(trimmedLine, task);
                Thought responseType = theUKS.Labeled("response") ?? theUKS.GetOrAddThought("response", "LinkType");
                currentContext.AddLink(responseType, targetThought).Weight = newWeight;
            }
            currentIndex++;
        }
        return currentIndex;
    }
}
