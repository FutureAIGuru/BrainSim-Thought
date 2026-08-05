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

using Pluralize.NET;



/// <summary>
/// Contains a collection of Thoughts linked by Links to implement Common Sense and general knowledge.
/// </summary>
public partial class UKS
{
    //This is the actual internal Universal Knowledge Store
    static private HashSet<Thought> uKSList = new(ReferenceEqualityComparer.Instance);

    //This is a reformatted temporary copy of the UKS which used internally during the save and restore process to 
    //break circular links by storing index values instead of actual links Note the use of SThought instead of Thought
    private List<sThought> UKSTemp = new();

    /// <summary>
    /// Occasionally a set of all the atomic Thoughts in the UKS is needed.
    /// Membership uses object identity so changing a label cannot invalidate an entry.
    /// </summary>
    public HashSet<Thought> AtomicThoughts { get => uKSList; }

    public static UKS theUKS = new UKS();

    /// <summary>
    /// Creates a new reference to the UKS and initializes it if it is the first reference.
    /// </summary>
    /// <param name="clear">When true, clears existing thoughts and label cache before initialization.</param>
    public UKS(bool clear = false)
    {
        if (AtomicThoughts.Count == 0 || clear)
        {
            AtomicThoughts.Clear();
            ThoughtLabels.ClearLabelList();
        }
        UKSTemp.Clear();
    }

    // Handy for temporarily suppressing firing during bulk updates or similar operations
    public bool SuppressFiring { get; set; } = false;

    /// <summary>
    /// This is a primitive method needed only to create ROOT Thoughts which have no parents.
    /// </summary>
    /// <param name="label">Label of the thought to create.</param>
    /// <param name="parent">Optional parent thought (may be null).</param>
    /// <returns>The newly created thought.</returns>
    public virtual Thought AddThought(string label, Thought? parent)
    {
        if (label == "SET.EQ")
        { }
        Thought newThought = new();
        newThought.Label = label;
        if (parent is not null)
        {
            newThought.AddParent(parent);
        }
        lock (AtomicThoughts)
        {
            AtomicThoughts.Add(newThought);
        }

        return newThought;
    }

    /// <summary>
    /// Uses a hash table to return the Thought with the given label or null if it does not exist.
    /// </summary>
    /// <param name="label">Label to look up.</param>
    /// <returns>The Thought or null.</returns>
    public Thought? Labeled(string label) => ThoughtLabels.GetThought(label);


    private bool HasProperty(Thought t, string propertyName)
    {
        if (t is null) return false;
        var v = t.LinksTo;
        if (v.FindFirst(x => x.To?.Label.ToLower() == propertyName.ToLower() && x.LinkType?.Label == "hasProperty") is not null) return true;
        return false;
    }

