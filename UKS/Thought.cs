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

namespace UKS;

public class Link : Thought
{
    /// <summary>
    /// Default constructor for a link thought.
    /// </summary>
    public Link(UKS store) : base(store) { }

    /// <summary>
    /// True if the two are the same Thought, or Links making the same connection: the same source,
    /// link type and target, where a source or target that is itself a Link (a nested statement) is
    /// compared the same way. A store keeps one link per connection, so this finds the stored link
    /// that an unstored one (built for a query or a pattern) stands for.
    /// </summary>
    internal static bool SameConnection(Thought? a, Thought? b)
    {
        if (ReferenceEquals(a, b)) return true;
        return a is Link la && b is Link lb && ReferenceEquals(la.LinkType, lb.LinkType) &&
            SameConnection(la.From, lb.From) && SameConnection(la.To, lb.To);
    }

    /// <summary>
    /// Creates a link with the specified source, link type, and target.
    /// </summary>
    /// <param name="from">Source thought.</param>
    /// <param name="linkType">Link type thought.</param>
    /// <param name="to">Target thought.</param>
    public Link(Thought? from, Thought? linkType, Thought? to)
        : base((from ?? linkType ?? to)?.Store ?? throw new ArgumentException("A link needs at least one Thought."))
    {
        From = from;
        LinkType = linkType;
        To = to;
    }
    public Link(Link l) : base(l.Store)
    {
        if (l.From is Link l1)
            From = new(l1);
        else
            From = l.From;
        if (l.LinkType is Link l2)
            LinkType = new(l2);
        else
            LinkType = l.LinkType;
        if (l.To is Link l3)    
            To = new(l3);   
        else
            To = l.To;
    }

    private Thought? from;
    private Thought? linkType;
    private Thought? to;
    public Thought? From
    {
        get => from;
        set
        {
            if (value is not null && !ReferenceEquals(value.Store, Store))
                throw new ArgumentException("A Link's source belongs to another UKS.", nameof(value));
            from = value;
        }
    }
    public Thought? LinkType
    {
        get => linkType;
        set
        {
            if (value is not null && !ReferenceEquals(value.Store, Store))
                throw new ArgumentException("A Link's type belongs to another UKS.", nameof(value));
            linkType = value;
        }
    }
    public Thought? To
    {
        get => to;
        set
        {
            if (value is not null && !ReferenceEquals(value.Store, Store))
                throw new ArgumentException("A Link's target belongs to another UKS.", nameof(value));
            to = value;
        }
    }

