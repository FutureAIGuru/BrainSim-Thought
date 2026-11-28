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

/// <summary>
/// A link: a Thought that connects three others, its source (<see cref="From"/>), its type
/// (<see cref="LinkType"/>) and its target (<see cref="To"/>), as in [Fido→is-a→dog]. Because a link
/// is a Thought, it can itself be the source or target of another link (a nested statement).
/// </summary>
/// <remarks>
/// A link built with a constructor is not stored: use it as a pattern (<see cref="UKS.FindLinks"/>,
/// <see cref="UKS.ApplyTestOrSetAction"/>). Store a link with <see cref="UKS.AddStatement"/> or
/// <see cref="Thought.AddLink"/>.
/// </remarks>
public class Link : Thought
{
    /// <summary>Creates an unconnected, unstored link in the store.</summary>
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

    /// <summary>Creates an unstored link [from → linkType → to], e.g. a query pattern.</summary>
    /// <exception cref="ArgumentException">All three are null.</exception>
    public Link(Thought? from, Thought? linkType, Thought? to)
        : base((from ?? linkType ?? to)?.Store ?? throw new ArgumentException("A link needs at least one Thought."))
    {
        From = from;
        LinkType = linkType;
        To = to;
    }
    /// <summary>Creates an unstored copy of a link; nested links are copied too.</summary>
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
    /// <summary>The source of the link.</summary>
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
    /// <summary>The type of the link (is-a, has, ...).</summary>
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
    /// <summary>The target of the link; may be null.</summary>
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

    /// <summary>The link as text: [From→LinkType→To], preceded by its label if it has a name.</summary>
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
/// The single kind of element in a UKS. A Thought can stand for anything: an object, an attribute, a
/// word, an action. It has a label, links to other Thoughts (<see cref="LinksTo"/>), and a place in
/// the is-a hierarchy (<see cref="Parents"/>, <see cref="Children"/>), from which it inherits.
/// </summary>
/// <remarks>
/// Thoughts are compared by identity: two Thoughts are equal only if they are the same object. A label
/// finds a Thought (<see cref="UKS.Labeled"/>) but is never compared.
/// </remarks>
public partial class Thought
{
    /// <summary>The store this Thought belongs to; it never changes.</summary>
    public UKS Store { get; }

    private readonly List<Link> _linksTo = new();   // links to "has", "is", is-a, many others
    private readonly List<Link> _linksFrom = new(); // links from
    private readonly List<Link> _linksAsType = new(); // links which use this Thought as their LinkType

    /// <summary>Unsafe writeable list of outgoing links.</summary>
    internal List<Link> LinksToWriteable { get => _linksTo; }
    /// <summary>Unsafe writeable list of incoming links.</summary>
    internal List<Link> LinksFromWriteable { get => _linksFrom; }
    internal List<Link> LinksAsTypeWriteable { get => _linksAsType; }
    /// <summary>This Thought's own links (it is their source), as a copy that is safe to keep.</summary>
    public IReadOnlyList<Link> LinksTo
    {
        get { lock (_linksTo) return new List<Link>(_linksTo); }
    }
    /// <summary>The links that have this Thought as their target, as a copy that is safe to keep.</summary>
    public IReadOnlyList<Link> LinksFrom { get { lock (_linksFrom) { return new List<Link>(_linksFrom.AsReadOnly()); } } }
    /// <summary>The links that have this Thought as their type, as a copy that is safe to keep.</summary>
    public IReadOnlyList<Link> LinksAsType { get { lock (_linksAsType) return new List<Link>(_linksAsType); } }
    /// <summary>The direct parents: targets of this Thought's is-a links.</summary>
    public IReadOnlyList<Thought> Parents
    {
        get
        {
            if (Store.IsAType is not Thought isA) return [];
            lock (_linksTo) return _linksTo.Where(x => x.LinkType == isA).Select(x => x.To).OfType<Thought>().ToList();
        }
    }
    /// <summary>The direct children: sources of is-a links to this Thought.</summary>
    public IReadOnlyList<Thought> Children
    {
        get
        {
            if (Store.IsAType is not Thought isA) return [];
            lock (_linksFrom) return _linksFrom.Where(x => x.LinkType == isA).Select(x => x.From).OfType<Thought>().ToList();
        }
    }

    private string _label = "";
    /// <summary>
    /// The Thought's name, unique in its store and case-insensitive. Setting a label already in use
    /// appends a number, and a trailing * becomes the next free number (dog* gives dog0, dog1, ...).
    /// </summary>
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

    /// <summary>
    /// Removes this Thought from its store with all its links. Its children move to its parents (a
    /// wildcard child is deleted instead), and sequences only it refers to are deleted. Deleting a link removes it
    /// from its source.
    /// </summary>
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
    /// Cycles left before this Thought (or link) is deleted, counted down by <see cref="UKS.AdvanceCycle"/>.
    /// <see cref="Forever"/> (the default) means it never expires; firing extends a finite time.
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
    /// <summary>Any object attached to the Thought. Only a string value is saved to a file.</summary>
    public object? Value
    {
        get => _value;
        set { _value = value; }
    }

    private float _weight = 1;
    /// <summary>
    /// Strength or confidence, 1 by default. A query multiplies the weights along an inheritance path and
    /// orders its results by weight; repeating a statement strengthens its link.
    /// </summary>
    public float Weight
    {
        get => _weight;
        set => _weight = value;
    }
    private float _maxWeight = 1;
    /// <summary>An upper limit on <see cref="Weight"/> for learning code to respect; the store saves it but does not use it.</summary>
    public float MaxWeight
    {
        get => _maxWeight;
        set => _maxWeight = value;
    }

    /// <summary>
    /// True if learning may still change this Thought's weight. The store saves the flag but does not act
    /// on it; structural Thoughts and links are not plastic.
    /// </summary>
    public bool IsPlastic { get; set; }

    /// <summary>
    /// Creates an unlabeled Thought in the store. It becomes a stored Thought when it is given a label;
    /// usually <see cref="UKS.GetOrAddThought"/> or <see cref="UKS.AddThought"/> is simpler.
    /// </summary>
    public Thought(UKS store)
    {
        Store = store ?? throw new ArgumentNullException(nameof(store));
        CreatedCycle = Store.Cycle;
    }

    /// <summary>Creates a copy of a Thought's label, weights and value, without its links.</summary>
    /// <param name="r">The Thought to copy; for a link, construct a <see cref="Link"/> instead.</param>
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

    /// <summary>The label, followed by _V: and the value if there is one.</summary>
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

    /// <summary>Every ancestor (parents, their parents, ...), nearest first, each once; not this Thought.</summary>
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

    /// <summary>Every descendant (children, their children, ...), nearest first, each once; not this Thought.</summary>
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
    /// True if <paramref name="ancestor"/> is this Thought or one of its ancestors. An unknown label is
    /// never an ancestor.
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
    /// Removes a stored link. <paramref name="r"/> may be an unstored copy: the link with the same source,
    /// type and target is removed.
    /// </summary>
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
    /// This Thought's own link of the given type to the given target (to any target if it is empty), or
    /// null if there is none or a label is unknown. Inherited links are not included; see
    /// <see cref="UKS.GetAttributes"/>.
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