    private bool LinksAreEqual(Link r1, Link r2, bool ignoreSource = true)
    {
        if (
            r1.Label == r2.Label &&
            (r1.From == r2.From || ignoreSource) &&
            (r1.To is null && r2.To is null || r1.To == r2.To) &&
            r1.LinkType == r2.LinkType
          ) return true;
        //special case if these contain other links
        if (r1.From is Link rt1 && r2.From is Link rt2)
        {
            if (!LinksAreEqual(rt1, rt2)) return false;
            if (r1.To is Link rt3 && r2.To is Link rt4)
                if (!LinksAreEqual(rt3, rt4)) return false;
            if (r1.LinkType != r2.LinkType) return false;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Gets an existing link matching the specified from/linkType/to triple, if present.
    /// </summary>
    /// <param name="from">Source thought of the link.</param>
    /// <param name="linkType">Relationship type thought.</param>
    /// <param name="to">Target thought of the link.</param>
    /// <returns>The existing link Thought, or null if not found.</returns>
    public Link? GetLink(Thought from, Thought linkType, Thought? to)
    {
        if (from is null) return null;
        //create a temporary link
        Link r = new() { From = from, LinkType = linkType, To = to };
        //see if it already exists
        return GetLink(r);
    }

    /// <summary>
    /// Gets an existing link matching the supplied link prototype.
    /// </summary>
    /// <param name="r">Link prototype (From, LinkType, To) to search for.</param>
    /// <returns>The existing link Thought, or null if not found.</returns>
    public Link? GetLink(Link r)
    {
        if (r.From is null) return null;
        foreach (Link r1 in r.From.LinksTo)
        {
            if (LinksAreEqual(r, r1)) return r1;
        }
        return null;
    }

    /// <summary>
    /// Gets all links from the same source matching the prototype's LinkType and To.
    /// </summary>
    /// <param name="r">Link prototype containing source, link type, and target.</param>
    /// <returns>List of matching link Thoughts.</returns>
    public List<Link> GetLinks(Link r)
    {
        List<Link> retVal = new();
        if (r.From is not null)
        {
            foreach (Link r1 in r.From.LinksTo)
            {
                if (r.LinkType == r1.LinkType && r.To == r1.To)
                    retVal.Add(r1);
            }
        }
        return retVal;
    }

    /// <summary>
    /// Recursively removes all the descendants of a Thought. If these descendants have no other parents, they will be deleted as well.
    /// </summary>
    /// <param name="t">The thought to remove the children from.</param>
    public void DeleteAllChildrenAndLinks(Thought t)
    {
        if (t is not null)
        {
            //List<Thought> subThoughts = t.EnumerateSubThoughts().ToList();
            List<Thought> descendants = t.Descendants.ToList();
            foreach (Link t1 in t.LinksTo)
            {
                if (t1.To is SeqElement s)
                {
                    DeleteSequence(s);
                }
                t1.Delete();
            }
            foreach (Thought t1 in descendants)
                t1.Delete();
        }
    }

    /// <summary>
    /// Creates a new Thought in the UKS OR returns an existing Thought, based on the label.
    /// </summary>
    /// <param name="label">Label for the thought. Trailing '*' auto-increments to a unique label.</param>
    /// <param name="parent">Optional parent thought or label; defaults to "Unknown" if null.</param>
    /// <param name="source">Optional source thought used to probe existing numbered links.</param>
    /// <returns>The existing or newly created Thought.</returns>
    public Thought? GetOrAddThought(string label, object? parent = null, Thought? source = null)
    {
        if (string.IsNullOrEmpty(label)) return null;

        Thought? thoughtToReturn = ThoughtLabels.GetThought(label);
        Thought? correctParent = null;
        if (parent is string s)
            correctParent = ThoughtLabels.GetThought(s);
        if (parent is Thought t)
            correctParent = t;
        if (correctParent is null)
            correctParent = ThoughtLabels.GetThought("Unknown");

        if (thoughtToReturn is not null)
        {
            if (thoughtToReturn.Parents.Count == 0 &&
                thoughtToReturn.Label.ToLower() != "brainsim" &&
                thoughtToReturn.Label.ToLower() != "thought" &&
                correctParent is not null)
                thoughtToReturn.AddParent(correctParent);
            if (correctParent is not null && correctParent.Label != "Unknown")
            {
                thoughtToReturn.RemoveParent("Unknown");
                thoughtToReturn.AddParent(correctParent);
            }
            if (label.Split('.').Contains("?") && Labeled("?") is Thought conditionalType)
                thoughtToReturn.AddParent(conditionalType);
            AddActionTypeInheritance(thoughtToReturn, label);

            return thoughtToReturn;
        }
        //. are used to indicate attributes to be added
        if (label.Contains(".") && label != "." && !label.Contains(".py"))
        {
            string[] attribs = label.Split(".");
            Thought? baseThought = Labeled(attribs[0]) ?? AddThought(attribs[0], "Unknown");
            Thought? instanceThought = Labeled(label) ?? AddThought(label, baseThought);
            if (instanceThought is not null && baseThought?.Label.ToLower() == "not")
            {
                Thought? parentAttrib = ThoughtLabels.GetThought(attribs[1]);
                if (parentAttrib is not null)
                    instanceThought.AddParent(parentAttrib);
            }
            if (instanceThought is not null)
            {
                for (int i = 1; i < attribs.Length; i++)
                {
                    if (attribs[i] == "?")
                    {
                        Thought? conditionalType = Labeled("?") ?? AddThought("?", Labeled("LinkType"));
                        if (conditionalType is not null)
                            instanceThought.AddParent(conditionalType);
                        continue;
                    }
                    Thought? attrib = Labeled(attribs[i]) ?? AddThought(attribs[i], "Unknown");
                    if (attrib is not null)
                        instanceThought.AddLink("is", attrib);
                }
                AddActionTypeInheritance(instanceThought, label);
            }
            return instanceThought;
        }


        if (correctParent is null) return null;
        //            throw new ArgumentException("GetOrAddThought: could not find parent");

        if (label.EndsWith("*"))
        {
            string baseLabel = label.Substring(0, label.Length - 1);
            //instead of creating a new label, see if the next label for this item already exists and can be reused
            if (source is not null)
            {
                int digit = 0;
                while (source.LinksTo.FindFirst(x => x.LinkType?.Label == baseLabel + digit) is not null) digit++;
                Thought? labeled = ThoughtLabels.GetThought(baseLabel + digit);
                if (labeled is not null)
                    return labeled;
            }
        }

        thoughtToReturn = AddThought(label, correctParent);
        thoughtToReturn.Fire();
        return thoughtToReturn;
    }

    /// <summary>
    /// SET and TEST action types inherit from both the operation and the
    /// relationship they operate on. For example, SET.ref inherits from SET
    /// and ref. The dotted "is" metadata remains descriptive and is not needed
    /// when an action is applied.
    /// </summary>
    private void AddActionTypeInheritance(Thought actionType, string label)
    {
        string[] parts = label.Split('.');
        if (parts.Length < 2) return;
        if (!parts[0].Equals("SET", StringComparison.OrdinalIgnoreCase) &&
            !parts[0].Equals("TEST", StringComparison.OrdinalIgnoreCase))
            return;

        Thought? linkTypeRoot = Labeled("LinkType");
        if (linkTypeRoot is null) return;

        string relationshipLabel = string.Join(".", parts.Skip(1));
        Thought? relationshipType = Labeled(relationshipLabel);
        if (relationshipType is null)
            relationshipType = GetOrAddThought(relationshipLabel, linkTypeRoot);
        else if (!relationshipType.HasAncestor(linkTypeRoot))
            relationshipType.AddParent(linkTypeRoot);

        actionType.AddParent(relationshipType);
    }


    /// <summary>
    /// Finds or creates a subclass from a phrase, attaching attributes as dotted parts.
    /// Example: "has 4" becomes thought {has.4} and [has.4 is 4].
    /// </summary>
    /// <param name="label">Input phrase to process.</param>
    /// <param name="attributesFollow">True if attributes follow the base term; false if they precede it.</param>
    /// <param name="singularize">When true, singularizes non-capitalized words.</param>
    /// <returns>The created or retrieved Thought.</returns>
    public Thought? CreateThoughtFromMultipleAttributes(string label, bool attributesFollow, bool singularize = true)
    {
        if (label.StartsWith("^"))  //if it starts with an ^, it's a sequence
        {
            List<Thought> targets = new();
            string[] targetParts = label[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (string label1 in targetParts)
            {
                if (label1.Length == 1)
                {
                    Thought? letterParent = theUKS.GetOrAddThought("letter", "Object");
                    Thought? t1 = theUKS.GetOrAddThought(label1.ToUpper(), letterParent!);
                    if (t1 is not null) targets.Add(t1);
                }
                else
                {
                    Thought? t1 = label1.StartsWith("w:")
                        ? theUKS.GetOrAddThought(label1, "word")
                        : theUKS.GetOrAddThought(label1);
                    if (t1 is not null) targets.Add(t1);
                }
            }
            string seqLabel = string.Join("", targetParts);
            Thought r1 = (Thought)theUKS.AddSequence(seqLabel, targets, false);
            return r1;

        }
        IPluralize pluralizer = new Pluralizer();
        label = label.Trim();
        string[] attributeParts = label.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (attributeParts.Length == 0 || attributeParts[0].Length == 0) return null;

        for (int i = 0; i < attributeParts.Length; i++)
            if (!char.IsUpper(attributeParts[i][0]) && singularize)
                attributeParts[i] = pluralizer.Singularize(attributeParts[i]);

        string thoughtLabel;
        if (attributesFollow)
        {
            thoughtLabel = attributeParts[0];
            for (int i = 1; i < attributeParts.Length; i++)
                //if (!string.IsNullOrEmpty(tempStringArray[i]))
                thoughtLabel += "." + attributeParts[i];
        }
        else
        {
            int last = attributeParts.Length - 1;
            thoughtLabel = attributeParts[last];
            for (int i = 0; i < last; i++)
                //if (!string.IsNullOrEmpty(tempStringArray[i]))
                thoughtLabel += "." + attributeParts[i];
        }

        //find the base thing so we can assign the parent?  //this is handled in GetOrAddThought now, so we don't need to do it here
        return GetOrAddThought(thoughtLabel);
    }
}