    /// <summary>
    /// Returns a formatted string for the link, showing sequence notation or From→Type→To.
    /// </summary>
    public override string ToString()
    {
        string retVal = (HasGeneratedLabel ? "" : Label) + "[";
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
    /// <summary>The independent UKS containing this Thought; never changes after construction.</summary>
    public UKS Store { get; }

    private readonly List<Link> _linksTo = new();   // links to "has", "is", is-a, many others
    private readonly List<Link> _linksFrom = new(); // links from
    private readonly List<Link> _linksAsType = new(); // links which use this Thought as their LinkType

    /// <summary>Unsafe writeable list of outgoing links.</summary>
    internal List<Link> LinksToWriteable { get => _linksTo; }
    /// <summary>Unsafe writeable list of incoming links.</summary>
    internal List<Link> LinksFromWriteable { get => _linksFrom; }
    internal List<Link> LinksAsTypeWriteable { get => _linksAsType; }
    /// <summary>Safe snapshot of outgoing links.</summary>
    public IReadOnlyList<Link> LinksTo
    {
        get { lock (_linksTo) return new List<Link>(_linksTo); }
    }
    /// <summary>Safe snapshot of incoming links.</summary>
    public IReadOnlyList<Link> LinksFrom { get { lock (_linksFrom) { return new List<Link>(_linksFrom.AsReadOnly()); } } }
    /// <summary>Safe snapshot of the links which use this Thought as their LinkType.</summary>
    public IReadOnlyList<Link> LinksAsType { get { lock (_linksAsType) return new List<Link>(_linksAsType); } }
    /// <summary>Direct parents (targets of outgoing is-a links).</summary>
    public IReadOnlyList<Thought> Parents
    {
        get
        {
            if (Store.IsAType is not Thought isA) return [];
            lock (_linksTo) return _linksTo.Where(x => x.LinkType == isA).Select(x => x.To).OfType<Thought>().ToList();
        }
    }
    /// <summary>Direct children (sources of incoming is-a links).</summary>
    public IReadOnlyList<Thought> Children
    {
        get
        {
            if (Store.IsAType is not Thought isA) return [];
            lock (_linksFrom) return _linksFrom.Where(x => x.LinkType == isA).Select(x => x.From).OfType<Thought>().ToList();
        }
    }

    private string _label = "";
    public string Label
    {
        get => _label;
        set
        {
            if (value == _label) return;
            bool wasUnlabeled = string.IsNullOrWhiteSpace(_label);
            string oldLabel = _label;
            Store.UnindexLabel(_label);
            _label = Store.IndexLabel(value, this);
            // A Thought is created when it is first labeled; an open transaction records it, or the relabel.
            if (wasUnlabeled && !string.IsNullOrWhiteSpace(_label))
                Store.Record(UKSTransaction.Change.ThoughtCreated, this);
            else if (!wasUnlabeled && oldLabel != _label)
                Store.Record(UKSTransaction.Change.ThoughtRelabeled, this, oldLabel);
            // A private pattern can become a stored Thought with existing links.
            if (wasUnlabeled && !string.IsNullOrWhiteSpace(_label) && _linksTo.Count != 0)
                foreach (var link in LinksTo)
                {
                    link.AddDefaultLabel();
                    link.LinkType?.AddDefaultLabel();
                    link.To?.AddDefaultLabel();
                }
        }
    }

    // A label the store made up (NextGeneratedLabel), as opposed to a name.
    internal bool HasGeneratedLabel => Label.StartsWith("unl_", StringComparison.OrdinalIgnoreCase);

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

        if (this is not Link) Store.ReplaceDeletedSequenceValues(this);
        foreach (Link typedLink in _linksAsType.ToList())
            typedLink.From?.RemoveLink(typedLink);

        foreach (Link r in _linksTo
            .Where(candidate => candidate.To is SeqElement seq && ReferenceEquals(seq.FRST, seq))
            .ToList())
        {
            if (r.To is SeqElement sequence)
                Store.DeleteSequence(sequence);
        }

        foreach (Link r in _linksTo.ToList())
            RemoveLink(r);

        foreach (Link r in _linksFrom.ToList())
            (r.From ?? this).RemoveLink(r);

        foreach (Thought child in childrenToReparent)
        {
            // A class-specific wildcard has no meaning after its class is
            // deleted. Promoting it to the class's parent turns an internal
            // matcher into an ordinary sibling, so remove it instead.
            if (child.HasProperty("isWildcard"))
            {
                child.Delete();
                continue;
            }

            foreach (Thought parent in replacementParents)
                if (!ReferenceEquals(child, parent) && !ReferenceEquals(parent, this))
                    child.AddParent(parent);

            if (child.Parents.Count == 0 && Store.Labeled("Unknown") is Thought unknown)
                child.AddParent(unknown);
        }

        Store.TrackMortality(this, forget: true);
        Store.UnindexLabel(Label);
        lock (Store.AtomicThoughts)
            Store.AtomicThoughts.Remove(this);
        if (this is not Link) Store.Record(UKSTransaction.Change.ThoughtDeleted, this, Label);
    }

    /// <summary>The <see cref="UKS.Cycle"/> at which this Thought last fired; 0 if it never has.</summary>
    public long LastFiredCycle;
    /// <summary>True if this Thought fired within the last <paramref name="cycles"/> cycles.</summary>
    public bool FiredWithin(long cycles) => LastFiredCycle != 0 && Store.Cycle - LastFiredCycle < cycles;
    /// <summary>The <see cref="UKS.Cycle"/> at which this Thought was created.</summary>
    public long CreatedCycle { get; }

    /// <summary>A <see cref="TimeToLive"/> of -1: the Thought never expires.</summary>
    public const long Forever = -1;

    private long _timeToLive = Forever;
    /// <summary>
    /// Cycles left before this Thought (or Link) is deleted, counted down by <see cref="UKS.AdvanceCycle"/>;
    /// <see cref="Forever"/> (-1, the default) means it never expires.
    /// </summary>
    public long TimeToLive
    {
        get => _timeToLive;
        set
        {
            _timeToLive = value < 0 ? Forever : value;
            Store.TrackMortality(this);
        }
    }

    // One cycle passes: true when the time to live has run out.
    internal bool CountDown()
    {
        if (_timeToLive == Forever) return false;
        if (_timeToLive > 0) _timeToLive--;
        return _timeToLive == 0;
    }

    private object? _value;
    /// <summary>Any serializable object can be attached to a Thought. ONLY STRINGS are supported for save/restore to disk file.</summary>
    public object? Value
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
    /// <summary>MaxWeight of this Thought (for links, applies to the link).</summary>
    public float MaxWeight
    {
        get => _maxWeight;
        set => _maxWeight = value;
    }

