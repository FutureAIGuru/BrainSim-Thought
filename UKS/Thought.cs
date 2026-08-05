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

using System.Diagnostics;

namespace UKS;

public class Link : Thought
{
    /// <summary>
    /// Default constructor for a link thought.
    /// </summary>
    public Link() { }

    /// <summary>
    /// Creates a link with the specified source, link type, and target.
    /// </summary>
    /// <param name="from">Source thought.</param>
    /// <param name="linkType">Relationship type thought.</param>
    /// <param name="to">Target thought.</param>
    public Link(Thought from, Thought linkType, Thought to)
    {
        From = from;
        LinkType = linkType;
        To = to;
    }

    public Thought? From { get; set; }
    public Thought? LinkType { get; set; }
    public Thought? To { get; set; }

    /// <summary>Query-time metadata: 0 = asserted on the query source; N = inherited via N is-a hops.</summary>
    public int InheritanceDepth { get; set; }

    /// <summary>Ch.5 provenance: category Thought where an inherited link was found (e.g. dog for Fido→fur).</summary>
    public Thought? InheritedFromCategory { get; set; }

    /// <summary>
    /// Returns a formatted string for the link, showing sequence notation or From→Type→To.
    /// </summary>
    public override string ToString()
    {
        string retVal = Label + "[";
        if (From is not null) retVal += From.ToString();
        if (LinkType is not null) retVal += ((retVal == "") ? "" : "→") + LinkType.ToString();
        if (To is not null) retVal += ((retVal == "") ? "" : "→") + To.ToString();
        retVal += "]";
        return retVal;
    }
}


/// <summary>
/// A Thought is an atomic unit of thought. In the lexicon of graphs, a Thought is both a "node" and an Edge.  
/// A Thought can represent anything, physical object, attribute, word, action, feeling, etc.
/// </summary>
public partial class Thought
{
    private static readonly Queue<Thought> recentlyFired = new();
    private static readonly object recentlyFiredLock = new();

    public static Thought IsA => ThoughtLabels.GetThought("is-a")!;  //this is a cache value shortcut for (Thought)"is-a"
    private readonly List<Link> _linksTo = new();   // links to "has", "is", is-a, many others
    private readonly List<Link> _linksFrom = new(); // links from
    private readonly List<Link> _linksAsType = new(); // links which use this Thought as their LinkType

    /// <summary>Unsafe writeable list of outgoing links.</summary>
    public List<Link> LinksToWriteable { get => _linksTo; }
    /// <summary>Unsafe writeable list of incoming links.</summary>
    public List<Link> LinksFromWriteable { get => _linksFrom; }
    internal List<Link> LinksAsTypeWriteable { get => _linksAsType; }
    /// <summary>Safe snapshot of outgoing links.</summary>
    //public IReadOnlyList<Link> LinksTo { get { lock (_linksTo) { return new List<Link>(_linksTo.AsReadOnly()); } } }
    public IReadOnlyList<Link> LinksTo
    {
        get
        { 
            if (CheckExpiredAndMaybeDelete()) return Array.Empty<Link>();
            lock (_linksTo) { return new List<Link>(_linksTo.AsReadOnly()); }
        }
    }
    /// <summary>Safe snapshot of incoming links.</summary>
    /// 
    public IReadOnlyList<Link> LinksFrom { get { lock (_linksFrom) { return new List<Link>(_linksFrom.AsReadOnly()); } } }
    /// <summary>Direct parents (targets of outgoing is-a links).</summary>
    public IReadOnlyList<Thought> Parents { get { lock (_linksTo) return _linksTo.Where(x => x.LinkType?.Label == "is-a").Select(x => x.To).OfType<Thought>().ToList(); } }
    /// <summary>Direct children (sources of incoming is-a links).</summary>
    public IReadOnlyList<Thought> Children { get { lock (_linksFrom) return _linksFrom.Where(x => x.LinkType?.Label == "is-a").Select(x => x.From).OfType<Thought>().ToList(); } }

