using System.Collections.Concurrent;
using Xunit;

namespace UKS.Tests;

/// <summary>
/// Several threads use one store at once, as the clock thread, the engine and the UI do:
/// writers add and delete Thoughts, links and sequences, readers walk the store, and the clock
/// expires Thoughts and links. Afterwards the store's structure is checked for consistency.
/// Known to fail today: it documents the thread-safety gaps rather than guarding a fix.
/// Run it alone with --filter "Category=MultiThread"; exclude it with "Category!=MultiThread".
/// </summary>
[Trait("Category", "MultiThread")]
public class UKSMultiThreadTests
{
    private const int LabelPool = 150;               // small, so threads collide on the same Thoughts
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(3);

    [Fact]
    public void ConcurrentWritersReadersAndTheClockLeaveTheStoreConsistent()
    {
        var store = new UKS();
        store.CreateInitialStructure();
        Thought objectRoot = store.Labeled("Object")!;
        Thought has = store.GetOrAddThought("has", "LinkType");
        Thought spelled = store.GetOrAddThought("spelled", "LinkType");
        List<Thought> letters = "abcdef".Select(c => store.GetOrAddThought("letter" + c, "Object")).ToList();

        var problems = new ConcurrentQueue<string>();
        var sequences = new ConcurrentDictionary<SeqElement, byte>(ReferenceEqualityComparer.Instance);
        using var stop = new CancellationTokenSource(Duration);
        var threads = new List<Thread>();

        void Run(string name, int seed, Action<Random> step) => threads.Add(new Thread(() =>
        {
            UKS.theUKS = store;
            var random = new Random(seed);
            while (!stop.IsCancellationRequested)
            {
                try { step(random); }
                catch (Exception e) { problems.Enqueue($"{name}: {e.GetType().Name}: {e.Message} at {TopFrame(e)}"); }
            }
        }) { IsBackground = true, Name = name });

        Thought RandomThought(Random random) => store.GetOrAddThought("mt" + random.Next(LabelPool), objectRoot);

        // Writers.
        Run("thoughts", 1, random =>
        {
            Thought t = RandomThought(random);
            if (random.Next(3) == 0) t.Delete();
            else if (random.Next(10) == 0) t.TimeToLive = random.Next(1, 20); // the clock will expire it
        });
        Run("links", 2, random =>
        {
            Thought from = RandomThought(random), to = RandomThought(random);
            if (random.Next(2) == 0)
            {
                Link? link = from.AddLink(has, to);
                if (link is not null && random.Next(5) == 0) link.TimeToLive = random.Next(1, 20);
            }
            else if (from.HasLink(has, to) is Link existing) from.RemoveLink(existing);
        });
        Run("sequences", 3, random =>
        {
            Thought owner = RandomThought(random);
            if (random.Next(3) > 0)
            {
                List<Thought> values = Enumerable.Range(0, random.Next(2, 6)).Select(_ => letters[random.Next(letters.Count)]).ToList();
                if (store.AddSequenceAndLink(owner, spelled, values) is SeqElement start) sequences.TryAdd(start, 0);
            }
            else if (sequences.Keys.Skip(random.Next(Math.Max(1, sequences.Count))).FirstOrDefault() is SeqElement doomed)
            {
                sequences.TryRemove(doomed, out _);
                DeleteOwnedSequence(store, doomed);
            }
        });

        // Readers.
        Run("walker", 4, random =>
        {
            foreach (Thought t in objectRoot.Descendants)
            {
                foreach (Link link in t.LinksTo) _ = link.To?.Label;
                _ = t.Parents.Count;
            }
            _ = store.Roots().Count + store.ThoughtCount() + store.LinkCount();
        });
        Run("queries", 5, random =>
        {
            if (store.Labeled("mt" + random.Next(LabelPool)) is Thought t) _ = store.GetAttributes(t);
            if (sequences.Keys.FirstOrDefault() is SeqElement start) _ = store.FlattenSequence(start);
        });

        store.StartClock();
        threads.ForEach(thread => thread.Start());
        threads.ForEach(thread => thread.Join());
        store.StopClock();
        Thread.Sleep(50); // let a clock cycle in progress finish

        foreach (string problem in StructureProblems(store)) problems.Enqueue("after: " + problem);

        // Group identical problems so the report stays readable.
        List<string> report = problems.GroupBy(p => p).OrderByDescending(g => g.Count())
            .Take(15).Select(g => $"{g.Count()} × {g.Key}").ToList();
        Assert.True(problems.IsEmpty, $"{problems.Count} problems ({problems.Distinct().Count()} distinct):\n" + string.Join("\n", report));
    }

