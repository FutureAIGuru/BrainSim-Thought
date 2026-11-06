// The logic behind the smaller desktop dialogs (BrainSimulator/Modules/*Dlg.xaml.cs): state
// as plain data, and the operations their buttons performed. Presentation is in SwiftUI.
using Pluralize.NET;
using UKS;

namespace BrainSimulator.Modules;

// ---------- ModuleActionDlg ----------

public sealed class ActionState
{
    public string LastContext { get; set; }
    public string LastAction { get; set; }
}

sealed class ActionApi : ModuleApi
{
    static string Show(Thought t) => t switch
    {
        null => null,
        Link l when !string.IsNullOrWhiteSpace(l.To?.Label) => l.To.Label,
        Link l when !string.IsNullOrWhiteSpace(l.Label) => l.Label,
        _ => Format(t),
    };

    public override string State(ModuleBase m)
    {
        var a = (ModuleAction)m;
        return Json(new ActionState { LastContext = Show(a._lastContext), LastAction = Show(a._lastSelectedAction) }, ApiJson.Default.ActionState);
    }
}

// ---------- ModuleAgentsDlg ----------

public sealed class AgentsState
{
    public List<string> Agents { get; set; } = new();
    public string Status { get; set; }
}

sealed class AgentsApi : ModuleApi
{
    public override string State(ModuleBase m)
    {
        var a = (ModuleAgents)m;
        return Json(new AgentsState { Agents = a.Agents.Select(x => x.AgentName).ToList(), Status = a.Status }, ApiJson.Default.AgentsState);
    }

    public override string Call(ModuleBase m, string op, ApiArgs args)
    {
        if (op != "run") return Unknown(op);
        ((ModuleAgents)m).RunAgent(args.Str("agent"));
        return Done();
    }
}

// ---------- ModuleAddCountsDlg, ModuleAttributeBubbleDlg, ModuleBalanceTreeDlg, ModuleClassCreateDlg, ModuleRemoveRedundancyDlg ----------

public sealed class LogState
{
    public string Log { get; set; }
}

sealed class RunOnceAgentApi : ModuleApi
{
    public override string State(ModuleBase m) => Json(new LogState { Log = ((IManualAgent)m).DebugLog }, ApiJson.Default.LogState);

    public override string Call(ModuleBase m, string op, ApiArgs args)
    {
        if (op != "run") return Unknown(op);
        switch (m)
        {
            case ModuleAddCounts a: a.DoTheWork(); break;
            case ModuleAttributeBubble a: a.DoTheWork(); break;
            case ModuleBalanceTree a: a.DoTheWork(); break;
            case ModuleClassCreate a: a.DoTheWork(); break;
            case ModuleRemoveRedundancy a: a.DoTheWork(); break;
        }
        return Done();
    }
}

// ---------- ModuleWellBeingDlg ----------

public sealed class WellBeingState
{
    /// <summary>-1 very sad … +1 very happy (decays toward 0).</summary>
    public float Value { get; set; }
}

sealed class WellBeingApi : ModuleApi
{
    public override string State(ModuleBase m) => Json(new WellBeingState { Value = ModuleWellBeing.State }, ApiJson.Default.WellBeingState);

    public override string Call(ModuleBase m, string op, ApiArgs args)
    {
        switch (op)
        {
            case "set": ModuleWellBeing.SetState((float)args.Num("value")); break;
            case "increase": ModuleWellBeing.Increase(); break;
            case "decrease": ModuleWellBeing.Decrease(); break;
            default: return Unknown(op);
        }
        return State(m);
    }
}

// ---------- ModuleAttentionDlg ----------

public sealed class AttentionState
{
    public float Azimuth { get; set; }
    public float Elevation { get; set; }
    public string Focus { get; set; }
}

sealed class AttentionApi : ModuleApi
{
    public override string State(ModuleBase m)
    {
        var a = (ModuleAttention)m;
        return Json(new AttentionState { Azimuth = a.CenterAzimuthDeg.Degrees, Elevation = a.CenterElevationDeg.Degrees, Focus = a.CurrentFocus?.Label },
            ApiJson.Default.AttentionState);
    }

    public override string Call(ModuleBase m, string op, ApiArgs args)
    {
        if (op != "setCenter") return Unknown(op);
        var a = (ModuleAttention)m;
        a.SetCenterOfAttention(Angle.FromDegrees((float)args.Num("azimuth", a.CenterAzimuthDeg.Degrees)),
                               Angle.FromDegrees((float)args.Num("elevation", a.CenterElevationDeg.Degrees)));
        return Done();
    }
}