    private string _label = "";
    public string Label
    {
        get => _label;
        set
        {
            if (value == _label) return;
            ThoughtLabels.RemoveThoughtLabel(_label);
            _label = ThoughtLabels.AddThoughtLabel(value, this);
        }
    }


    public void Delete()
    {
        // A Link is also a Thought. If it is still installed in the graph, detach
        // it first; RemoveLink calls back here after the detachment is complete.
        if (this is Link link && link.From is not null)
        {
            bool isAttached;
            lock (link.From._linksTo)
                isAttached = link.From._linksTo.Any(candidate => ReferenceEquals(candidate, link));
            if (isAttached)
            {
                link.From.RemoveLink(link);
                return;
            }
        }

        List<Thought> replacementParents = this is Link ? new() : Parents.ToList();
        List<Thought> childrenToReparent = this is Link ? new() : Children.ToList();

        if (this is not Link)
        {
            UKS.theUKS.DeleteSequencesContainingValue(this);

            foreach (Link typedLink in _linksAsType.ToList())
                typedLink.From?.RemoveLink(typedLink);
        }

        foreach (Link r in _linksTo
            .Where(candidate => candidate.To is SeqElement seq && ReferenceEquals(seq.FRST, seq))
            .ToList())
        {
            if (r.To is SeqElement sequence)
                UKS.theUKS.DeleteSequence(sequence);
        }

        foreach (Link r in _linksTo.ToList())
            RemoveLink(r);

        foreach (Link r in _linksFrom.ToList())
            (r.From ?? this).RemoveLink(r);

        foreach (Thought child in childrenToReparent)
        {
            foreach (Thought parent in replacementParents)
                if (!ReferenceEquals(child, parent) && !ReferenceEquals(parent, this))
                    child.AddParent(parent);

            if (child.Parents.Count == 0 && ThoughtLabels.GetThought("Unknown") is Thought unknown)
                child.AddParent(unknown);
        }

        DeleteFromRecentlyFired(this);
        ThoughtLabels.RemoveThoughtLabel(Label);
        lock (UKS.theUKS.AtomicThoughts)
            UKS.theUKS.AtomicThoughts.Remove(this);
    }

    private bool CheckExpiredAndMaybeDelete()
    {
        for (int i = 0; i < _linksTo.Count; i++)
        {
            if (_linksTo[i].CheckExpiredAndMaybeDelete())
                i --;
        }
        if (_timeToLive == TimeSpan.MaxValue) return false;
        if (_timeToLive > DateTime.MaxValue - LastFiredTime) return false; // Effectively never expires
        if (LastFiredTime + _timeToLive > DateTime.Now) return false;

        //Debug.WriteLine($"Thought forgotten: {this.ToString()}");
        // Expired: remove self
        if (this is Link l)
        {
            l.From?.RemoveLink(l);
        }
        else
        {
            Delete();
        }
        return true;
    }

    public DateTime LastFiredTime = DateTime.MinValue;

    private TimeSpan _timeToLive = TimeSpan.MaxValue;
    /// <summary>Makes a Thought transient when set to a finite time.</summary>
    public TimeSpan TimeToLive
    {
        get => _timeToLive;
        set
        {
            _timeToLive = value;
        }
    }

    private object? _value;
    /// <summary>Any serializable object can be attached to a Thought. ONLY STRINGS are supported for save/restore to disk file.</summary>
    public object? V
    {
        get => _value;
        set { _value = value; }
    }

    private float _weight = 1;
    /// <summary>Weight of this Thought (for links, applies to the link).</summary>
    public float Weight
    {
        get => _weight;
        set => _weight = value;
    }
    private float _maxWeight = 1;
    /// <summary>maxWeight of this Thought (for links, applies to the link).</summary>
    public float maxWeight
    {
        get => _maxWeight;
        set => _maxWeight = value;
    }

    /// <summary>
    /// True when ordinary learning and forgetting are allowed to change this
    /// Thought's persistent weight. Structural Thoughts and Links are not
    /// plastic by default.
    /// </summary>
    public bool isPlastic { get; set; }