    [Fact]
    public void TheStructureCheckPassesWhenOneThreadDoesTheSameWork()
    {
        var store = new UKS();
        store.CreateInitialStructure();
        Thought objectRoot = store.Labeled("Object")!;
        Thought has = store.GetOrAddThought("has", "LinkType");
        Thought spelled = store.GetOrAddThought("spelled", "LinkType");
        List<Thought> letters = "abcdef".Select(c => store.GetOrAddThought("letter" + c, "Object")).ToList();
        var random = new Random(7);
        var sequences = new List<SeqElement>();
        for (int i = 0; i < 3000; i++)
        {
            Thought a = store.GetOrAddThought("mt" + random.Next(LabelPool), objectRoot);
            Thought b = store.GetOrAddThought("mt" + random.Next(LabelPool), objectRoot);
            switch (random.Next(5))
            {
                case 0: a.Delete(); break;
                case 1: a.AddLink(has, b); break;
                case 2: if (a.HasLink(has, b) is Link link) a.RemoveLink(link); break;
                case 3:
                    if (store.AddSequenceAndLink(a, spelled, Enumerable.Range(0, random.Next(2, 6))
                        .Select(_ => letters[random.Next(letters.Count)]).ToList()) is SeqElement start) sequences.Add(start);
                    break;
                case 4:
                    if (sequences.Count > 0) { DeleteOwnedSequence(store, sequences[^1]); sequences.RemoveAt(sequences.Count - 1); }
                    break;
            }
            if (random.Next(10) == 0) a.TimeToLive = random.Next(1, 20);
            store.AdvanceCycle();
        }

        Assert.Empty(StructureProblems(store));
    }

    // Every link must be attached at all three ends, and refer only to Thoughts still in the store.
    private static IEnumerable<string> StructureProblems(UKS store)
    {
        List<Thought> thoughts;
        lock (store.AtomicThoughts) thoughts = store.AtomicThoughts.Where(t => t is not Link).ToList();
        var inStore = new HashSet<Thought>(thoughts, ReferenceEqualityComparer.Instance);
        // Sequence elements are not in the Thought set by design; deleting a sequence removes their labels.
        bool Exists(Thought t) => t is SeqElement ? ReferenceEquals(store.Labeled(t.Label), t) : inStore.Contains(t);
        foreach (Thought t in thoughts)
        {
            foreach (Link link in t.LinksTo)
            {
                if (!ReferenceEquals(link.From, t)) yield return $"{link} is in {t.Label}'s LinksTo but comes from {link.From?.Label}";
                if (link.LinkType is Thought type && !type.LinksAsType.Contains(link)) yield return $"{link} is missing from its type's LinksAsType";
                if (link.To is Thought to && to is not Link && !to.LinksFrom.Contains(link)) yield return $"{link} is missing from its target's LinksFrom";
                if (link.LinkType is Thought lt && !inStore.Contains(lt)) yield return $"{link} uses a deleted link type";
                if (link.To is Thought target && target is not Link && !Exists(target)) yield return $"{link} points to deleted {target.Label}";
            }
            if (t.Label != "" && !ReferenceEquals(store.Labeled(t.Label), t)) yield return $"{t.Label} is in the store but its label finds another Thought";
        }
        foreach (Thought labeled in store.Labels.Values)
            if (labeled is not Link && !Exists(labeled)) yield return $"label {labeled.Label} finds a deleted Thought";
    }

    // DeleteSequence leaves the owner's link pointing at the deleted elements, so remove it first, as Thought.Delete does.
    private static void DeleteOwnedSequence(UKS store, SeqElement start)
    {
        foreach (Link owner in start.LinksFrom.Where(link => link.LinkType?.Label != "FRST"))
            owner.From?.RemoveLink(owner);
        store.DeleteSequence(start);
    }

    private static string TopFrame(Exception e) =>
        e.StackTrace?.Split('\n').FirstOrDefault(line => line.Contains(" in "))?.Trim() ?? "?";
}