// ---------- ModuleSoundOutDlg ----------

public sealed class SoundOutState
{
    public int Cadence { get; set; }
    public int PitchOffset { get; set; }
}

sealed class SoundOutApi : ModuleApi
{
    public override string State(ModuleBase m)
    {
        var s = (ModuleSoundOut)m;
        return Json(new SoundOutState { Cadence = s.Cadence, PitchOffset = s.PitchOffset }, ApiJson.Default.SoundOutState);
    }

    public override string Call(ModuleBase m, string op, ApiArgs args)
    {
        var s = (ModuleSoundOut)m;
        switch (op)
        {
            case "play": s.PlayCMajorTriad(1000); break;
            case "setCadence": s.Cadence = args.Int("value", s.Cadence); break;
            case "setPitchOffset": s.PitchOffset = args.Int("value", s.PitchOffset); break;
            default: return Unknown(op);
        }
        return Done();
    }
}

// ---------- ModuleSoundInDlg ----------

public sealed class SoundInState
{
    /// <summary>The musical phrases that can be played.</summary>
    public List<string> Phrases { get; set; } = new();
}

sealed class SoundInApi : ModuleApi
{
    public override string State(ModuleBase m) =>
        Json(new SoundInState { Phrases = ThoughtLabels.GetThought("musicalPhrase")?.Children.Select(c => c.Label).ToList() ?? new() },
            ApiJson.Default.SoundInState);

    public override string Call(ModuleBase m, string op, ApiArgs args)
    {
        var s = (ModuleSoundIn)m;
        switch (op)
        {
            case "noteDown": s.StartNote(args.Int("note")); return Done();
            case "noteUp": s.StopNote(args.Int("note")); return Done();
            case "playPhrase":
                string phraseLabel = args.Str("phrase").Trim();
                Thought phrase = ThoughtLabels.GetThought(phraseLabel) ?? throw new ArgumentException($"Phrase '{phraseLabel}' not found.");
                var soundOut = ModuleApis.Active<ModuleSoundOut>() ?? throw new InvalidOperationException("SoundOut module not found.");
                soundOut.PlayThePhrase(phrase, true);
                return Message($"Playing: {phraseLabel}");
            default: return Unknown(op);
        }
    }
}

// ---------- ModuleStressTestDlg ----------

sealed class StressTestApi : ModuleApi
{
    public override string State(ModuleBase m) => "{}";

    public override string Call(ModuleBase m, string op, ApiArgs args) =>
        op == "add" ? Message(ModuleStressTest.AddManyTestItems(args.Int("count"))) : Unknown(op);
}

// ---------- ModuleLearnMelodyDlg ----------

sealed class LearnMelodyApi : ModuleApi
{
    public override string State(ModuleBase m) => "{}";

    public override string Call(ModuleBase m, string op, ApiArgs args)
    {
        if (op != "load") return Unknown(op);
        string path = args.Str("path").Trim();
        if (!File.Exists(path)) throw new FileNotFoundException("File not found.", path);
        ((ModuleLearnMelody)m).LoadMelodiesFromFile(path);
        return Message($"Melody loaded from file: {Path.GetFileName(path)}");
    }
}

// ---------- ModuleSequenceSegmenterDlg ----------

public sealed class JobState
{
    public string Status { get; set; }
    public bool Busy { get; set; }
    public string Trace { get; set; }
}

sealed class SequenceSegmenterApi : ModuleApi
{
    // the dialog's output box (SegmentationBox): the latest results, newest last
    readonly object traceLock = new();
    string trace = "";
    volatile bool cancel;

    public override string State(ModuleBase m)
    {
        var s = (ModuleSequenceSegmenter)m;
        string text;
        lock (traceLock) text = trace;
        return Json(new JobState { Status = status ?? s.LastStatus, Busy = busy, Trace = text }, ApiJson.Default.JobState);
    }

    void Append(string text)
    {
        lock (traceLock)
        {
            trace = trace.Length == 0 ? text : trace + Environment.NewLine + text;
            // keep the box (and every poll's JSON) small
            if (trace.Length > 20_000) trace = trace[^16_000..];
        }
    }