    /// <summary>
    /// Default constructor.
    /// </summary>
    public Thought() { }

    /// <summary>
    /// Copy constructor. For link thoughts, construct a Link instead.
    /// </summary>
    /// <param name="r">Thought to copy.</param>
    public Thought(Thought r)
    {
        // Copy only common fields; link-specific fields handled via Link subclass.
        if (r is Link)
            return;
        Weight = r.Weight;
        maxWeight = r.maxWeight;
        isPlastic = r.isPlastic;
        V = r.V;
        Label = r.Label;
    }

    /// <summary>
    /// Returns a Thought's label with attached value if present.
    /// </summary>
    public override string ToString()
    {
        string retVal = Label.Trim();
        if (V is not null)
            retVal += "_V:" + V.ToString();

        return retVal;
    }

    /// <summary>
    /// Allows implicit conversion from a label string to an existing Thought (or null if not found).
    /// </summary>
    public static implicit operator Thought?(string label) => ThoughtLabels.GetThought(label);

    /// <summary>
    /// Equality by label; for Link, also compares endpoints and link type.
    /// </summary>
    public override bool Equals(object? obj)
    {
        if (obj is Thought t)
        {
            if (Label != t.Label) return false;
            if (this is Link l1 && t is Link l2)
            {
                return l1.From == l2.From && l1.LinkType == l2.LinkType && l1.To == l2.To;
            }
            if (this is not Link && t is not Link)
                return true;
        }
        return false;
    }

    public override int GetHashCode()
    {
        if (this is Link link)
            return HashCode.Combine(Label, link.From, link.LinkType, link.To);
        return Label.GetHashCode(StringComparison.Ordinal);
    }

    public static bool operator ==(Thought? a, Thought? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        if (a.Label != "" && a.Label == b.Label) return true;
        if (a is Link la && b is Link lb)
            return la.From == lb.From && la.To == lb.To && la.LinkType == lb.LinkType;
        return false;
    }
    public static bool operator !=(Thought? a, Thought? b) => !(a == b);

    /// <summary>
    /// Assigns a default label to a link thought when missing.
    /// </summary>
    public Thought AddDefaultLabel()
    {
        if (this is not Link l || l.LinkType is null) return this;
        if (string.IsNullOrEmpty(Label))
            Label = "R*";
        return this;
    }

    /// <summary>
    /// Ancestors including self (BFS).
    /// </summary>
    public IEnumerable<Thought> AncestorsWithSelf
    {
        get
        {
            yield return this;
            foreach (var ancestor in Ancestors)
                yield return ancestor;
        }
    }

    /// <summary>
    /// Breadth-first ancestors (excluding self).
    /// </summary>
    public IEnumerable<Thought> Ancestors
    {
        get
        {
            var queue = new Queue<Thought>(Parents);
            var seen = new HashSet<Thought>();

            while (queue.Count > 0)
            {
                var parent = queue.Dequeue();
                if (parent is null || !seen.Add(parent)) continue;

                yield return parent;

                foreach (var gp in parent.Parents)
                    queue.Enqueue(gp);
            }
        }
    }

    /// <summary>
    /// Breadth-first descendants.
    /// </summary>
    public IEnumerable<Thought> Descendants
    {
        get
        {
            var queue = new Queue<Thought>(Children);
            var seen = new HashSet<Thought>();

            while (queue.Count > 0)
            {
                var child = queue.Dequeue();
                if (child is null || !seen.Add(child)) continue;

                yield return child;

                foreach (var gc in child.Children)
                    queue.Enqueue(gc);
            }
        }
    }

    /// <summary>
    /// Determines whether this thought has the specified ancestor (self-inclusive).
    /// </summary>
    /// <param name="t">Ancestor to test.</param>
    public bool HasAncestor(string label)
    {
        Thought? t = ThoughtLabels.GetThought(label);
        return t is not null && HasAncestor(t);
    }