    /// <summary>
    /// True when ordinary learning and forgetting are allowed to change this
    /// Thought's persistent weight. Structural Thoughts and Links are not
    /// plastic by default.
    /// </summary>
    public bool IsPlastic { get; set; }

    /// <summary>
    /// Default constructor.
    /// </summary>
    public Thought(UKS store)
    {
        Store = store ?? throw new ArgumentNullException(nameof(store));
        CreatedCycle = Store.Cycle;
    }

    /// <summary>
    /// Copy constructor. For link thoughts, construct a Link instead.
    /// </summary>
    /// <param name="r">Thought to copy.</param>
    public Thought(Thought r) : this(r.Store)
    {
        if (r is Link)
            return;
        Weight = r.Weight;
        MaxWeight = r.MaxWeight;
        IsPlastic = r.IsPlastic;
        Value = r.Value;
        Label = r.Label;
        
    }

    /// <summary>
    /// Returns a Thought's label with attached value if present.
    /// </summary>
    public override string ToString()
    {
        string retVal = Label.Trim();
        if (Value is not null)
            retVal += "_V:" + Value.ToString();

        return retVal;
    }

    /// <summary>
    /// Assigns a default label to a link thought when missing.
    /// </summary>
    internal Thought AddDefaultLabel()
    {
        if (string.IsNullOrWhiteSpace(Label))
            Label = Store.NextGeneratedLabel();
        return this;
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
    /// True if this Thought is the given Thought or one of its descendants (follows is-a upward).
    /// An unknown label is never an ancestor.
    /// </summary>
    public bool HasAncestor(ThoughtRef ancestor)
    {
        Thought? t = ancestor.Find(Store);
        return t is not null && (t == this || Ancestors.Contains(t));
    }

    /// <summary>
    /// Fires the Thought: records the cycle, extends a finite time to live, and raises <see cref="UKS.ThoughtFired"/>.
    /// </summary>
    public void Fire()
    {
        if (Store.SuppressFiring)
            return;
            
        LastFiredCycle = Store.Cycle;
        if (TimeToLive != Forever) TimeToLive += 20 * UKS.CyclesPerSecond;
        Store.RaiseThoughtFired(this);
    }

    // LINK OPERATIONS

    /// <summary>
    /// Adds the link [this → linkType → to] unless it already exists. An unknown label creates the
    /// Thought (under LinkType for the link type, else under Unknown). The target may be empty.
    /// </summary>
    /// <returns>The new or existing link, or null if the link type is empty.</returns>
    public Link? AddLink(ThoughtRef linkType, ThoughtRef to)
    {
        Thought? type = linkType.GetOrAdd(Store, "LinkType");
        if (type is null) return null;
        Thought? target = to.GetOrAdd(Store, "Unknown");

        Link? existing = HasLink(type, target);
        if (existing is not null)
            return existing;

        var r = new Link(Store)
        {
            LinkType = type,
            From = this,
            To = target,
            LastFiredCycle = Store.Cycle,
        };
        // Unlabeled prototypes can build a private pattern without indexing it.
        if (!string.IsNullOrEmpty(Label))
        {
            r.AddDefaultLabel();
            type.AddDefaultLabel();
            target?.AddDefaultLabel();
        }
        if (target is not null)
        {
            lock (_linksTo)
                lock (type._linksAsType)
                lock (target._linksFrom)
                {
                    LinksToWriteable.Add(r);
                    type._linksAsType.Add(r);
                    target.LinksFromWriteable.Add(r);
                }
        }
        else
        {
            lock (_linksTo)
            lock (type._linksAsType)
            {
                LinksToWriteable.Add(r);
                type._linksAsType.Add(r);
            }
        }
        Store.Record(UKSTransaction.Change.LinkAdded, r);
        return r;
    }

    /// <summary>
    /// Removes the link [this → linkType → to], if present. An empty target means a link with no target;
    /// an unknown label removes nothing.
    /// </summary>
    public void RemoveLink(ThoughtRef linkType, ThoughtRef to)
    {
        Thought? type = linkType.Require(Store);
        Thought? target = to.Require(Store);
        if (type is null || (target is null && !to.IsEmpty)) return;
        RemoveLink(new Link(Store) { From = this, LinkType = type, To = target });
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
                storedLink = r.From._linksTo.Find(candidate => Link.SameConnection(candidate, r));
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

        storedLink.Store.Record(UKSTransaction.Change.LinkRemoved, storedLink);
        storedLink.Delete();
    }

    /// <summary>
    /// This Thought's link of the given type to the given target, or to any target if the target is
    /// empty; null if there is none or a label is unknown.
    /// </summary>
    public Link? HasLink(ThoughtRef linkType, ThoughtRef to = default)
    {
        Thought? type = linkType.Find(Store);
        Thought? target = to.Find(Store);
        if (type is null || (target is null && !to.IsEmpty)) return null;
        lock (_linksTo)
        {
            foreach (Link r in _linksTo)
            {
                if (ReferenceEquals(r.LinkType, type) && (target is null || Link.SameConnection(r.To, target)))
                    return r;
            }
        }
        return null;
    }
    /// <summary>
    /// The first of this Thought's links matching the given source, type and target, where an empty
    /// one matches anything; null if there is none, all three are empty, or a label is unknown.
    /// </summary>
    public Link? HasLink(ThoughtRef from, ThoughtRef linkType, ThoughtRef to)
    {
        if (from.IsEmpty && linkType.IsEmpty && to.IsEmpty) return null;
        Thought? source = from.Find(Store), type = linkType.Find(Store), target = to.Find(Store);
        if ((source is null && !from.IsEmpty) || (type is null && !linkType.IsEmpty) ||
            (target is null && !to.IsEmpty)) return null;
        foreach (Link r in LinksTo)
            if ((source is null || Link.SameConnection(r.From, source)) &&
                (type is null || ReferenceEquals(r.LinkType, type)) &&
                (target is null || Link.SameConnection(r.To, target))) return r;
        return null;
    }

    /// <summary>
    /// Makes this Thought a child of the parent (an is-a link) unless it already is.
    /// An unknown label creates the parent under Unknown.
    /// </summary>
    /// <returns>The is-a link, or null if the parent is empty.</returns>
    public Link? AddParent(ThoughtRef parent)
    {
        // is-a is found, never created: creating a Thought adds its parent, which would come back here.
        if (Store.Labeled("is-a") is not Thought isA) return null;
        Thought? t = parent.GetOrAdd(Store, "Unknown");
        return t is null ? null : AddLink(isA, t);
    }

    /// <summary>Removes the is-a link to the parent, if present; an unknown label removes nothing.</summary>
    public void RemoveParent(ThoughtRef parent) => RemoveLink("is-a", parent);

    // The targets of this Thought's own "is" and "hasAttribute" links (not inherited).
    internal List<Thought> DirectAttributes()
    {
        Thought? isType = Store.Labeled("is"), hasAttribute = Store.Labeled("hasAttribute");
        return LinksTo.Where(link => link.To is not null && link.LinkType is not null &&
                (link.LinkType == isType || link.LinkType == hasAttribute))
            .Select(link => link.To!).ToList();
    }

    /// <summary>
    /// Adds a hasProperty link to the property unless this Thought already has it, directly or by
    /// inheritance. An unknown label creates the property under Unknown.
    /// </summary>
    /// <returns>The property, or null if it is empty.</returns>
    public Thought? AddProperty(ThoughtRef property)
    {
        Thought? t = property.GetOrAdd(Store, "Unknown");
        if (t is not null && !HasProperty(t))
            AddLink("hasProperty", t);
        return t;
    }

    /// <summary>True if this Thought or one of its ancestors has a hasProperty link to the property.</summary>
    public bool HasProperty(ThoughtRef property)
    {
        Thought? t = property.Find(Store);
        if (t is null || Store.HasPropertyType is not Thought hasProperty) return false;
        bool HasItself(Thought thought) => thought.LinksTo.FirstOrDefault(x => x.LinkType == hasProperty && x.To == t) is not null;
        return HasItself(this) || Ancestors.Any(HasItself);
    }

    // The closure from this Thought (the root), breadth first over links and is-a children: what a
    // file writes for the root.
    internal IEnumerable<Thought> EnumerateSubThoughts()
    {
        Thought? isA = Store.IsAType;
        // This is graph traversal, so distinct objects must remain distinct even
        // when Thought.Equals considers them structurally equivalent. In
        // particular, anonymous sequence values may be equal links that still
        // require separate persistence entries.
        var visited = new HashSet<Thought>(ReferenceEqualityComparer.Instance);
        var q = new Queue<Thought>();

        void EnqueueIfNew(Thought? t)
        {
            if (t is null) return;
            if (visited.Add(t))
                q.Enqueue(t);
        }

        EnqueueIfNew(this);
        foreach (var isaLink in this.LinksTo.Where(x => x.LinkType == isA))
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
                for (SeqElement? e = s; e is not null; e = e.NXT) EnqueueIfNew(e);
            }
            foreach (var isaLink in t.LinksFrom.Where(x => x.LinkType == isA))
            {
                EnqueueIfNew(isaLink);
                EnqueueIfNew(isaLink.From);
            }
            foreach (var link in t.LinksTo.Where(x => x.LinkType != isA))
            {
                EnqueueIfNew(link);
                EnqueueIfNew(link.To);
            }
        }
    }
}