    /// <summary>One phrase into the learner, and its result (with the learning details) into the box.</summary>
    string Submit(ModuleSequenceSegmenter s, string phrase, bool showPhrase)
    {
        string result = SequenceSegmenterInput.SubmitLine(s, phrase);
        Append(showPhrase ? $"{phrase}: {result}" : s.LastStatus);
        if (s.ExplainLearning && !string.IsNullOrEmpty(s.LastLearningDetails)) Append(s.LastLearningDetails);
        return result;
    }

    public override string Call(ModuleBase m, string op, ApiArgs args)
    {
        var s = (ModuleSequenceSegmenter)m;
        switch (op)
        {
            case "submit":
                // typed lines show how they were learned, like checking the dialog's "Explain learning"
                s.ExplainLearning = true;
                status = Submit(s, args.Str("line"), showPhrase: false);
                return Message(status);
            case "experiment":
                // the desktop's "exp" with its default settings: the first 14 words, 100 phrases
                // of 10 words, seed 42
                if (busy) throw new InvalidOperationException("The segmenter is busy.");
                string[] words = { "dog", "has", "fur", "cat", "hat", "car", "a", "an", "the", "cam", "can", "cap", "cart", "camp" };
                var vocabulary = words.Select(w => new KeyValuePair<string, double>(w, 1)).ToList();
                var random = new Random(42);
                s.ExplainLearning = false;
                Append("Random seed: 42. Same starting UKS and settings required for comparison.");
                Run(100, i =>
                {
                    Submit(s, SequenceSegmenterInput.BuildRandomPhrase(vocabulary, 10, random), showPhrase: true);
                    int missing = words.Count(w => s.theUKS.Labeled("w:" + w) is not { isPlastic: false });
                    status = $"Experiment {i + 1}/100, {words.Length} words, {missing} missing";
                    return true;
                });
                return Done();
            case "cancelLoad":
                cancel = true;
                return Done();
            case "load":
                if (busy) throw new InvalidOperationException("The segmenter is busy.");
                string path = args.Str("path").Trim();
                if (!File.Exists(path)) throw new FileNotFoundException("Select an existing corpus file first.", path);
                // CorpusReader: one phrase per nonempty line, read a phrase at a time
                var lines = File.ReadLines(path).GetEnumerator();
                int loaded = 0;
                s.ExplainLearning = false;
                status = "Reading phrases...";
                Run(int.MaxValue, _ =>
                {
                    while (lines.MoveNext())
                    {
                        string phrase = SequenceSegmenterInput.CorpusPhrase(lines.Current);
                        if (phrase is null) continue;
                        Submit(s, phrase, showPhrase: true);
                        loaded++;
                        status = $"Phrase {loaded}...";
                        return true;
                    }
                    lines.Dispose();
                    status = $"Loaded and processed {loaded} sequence(s) from the file.";
                    return false;
                });
                return Done();
            default: return Unknown(op);
        }
    }

    /// <summary>Runs `step` up to `count` times on a background thread, a step per lock, until it returns false or is cancelled.</summary>
    void Run(int count, Func<int, bool> step)
    {
        busy = true;
        cancel = false;
        int i = 0;
        Watch(UKSNative.Exports.RunLockedWhile(() =>
        {
            try
            {
                if (cancel) { status = "Cancelled."; busy = false; return false; }
                if (i >= count || !step(i++)) { busy = false; return false; }
                return true;
            }
            catch (Exception e) { status = "Error: " + e.Message; busy = false; return false; }
        }), e => { status = e; busy = false; });
    }
}

// ---------- ModuleAddToMentalModelDlg ----------

public sealed class AddToMentalModelState
{
    /// <summary>Thoughts added recently, newest first (kept with the module's saved settings).</summary>
    public List<string> History { get; set; } = new();
}

sealed class AddToMentalModelApi : ModuleApi
{
    const int HistoryLimit = 8;

    static List<string> History(ModuleBase m) =>
        (m.GetSavedDlgAttribute("ThoughtHistory") ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries).ToList();

    public override string State(ModuleBase m) => Json(new AddToMentalModelState { History = History(m) }, ApiJson.Default.AddToMentalModelState);

    public override string Query(ModuleBase m, string op, ApiArgs args) => op switch
    {
        "complete" => Json(Complete(args.Str("prefix"), ThoughtLabels.LabelList.Values.Select(t => t.Label).Where(l => l != "")), ApiJson.Default.ListString),
        _ => base.Query(m, op, args),
    };