    public bool HasAncestor(Thought? t)
    {
        if (t is null) return false;
        foreach (var ancestor in AncestorsWithSelf)
            if (ancestor == t) return true;
        return false;
    }

    /// <summary>
    /// Updates the last-fired time on a Thought.
    /// </summary>
    public void Fire()
    {
        if (UKS.theUKS?.SuppressFiring == true)
            return;
            
        LastFiredTime = DateTime.Now;
        AddToRecentlyFired();
        UpdateTimeToLive();
    }

    private void AddToRecentlyFired()
    {
        int maxCount = 100;
        lock (recentlyFiredLock)
        {
            recentlyFired.Enqueue(this);
            while (recentlyFired.Count > maxCount) _ = recentlyFired.Dequeue();
        }
    }
    public static void DeleteFromRecentlyFired(Thought t)
    {
        lock (recentlyFiredLock)
        {
            var tempList = recentlyFired.Where(x => x != t).ToList();
            recentlyFired.Clear();
            foreach (var item in tempList)
                recentlyFired.Enqueue(item);
        }
    }
    public static IReadOnlyList<Thought> GetRecentlyFiredThoughts(TimeSpan recency)
    {
        //remove duplicates while preserving order (keeping the most recent occurrence of each Thought)
        DateTime cutoff = DateTime.MinValue;
        if (recency < DateTime.Now-DateTime.MinValue) cutoff =    DateTime.Now - recency;
        Thought[] snapshot;
        lock (recentlyFiredLock)
            snapshot = recentlyFired.Where(x=>x.LastFiredTime > cutoff).ToArray();
        var seen = new HashSet<Thought>();
        var resultRev = new List<Thought>();
        foreach (var item in snapshot.Reverse())
        {
            if (seen.Add(item))
                resultRev.Add(item); // keep first time we see it from the back (i.e., the last occurrence)
        }
        return resultRev;
    }
    public static void FireAllRecentlyFiredThoughts(TimeSpan recency)
    {
        DateTime cutoff = DateTime.Now - recency;
        List<Thought> resultRev;
        lock (recentlyFiredLock)
        {
            var snapshot = recentlyFired.ToArray();
            var seen = new HashSet<Thought>();
            resultRev = new List<Thought>();

            foreach (var item in snapshot.Reverse())
            {
                if (seen.Add(item))
                    resultRev.Add(item);
            }
            resultRev.Reverse();
            recentlyFired.Clear();
            foreach (var item in resultRev)
                recentlyFired.Enqueue(item);
        }

        foreach (Thought t in resultRev.Where(x => x.LastFiredTime > cutoff))
        {
            t.Fire();
        }
    }
    public static void ClearRecentlyFiredQueue()
    {
        lock (recentlyFiredLock)
            recentlyFired.Clear();
    }

    private void UpdateTimeToLive()
    {
        float baseSeconds = 10;
        float growthFactor = 2;
        float maxSeconds = 60 * 60 * 24; //1 day
        double ttlSeconds = baseSeconds * Math.Pow(growthFactor, 1);
        ttlSeconds = Math.Min(ttlSeconds, maxSeconds);
        // Prevent overflow of TimeSpan when extending
        var increment = TimeSpan.FromSeconds(ttlSeconds);
        var remainingToMax = TimeSpan.MaxValue - TimeToLive;
        if (increment > remainingToMax) increment = remainingToMax;

        TimeToLive += increment;
    }

    // LINK OPERATIONS

    /// <summary>
    /// Adds a link to this thought if it does not already exist.
    /// </summary>
    /// <param name="linkType">Relationship type thought.</param>
    /// <param name="to">Target thought.</param>
    /// <returns>The new or existing link.</returns>
    public Link? AddLink(string linkTypeLabel, Thought? to)
    {
        Thought? linkType = ThoughtLabels.GetThought(linkTypeLabel);
        if (linkType is null) return null;
        return AddLink(linkType, to);
    }

