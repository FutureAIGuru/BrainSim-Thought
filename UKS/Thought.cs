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
public class Thought
{
    public static Thought IsA { get => ThoughtLabels.GetThought("is-a"); }  //this is a cache value shortcut for (Thought)"is-a"
    private readonly List<Link> _linksTo = new();   // links to "has", "is", is-a, many others
    private readonly List<Link> _linksFrom = new(); // links from

    /// <summary>Unsafe writeable list of outgoing links.</summary>
    public List<Link> LinksToWriteable { get => _linksTo; }
    /// <summary>Unsafe writeable list of incoming links.</summary>
    public List<Link> LinksFromWriteable { get => _linksFrom; }
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
    public IReadOnlyList<Thought> Parents { get { lock (_linksTo) return _linksTo.Where(x => x.LinkType?.Label == "is-a").Select(x => x.To).ToList(); } }
    /// <summary>Direct children (sources of incoming is-a links).</summary>
    public IReadOnlyList<Thought> Children { get { lock (_linksFrom) return _linksFrom.Where(x => x.LinkType?.Label == "is-a").Select(x => x.From).ToList(); } }

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
        foreach (Link r in _linksTo.Where(x => UKS.theUKS.IsSequenceFirstElement(x.To)))
            UKS.theUKS.DeleteSequence((SeqElement)r.To);

        for (int i = 0; i < _linksTo.Count; i++)
        {
            Link r = _linksTo[i];
            RemoveLink(r);
            i--;
        }

        for (int i = 0; i < _linksFrom.Count; i++)
        {
            Link r = _linksFrom[i];
            r.From.RemoveLink(r);
            i--;
        }

        ThoughtLabels.RemoveThoughtLabel(Label);
        lock (UKS.theUKS.AtomicThoughts)
            UKS.theUKS.AtomicThoughts.Remove(this);
    }

    private bool CheckExpiredAndMaybeDelete()
    {
        if (_timeToLive == TimeSpan.MaxValue) return false;
        if (LastFiredTime + _timeToLive > DateTime.Now) return false;

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

    public int UseCount = 0;
    public DateTime LastFiredTime = DateTime.Now;

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

    private object _value;
    /// <summary>Any serializable object can be attached to a Thought. ONLY STRINGS are supported for save/restore to disk file.</summary>
    public object V
    {
        get => _value;
        set { _value = value; }
    }

    private float _weight = 1;
    /// <summary>Weight of this Thought (for links, applies to the link).</summary>
    public float Weight
    {
        get => _weight;
        set
        {
            _weight = value;
            if (this is Link link && link.LinkType?.HasProperty("IsCommutative") == true)
            {
                Link rReverse = link.To?.LinksTo.FindFirst(x => x.LinkType == link.LinkType && x.To == link.From);
                if (rReverse is not null)
                    rReverse._weight = _weight;
            }
        }
    }

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
        V = r.V;
        Label = r.Label;
    }

    /// <summary>
    /// Returns a Thought's label with attached value if present.
    /// </summary>
    public override string ToString()
    {
        string retVal = Label;
        if (V is not null)
            retVal += "_V:" + V.ToString();

        return retVal;
    }

    /// <summary>
    /// Allows implicit conversion from a label string to an existing Thought (or null if not found).
    /// </summary>
    public static implicit operator Thought(string label)
    {
        Thought t = ThoughtLabels.GetThought(label);
        return t;
    }

    /// <summary>
    /// Equality by label; for Link, also compares endpoints and link type.
    /// </summary>
    public override bool Equals(object obj)
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
    /// Returns direct children plus any subclass children.
    /// </summary>
    public IReadOnlyList<Thought> ChildrenWithSubclasses
    {
        get
        {
            List<Thought> retVal = Children.ToList();
            for (int i = 0; i < retVal.Count; i++)
            {
                Thought t = retVal[i];
                if (t.Label.StartsWith(this._label))
                {
                    retVal.AddRange(t.Children);
                    retVal.RemoveAt(i);
                    i--;
                }
            }
            return retVal;
        }
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
    public bool HasAncestor(Thought t)
    {
        foreach (var ancestor in AncestorsWithSelf)
            if (ancestor == t) return true;
        return false;
    }

    /// <summary>
    /// Updates the last-fired time on a Thought.
    /// </summary>
    public void Fire()
    {
        LastFiredTime = DateTime.Now;
        UseCount++;
        float baseSeconds = 10;
        float growthFactor = 2;
        float maxSeconds = 60 * 60 * 24; //1 day
        double ttlSeconds = baseSeconds * Math.Pow(growthFactor, UseCount);
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
    public Link AddLink(Thought linkType, Thought to)
    {
        if (linkType is null) return null;

        Link existing = HasLink(linkType, to);
        if (existing is not null)
            return existing;

        var r = new Link
        {
            LinkType = linkType,
            From = this,
            To = to,
        };
        if (to is not null && linkType is not null)
        {
            lock (_linksTo)
                lock (to._linksFrom)
                {
                    LinksToWriteable.Add(r);
                    to.LinksFromWriteable.Add(r);
                }
        }
        else
        {
            lock (_linksTo)
            {
                LinksToWriteable.Add(r);
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
        if (r.From is null)
        {
            lock (r.LinkType._linksFrom)
            {
                lock (r.To._linksFrom)
                {
                    r.LinkType._linksFrom.RemoveAll(x => x.From == r.From && x.LinkType == r.LinkType && x.To == r.To);
                    r.To._linksFrom.RemoveAll(x => x.From == r.From && x.LinkType == r.LinkType && x.To == r.To);
                }
            }
        }
        else if (r.To is null)
        {
            lock (r.From._linksTo)
            {
                lock (r.LinkType._linksFrom)
                {
                    r.From._linksTo.RemoveAll(x => x.From == r.From && x.LinkType == r.LinkType && x.To is null);
                    r.LinkType._linksFrom.RemoveAll(x => x.From == r.From && x.LinkType == r.LinkType && x.To is null);
                }
            }
        }
        else
        {
            lock (r.From._linksTo)
            {
                lock (r.LinkType._linksFrom)
                {
                    lock (r.To._linksFrom)
                    {
                        r.From._linksTo.Remove(r);
                        r.LinkType._linksFrom.Remove(r);
                        r.To._linksFrom.Remove(r);
                    }
                }
            }
        }
        r.Delete();
    }

    private Link HasLink(Thought linkType, Thought to)
    {
        foreach (Link r in _linksTo)
        {
            if (r.From == this && r.To == to && r.LinkType == linkType)
                return r;
        }
        return null;
    }
    /// <summary>
    /// Finds a link matching the optional source/type/target criteria.
    /// </summary>
    public Link HasLink(Thought from, Thought linkType, Thought to)
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
    public Link AddParent(Thought newParent)
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
    public void RemoveParent(Thought t)
    {
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
            retVal.Add(r.To);
        }
        return retVal;
    }

    /// <summary>
    /// Determines whether this thought has the specified property, considering inheritance.
    /// </summary>
    /// <param name="t">Property thought to test.</param>
    public bool HasProperty(Thought t)  //with inheritance
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