    public override string Call(ModuleBase m, string op, ApiArgs args)
    {
        if (op != "add") return Unknown(op);
        string label = args.Str("thought").Trim();
        if (label == "") throw new ArgumentException("Type a Thought.");
        ((ModuleAddToMentalModel)m).AddThoughtToMentalModel(label, args.Num("azimuth"), args.Num("elevation"), args.Num("distance", 1));
        var history = History(m);
        history.RemoveAll(x => string.Equals(x, label, StringComparison.OrdinalIgnoreCase));
        history.Insert(0, label);
        m.SetSavedDlgAttribute("ThoughtHistory", string.Join("|", history.Take(HistoryLimit)));
        return Done();
    }
}

// ---------- ModuleOnlineInfoDlg ----------

public sealed class OutputState
{
    public string Output { get; set; }
}

sealed class OnlineInfoApi : ModuleApi
{
    public override string State(ModuleBase m) => Json(new OutputState { Output = ((ModuleOnlineInfo)m).Output }, ApiJson.Default.OutputState);

    public override string Call(ModuleBase m, string op, ApiArgs args)
    {
        var mcn = (ModuleOnlineInfo)m;
        switch (op)
        {
            case "clear": mcn.Output = ""; return Done();
            case "lookup": Lookup(mcn, args.Str("source", "ChatGPT"), args.Str("link", "is-a"), args.Str("text")); return Done();
            default: return Unknown(op);
        }
    }

    static void Lookup(ModuleOnlineInfo mcn, string source, string link, string txt)
    {
        var qType = link == "hasa" ? ModuleOnlineInfo.QueryType.hasa : ModuleOnlineInfo.QueryType.isa;
        switch (source)
        {
            case "ChatGPT":
                if (txt.EndsWith("?")) Watch(mcn.GetChatGPTResult(txt, ModuleOnlineInfo.QueryType.general), e => mcn.Output = e);
                else if (txt.StartsWith("list some")) Watch(mcn.GetChatGPTResult(txt[10..], ModuleOnlineInfo.QueryType.list), e => mcn.Output = e);
                else if (txt.StartsWith("count some")) Watch(mcn.GetChatGPTResult(txt[11..], ModuleOnlineInfo.QueryType.listCount), e => mcn.Output = e);
                else if (txt.EndsWith("can")) Watch(mcn.GetChatGPTResult(txt[..^3], ModuleOnlineInfo.QueryType.can), e => mcn.Output = e);
                else Watch(mcn.GetChatGPTResult(txt, qType), e => mcn.Output = e);
                break;
            case "ConceptNet": mcn.GetConceptNetData(txt); break;
            case "WikiData": Watch(mcn.GetWikidataData(txt, "subclass of"), e => mcn.Output = e); break;
            case "Wiktionary": Watch(mcn.GetWiktionaryData(txt), e => mcn.Output = e); break;
            case "Free Dictionary": Watch(mcn.GetFreeDictionaryAPIData(txt), e => mcn.Output = e); break;
            case "Webster's Elementary": Watch(mcn.GetWebstersDictionaryAPIData(txt), e => mcn.Output = e); break;
            case "Kid's Definition": mcn.GetKidsDefinition(txt); break;
            case "CSKG": mcn.GetCSKGData(txt); break;
            case "Oxford Word List": mcn.SetupWordList2(txt); break;
            default: throw new ArgumentException($"Unknown source '{source}'.");
        }
    }
}

// ---------- ModuleVisualInputDlg ----------

public sealed class VisualInputState
{
    public List<string> ObservationFiles { get; set; } = new();
    public string SelectedObservation { get; set; }
    public List<string> LessonFiles { get; set; } = new();
    public string SelectedLesson { get; set; }
    public double StepIntervalSeconds { get; set; }
    public bool LessonRunning { get; set; }
    public List<string> LessonSteps { get; set; } = new();
    public int NextStep { get; set; }
    public string CurrentWord { get; set; }
    public string Status { get; set; }
}