    public Link? AddLink(Thought linkType, Thought? to)
    {
        if (linkType is null) return null;

        Link? existing = HasLink(linkType, to);
        if (existing is not null)
            return existing;

        var r = new Link
        {
            LinkType = linkType,
            From = this,
            To = to,
            LastFiredTime = DateTime.Now,
        };
        if (to is not null)
        {
            lock (_linksTo)
                lock (linkType._linksAsType)
                lock (to._linksFrom)
                {
                    LinksToWriteable.Add(r);
                    linkType._linksAsType.Add(r);
                    to.LinksFromWriteable.Add(r);
                }
        }
        else
        {
            lock (_linksTo)
            lock (linkType._linksAsType)
            {
                LinksToWriteable.Add(r);
                linkType._linksAsType.Add(r);
            }
        }
        return r;
    }

    /// <summary>
    /// Removes a link of the given type to the given target.
    /// </summary>
    /// <param name="linkType">Link type.</param>
    /// <param name="to">Target thought.</param>
    public Link RemoveLink(Thought linkType, Thought to)
    {
        Link r = new() { From = this, LinkType = linkType, To = to };
        RemoveLink(r);
        return r;
    }

    /// <summary>
    /// Removes all links of a given type originating from this thought.
    /// </summary>
    /// <param name="linkType">Link type to remove.</param>
    public void RemoveLinks(Thought linkType)
    {
        for (int i = 0; i < _linksTo.Count; i++)
        {
            Link r = _linksTo[i];
            if (r.From == this && r.LinkType == linkType)
            {
                RemoveLink(r);
                i--;
            }
        }
    }
    /// <summary>
    /// Removes a specific link instance.
    /// </summary>
    /// <param name="r">Link to remove.</param>
    public void RemoveLink(Link r)
    {
        if (r is null) return;
        if (r.LinkType is null) return;

        Link? storedLink = r;
        if (r.From is not null)
        {
            lock (r.From._linksTo)
                storedLink = r.From._linksTo.Find(candidate =>
                    ReferenceEquals(candidate, r) ||
                    (ReferenceEquals(candidate.From, r.From) &&
                     ReferenceEquals(candidate.LinkType, r.LinkType) &&
                     ReferenceEquals(candidate.To, r.To)));
        }
        if (storedLink is null) return;

        if (storedLink.From is not null)
            lock (storedLink.From._linksTo)
                storedLink.From._linksTo.Remove(storedLink);
        lock (storedLink.LinkType!._linksAsType)
            storedLink.LinkType._linksAsType.Remove(storedLink);
        if (storedLink.To is not null)
            lock (storedLink.To._linksFrom)
                storedLink.To._linksFrom.Remove(storedLink);

        storedLink.Delete();
    }

    public Thought? GetTargetOfFirstLinkOfType(Thought linkType)
    {
        return LinksTo.FindFirst(x => x.LinkType == linkType)?.To;
    }

    public Thought? GetTargetOfFirstLinkOfType(string linkTypeLabel)
    {
        return LinksTo.FindFirst(x =>
            string.Equals(x.LinkType?.Label, linkTypeLabel, StringComparison.OrdinalIgnoreCase))?.To;
    }
    private static bool LinkTypesMatch(Thought? a, Thought? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        return string.Equals(a.Label, b.Label, StringComparison.OrdinalIgnoreCase);
    }

    public Link? HasLink(Thought linkType, Thought? to = null)
    {
        lock (_linksTo)
        {
            foreach (Link r in _linksTo)
            {
                if (r.From == this && (r.To == to || to is null) && LinkTypesMatch(r.LinkType, linkType))
                    return r;
            }
        }
        return null;
    }
    /// <summary>
    /// Finds a link matching the optional source/type/target criteria.
    /// </summary>
    public Link? HasLink(Thought? from, Thought? linkType, Thought? to)
    {
        if (from is null && linkType is null && to is null) return null;
        foreach (Link r in LinksTo)
            if ((from is null || r.From == from) &&
                (linkType is null || r.LinkType == linkType) &&
                (to is null || r.To == to)) return r;
        return null;
    }