sealed class VisualInputApi : ModuleApi
{
    public override string State(ModuleBase m)
    {
        var v = (ModuleVisualInput)m;
        var lessons = v.GetLessonFiles().ToList();
        // the desktop opens with the second lesson selected
        if (string.IsNullOrWhiteSpace(v.SelectedLessonFile) && lessons.Count > 0) v.SelectLessonFile(lessons[Math.Min(1, lessons.Count - 1)]);
        return Json(new VisualInputState
        {
            ObservationFiles = v.GetObservationFiles().ToList(),
            SelectedObservation = v.SelectedObservationFile,
            LessonFiles = lessons,
            SelectedLesson = v.SelectedLessonFile,
            StepIntervalSeconds = v.LessonStepIntervalSeconds,
            LessonRunning = v.IsLessonRunning,
            LessonSteps = v.LessonSteps.ToList(),
            NextStep = v.NextLessonStepIndex,
            CurrentWord = v.CurrentWord,
            Status = v.Status,
        }, ApiJson.Default.VisualInputState);
    }

    public override string Call(ModuleBase m, string op, ApiArgs args)
    {
        var v = (ModuleVisualInput)m;
        double distance = args.Num("distance", 1);
        switch (op)
        {
            case "selectObservation": v.SelectObservationFile(args.Str("file")); break;
            case "present": v.PresentSelected(distance); break;
            case "imagine": v.ImagineWord(args.Str("word", v.CurrentWord), distance); break;
            case "clear": v.ClearPresentation(); break;
            case "selectLesson": v.SelectLessonFile(args.Str("file")); break;
            case "setInterval":
                double interval = args.Num("seconds", v.LessonStepIntervalSeconds);
                if (double.IsFinite(interval) && interval > 0) v.LessonStepIntervalSeconds = interval;
                break;
            case "step": v.StepLesson(); break;
            case "run": v.StartLesson(v.LessonStepIntervalSeconds); break;
            case "pause": v.PauseLesson(); break;
            case "reset": v.ResetLesson(); break;
            default: return Unknown(op);
        }
        return Done();
    }
}

// ---------- ModuleWordDlg ----------

public sealed class StatusState
{
    public string Status { get; set; }
    public bool Busy { get; set; }
}

sealed class WordApi : ModuleApi
{
    string typed = "";

    public override string State(ModuleBase m) => Json(new StatusState { Status = status, Busy = busy }, ApiJson.Default.StatusState);

    public override string Query(ModuleBase m, string op, ApiArgs args)
    {
        if (op != "suggest") return base.Query(m, op, args);
        // the spelling the module would complete `prefix` to, if it differs from it
        string prefix = args.Str("prefix");
        var result = new List<string>();
        if (prefix != "")
        {
            string s = ((ModuleWord)m).GetWordSuggestion(prefix);
            if (s is not null) s = ThoughtLabels.GetThought("w:" + s)?.Label;
            if (s is not null && s.Length > 2 && !s[2..].Equals(prefix, StringComparison.OrdinalIgnoreCase)) result.Add(s[2..]);
        }
        return Json(result, ApiJson.Default.ListString);
    }

    public override string Call(ModuleBase m, string op, ApiArgs args)
    {
        var w = (ModuleWord)m;
        switch (op)
        {
            case "typed":
                // the desktop feeds each typed letter to the module as it arrives
                string text = args.Str("text");
                if (text.StartsWith(typed)) { if (text.Length > typed.Length) w.EnqueueLetters(text[typed.Length..]); }
                else w.RebuildQueueFromCurrentText(text);
                typed = text;
                return Done();
            case "add":
                string word = args.Str("word").Trim();
                if (word == "") throw new ArgumentException("Please enter a word.");
                w.AddWordSpelling(word);
                status = $"Spelling added: {word}";
                return Message(status);
            case "load":
                string path = args.Str("path").Trim();
                if (!File.Exists(path)) throw new FileNotFoundException("File not found.", path);
                Background(() => $"Successfully loaded {w.LoadWordsFromFile(path)} word(s) from file.");
                return Done();
            default: return Unknown(op);
        }
    }
}

// ---------- ModuleAlgorithmDlg ----------

public sealed class AlgorithmState
{
    public string LastAction { get; set; }
    public List<string> Tasks { get; set; } = new();
    /// <summary>A task was started in single-step mode and has steps left.</summary>
    public bool CanStep { get; set; }
}

sealed class AlgorithmApi : ModuleApi
{
    bool canStep;

    public override string State(ModuleBase m)
    {
        var a = (ModuleAlgorithm)m;
        return Json(new AlgorithmState { LastAction = a.LastAction, Tasks = Tasks(a.theUKS), CanStep = canStep }, ApiJson.Default.AlgorithmState);
    }

    internal static List<string> Tasks(UKS.UKS uks) =>
        uks?.Labeled("Task")?.Children.Select(t => t.Label).Where(l => !l.Equals("variable", StringComparison.OrdinalIgnoreCase)).ToList() ?? new();

    public override string Call(ModuleBase m, string op, ApiArgs args)
    {
        var a = (ModuleAlgorithm)m;
        string task = args.Str("task").Trim(), p1 = args.Str("param1").Trim(), p2 = args.Str("param2").Trim();
        switch (op)
        {
            case "execute":
                if (task == "") throw new ArgumentException("No task specified");
                a.IsSingleStepMode = false;
                return Message(a.ExecuteTask(task, p1, p2, false) ? $"Executing task: {task}" : a.LastAction);
            case "start":
                if (task == "") throw new ArgumentException("No task specified");
                a.IsSingleStepMode = true;
                if (!a.ExecuteTask(task, p1, p2, false)) throw new InvalidOperationException("Failed to start task");
                canStep = true;
                return Message($"Task ready: {task}");
            case "step":
                if (a.LastAction.StartsWith("TASK COMPLETE")) canStep = false;
                a.Step();
                return Message(a.LastAction);
            default: return Unknown(op);
        }
    }
}

// ---------- ModuleTextDlg (also ModuleTextIn), ModuleTextNEWDlg ----------

public sealed class TextState
{
    public string Status { get; set; }
    public bool Busy { get; set; }
}

public sealed class TextAnswer
{
    public string Answer { get; set; }
    public string Status { get; set; }
    /// <summary>How the input was understood ([from → type → to]), when it was.</summary>
    public string Interpretation { get; set; }
    /// <summary>The Thought it was about.</summary>
    public string Thought { get; set; }
}

public sealed class AttributeDto
{
    public string Relation { get; set; }
    public string Target { get; set; }
    public float Confidence { get; set; }
}

sealed class TextApi<T> : ModuleApi where T : ModuleBase
{
    readonly Func<T, string, (string answer, Link relationship, string status)> submit;
    readonly Func<Thought, Link> bestMeaning;
    readonly Func<Thought, string> bestWord;
    readonly Func<T, string, string> load;
    readonly Action<T> cancelLoad;

    public TextApi(Func<T, string, (string, Link, string)> submit, Func<Thought, Link> bestMeaning, Func<Thought, string> bestWord,
        Func<T, string, string> load, Action<T> cancelLoad)
    {
        this.submit = submit; this.bestMeaning = bestMeaning; this.bestWord = bestWord; this.load = load; this.cancelLoad = cancelLoad;
    }

    public static string LoadStatus(int count, string last, int retained, int loaded, string missing)
    {
        string result = last.StartsWith("Error:", StringComparison.Ordinal)
            ? $"Loaded {count} phrase(s). {last}"
            : $"Loaded {count} phrase(s): {retained} of {loaded} annotated exemplar(s) retained.";
        if (!string.IsNullOrEmpty(missing)) result += " Missing: " + missing;
        return result;
    }

    public override string State(ModuleBase m) => Json(new TextState { Status = status, Busy = busy }, ApiJson.Default.TextState);

    public override string Query(ModuleBase m, string op, ApiArgs args) => op switch
    {
        "attributes" => Json(Attributes(m.theUKS, args.Str("thought").Trim()), ApiJson.Default.ListAttributeDto),
        _ => base.Query(m, op, args),
    };

    public override string Call(ModuleBase m, string op, ApiArgs args)
    {
        var t = (T)m;
        switch (op)
        {
            case "submit":
                var (answer, relationship, result) = submit(t, args.Str("text").Trim());
                status = result;
                string interpretation = null;
                if (relationship is not null)
                {
                    interpretation = relationship.ToString();
                    Thought filterTarget = relationship.GetTargetOfFirstLinkOfType("filterBy");
                    if (filterTarget is not null) interpretation = $"[{interpretation}→filterBy→{filterTarget.Label}]";
                }
                return Json(new TextAnswer { Answer = answer, Status = result, Interpretation = interpretation, Thought = relationship?.From?.Label },
                    ApiJson.Default.TextAnswer);
            case "cancelLoad": cancelLoad(t); return Done();
            case "load":
                string path = args.Str("path").Trim();
                if (!File.Exists(path)) throw new FileNotFoundException("Select an existing phrase file first.", path);
                Background(() => load(t, path));
                return Done();
            default: return Unknown(op);
        }
    }