    /// <summary>
    /// Adds a parent link ("is-a") if not already present.
    /// </summary>
    /// <param name="newParent">Parent to add.</param>
    public Link? AddParent(Thought newParent)
    {
        if (newParent is null) return null;
        if (!Parents.Contains(newParent))
            return AddLink("is-a", newParent);
        return LinksTo.FindFirst(x => x.To == newParent && x.LinkType == IsA);
    }

    /// <summary>
    /// Remove a parent from a Thought.
    /// </summary>
    /// <param name="t">Parent thought to remove.</param>
    public void RemoveParent(string parentLabel)
    {
        Thought? t = ThoughtLabels.GetThought(parentLabel);
        if (t is not null) RemoveParent(t);
    }

    public void RemoveParent(Thought? t)
    {
        if (t is null) return;
        Link r = new() { From = this, LinkType = IsA, To = t };
        t.RemoveLink(r);
    }

    /// <summary>
    /// Remove a child link ("is-a") from this Thought.
    /// </summary>
    /// <param name="t">Child thought to remove.</param>
    public void RemoveChild(Thought t)
    {
        Link r = new() { From = t, LinkType = IsA, To = this };
        RemoveLink(r);
    }

    /// <summary>
    /// Gets attributes linked via "hasAttribute" or "is".
    /// </summary>
    public List<Thought> GetAttributes()
    {
        List<Thought> retVal = new();
        foreach (Link r in LinksTo)
        {
            if (r.LinkType?.Label != "hasAttribute" && r.LinkType?.Label != "is") continue;
            if (r.To is not null)
                retVal.Add(r.To);
        }
        return retVal;
    }

    /// <summary>
    /// Determines whether this thought has the specified property, considering inheritance.
    /// </summary>
    /// <param name="t">Property thought to test.</param>
    public bool HasProperty(string label)
    {
        Thought? t = ThoughtLabels.GetThought(label);
        return t is not null && HasProperty(t);
    }
    public Thought AddProperty(Thought t)
    {
        if (t is not null && !HasProperty(t))
        {
            AddLink("hasProperty", t);
        }
        return t;
    }

    public bool HasProperty(Thought? t)  //with inheritance
    {
        if (t is null) return false;
        if (LinksTo.FindFirst(x => x.LinkType?.Label == "hasProperty" && x.To == t) is not null) return true;

        foreach (Thought t1 in Ancestors)
            if (t1.LinksTo.FindFirst(x => x.LinkType?.Label == "hasProperty" && x.To == t) is not null) return true;
        return false;
    }

    /// <summary>
    /// Enumerate the closure starting from this Thought (root) using BFS over links and is-a children.
    /// </summary>
    public IEnumerable<Thought> EnumerateSubThoughts()
    {
        var visited = new HashSet<Thought>();
        var q = new Queue<Thought>();

        void EnqueueIfNew(Thought? t)
        {
            if (t is null) return;
            if (visited.Add(t))
                q.Enqueue(t);
        }

        EnqueueIfNew(this);
        foreach (var isaLink in this.LinksTo.Where(x => x.LinkType?.Label == "is-a"))
            yield return isaLink;

        while (q.Count > 0)
        {
            var t = q.Dequeue();
            if (t is null) continue;

            if (t is Link lnk)
            {
                yield return lnk;
                EnqueueIfNew(lnk.LinkType);
                EnqueueIfNew(lnk.To);
            }

            if (t is SeqElement s)
            {
                do { EnqueueIfNew(s); s = s.NXT as SeqElement; } while (s is not null);
            }
            foreach (var isaLink in t.LinksFrom.Where(x => x.LinkType?.Label == "is-a"))
            {
                EnqueueIfNew(isaLink);
                EnqueueIfNew(isaLink.From);
            }
            foreach (var link in t.LinksTo.Where(x => x.LinkType?.Label != "is-a"))
            {
                EnqueueIfNew(link);
                EnqueueIfNew(link.To);
            }
        }
    }
}