    /// <summary>What is known about a word's best meaning (or a Thought by label).</summary>
    List<AttributeDto> Attributes(UKS.UKS uks, string raw)
    {
        var result = new List<AttributeDto>();
        if (uks is null || raw == "") return result;
        Thought word = uks.Labeled("w:" + raw.ToLowerInvariant());
        Thought thought = bestMeaning(word)?.To ?? uks.Labeled(raw);
        if (thought is null) return result;
        IPluralize pluralizer = new Pluralizer();
        foreach (Link attribute in uks.GetAttributes(thought))
        {
            string typeText = attribute.LinkType?.Label ?? "";
            bool isPlural = false;
            int dot = typeText.IndexOf('.');
            if (dot >= 0)
            {
                if (int.TryParse(typeText[(dot + 1)..], out int count) && count > 1) isPlural = true;
                typeText = typeText.Replace('.', ' ');
            }
            string targetText = bestWord(attribute.To);
            if (string.IsNullOrWhiteSpace(targetText)) targetText = attribute.To?.Label ?? "";
            if (isPlural) targetText = pluralizer.Pluralize(targetText);
            result.Add(new AttributeDto
            {
                Relation = typeText, Target = targetText,
                Confidence = float.IsNaN(attribute.Weight) ? 0 : Math.Clamp(attribute.Weight, 0, 1),
            });
        }
        return result;
    }
}

// ---------- ModuleGPTInfoDlg ----------

public sealed class GPTInfoState
{
    public string Output { get; set; }
    public int TokensUsed { get; set; }
    public int UnknownCount { get; set; }
}

sealed class GPTInfoApi : ModuleApi
{
    const int wordMax = 100;
    volatile string output = "";

    public override string State(ModuleBase m) =>
        Json(new GPTInfoState { Output = output, TokensUsed = GPT.totalTokensUsed, UnknownCount = m.theUKS?.Labeled("Unknown")?.Children.Count ?? 0 },
            ApiJson.Default.GPTInfoState);

    public override string Call(ModuleBase m, string op, ApiArgs args)
    {
        string txt = args.Str("text");
        switch (op)
        {
            case "info":
                output = "Working...";
                Watch(Task.Run(async () =>
                {
                    await ModuleGPTInfo.GetChatGPTData(txt);
                    output = ModuleGPTInfo.Output + $"\nTotal link count: {ModuleGPTInfoDlg.linkCount}. Total error count (not accepted): {ModuleGPTInfoDlg.errorCount}.";
                }), e => output = e);
                break;
            case "parents":
                output = "Working...";
                Watch(Task.Run(async () => { await ModuleGPTInfo.GetChatGPTParents(txt); output = ModuleGPTInfo.Output; }), e => output = e);
                break;
            case "clear": output = ""; break;
            case "reparse": Reset(); ModuleGPTInfo.ParseGPTOutput(txt, output); break;
            case "verify": Reset(); Watch(VerifyAll(m), e => output = e); break;
            case "clauses":
                Reset(); output = "Working...";
                Watch(Task.Run(async () => { await ModuleGPTInfo.GetChatGPTClauses(); output = $"Total clause count: {ModuleGPTInfoDlg.linkCount}. Total error count (not accepted): {ModuleGPTInfoDlg.errorCount}."; }), e => output = e);
                break;
            case "ambiguity":
                Reset(); output = "Working...";
                Watch(Task.Run(async () => { await ModuleGPTInfo.DisambiguateTerms(); output = $"Total disambiguity success count: {ModuleGPTInfoDlg.linkCount}. Total error count (not accepted): {ModuleGPTInfoDlg.errorCount}."; }), e => output = e);
                break;
            case "duplicates":
                Reset(); output = "Working...";
                Watch(Task.Run(async () => { await ModuleGPTInfo.SolveDuplicates(); output = $"Done! Duplicates resolved: {ModuleGPTInfoDlg.linkCount}."; }), e => output = e);
                break;
            case "unknowns":
                Reset();
                var words = m.theUKS.Labeled("Unknown").Children.Take(wordMax).Select(t => t.Label).ToList();
                output = $"Getting parents for {words.Count} Thoughts";
                Watch(Process(words, ModuleGPTInfo.GetChatGPTParents, "Done processing unknowns!"), e => output = e);
                break;
            case "loadWords": Reset(); Watch(Process(ReadWords(args.Str("path")), ModuleGPTInfo.GetChatGPTData, "Done running!"), e => output = e); break;
            case "loadAmbiguity": Reset(); Watch(Process(ReadWords(args.Str("path")), ModuleGPTInfo.DisambiguateTermsFile, "Done running!"), e => output = e); break;
            default: return Unknown(op);
        }
        return Done();
    }

    static void Reset() { ModuleGPTInfoDlg.errorCount = 0; ModuleGPTInfoDlg.linkCount = 0; }

    List<string> ReadWords(string path)
    {
        var words = new List<string>();
        var pluralizer = new Pluralizer();
        try
        {
            foreach (string line in File.ReadLines(path))
            {
                if (words.Count >= wordMax) break;
                words.AddRange(line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Select(w => pluralizer.Singularize(w)));
            }
            output = "File Successfully Read!";
        }
        catch (IOException e) { output = "An error occurred while reading the file:\n" + e.Message; }
        return words;
    }

    async Task Process(List<string> words, Func<string, Task> request, string done)
    {
        output = $"Running through words... Word count is: {words.Count}.";
        var pending = words.Where(w => w.Trim() != "").Select(w => request(w.Trim())).ToList();
        await Task.WhenAll(pending);
        output = $"{done} Total word count: {words.Count}. Total link count: {ModuleGPTInfoDlg.linkCount}. Total error count (not accepted): {ModuleGPTInfoDlg.errorCount}.";
    }

    async Task VerifyAll(ModuleBase m)
    {
        output = "Verifying all is-a links";
        int count = 0;
        var pending = new List<Task>();
        foreach (Thought t in m.theUKS.AtomicThoughts.ToList())
        {
            if (t.Parents.FirstOrDefault(x => x.Label == "Unknown") is not null) continue;
            if (!t.Label.StartsWith('.') || t.Label == ".") continue;
            foreach (Link r in t.LinksTo.Where(r => r.LinkType?.Label == "has-child"))
            {
                count++;
                pending.Add(ModuleGPTInfo.GetChatGPTVerifyParentChild(r.To.Label, t.Label));
            }
        }
        await Task.WhenAll(pending);
        output = $"Done verifying is-a links for reasonableness. Checked {count} links.";
    }
}

// ---------- ModuleShowGraphDlg ----------

public sealed class ShowGraphState
{
    /// <summary>The UKS dialog's Root: the Thought whose descendants are drawn.</summary>
    public string Root { get; set; }
}

sealed class ShowGraphApi : ModuleApi
{
    public override string State(ModuleBase m) => Json(new ShowGraphState { Root = RootOf(m) }, ApiJson.Default.ShowGraphState);

    static string RootOf(ModuleBase m)
    {
        string root = "Object";
        Thought uksDlg = m.theUKS.Labeled("ModuleUKS0");
        if (uksDlg is not null)
            foreach (var r in uksDlg.LinksTo)
                if (r.LinkType?.Label == "hasAttribute" && r.To?.Label.StartsWith("Root") == true && r.To.V is string s)
                    root = s;
        return root;
    }

    public override string Query(ModuleBase m, string op, ApiArgs args) => op switch
    {
        "graph" => Json(Graph(m.theUKS, args.Str("root", RootOf(m))), UKSNative.JsonCtx.Default.GraphDto),
        _ => base.Query(m, op, args),
    };

    /// <summary>Same edges as DrawTheGraph: every link of every descendant of the root.</summary>
    internal static UKSNative.GraphDto Graph(UKS.UKS uks, string rootLabel)
    {
        var graph = new UKSNative.GraphDto { Root = rootLabel };
        Thought root = uks.Labeled(rootLabel);
        if (root is null) return graph;
        var nodes = new HashSet<string>();
        void Node(string label)
        {
            if (nodes.Add(label)) graph.Nodes.Add(new UKSNative.GraphNodeDto { Id = label, Label = label });
        }
        foreach (Thought t in root.Descendants)
            foreach (Link r in t.LinksTo)
            {
                if (r.From == root || r.To is null || r.LinkType is null) continue;
                if (graph.Edges.Count >= 400) { graph.Truncated = true; return graph; }
                Node(t.Label); Node(r.To.Label);
                graph.Edges.Add(new UKSNative.GraphEdgeDto
                {
                    Source = t.Label, Target = r.To.Label, LinkType = r.LinkType.Label,
                    IsA = r.LinkType.Label == "is-a", Weight = r.Weight,
                });
            }
        return graph;
    }
}
