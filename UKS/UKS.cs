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
    static private List<Thought> uKSList = new();// { Capacity = 1000000, };

    //This is a reformatted temporary copy of the UKS which used internally during the save and restore process to 
    //break circular links by storing index values instead of actual links Note the use of SThought instead of Thought
    private List<sThought> UKSTemp = new();

    /// <summary>
    /// Occasionally a list of all the Thoughts in the UKS is needed. This is READ ONLY.
    /// There is only one (shared) list for the App.
    /// </summary>
    public List<Thought> AtomicThoughts { get => uKSList; }

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

    /// <summary>
    /// This is a primitive method needed only to create ROOT Thoughts which have no parents.
    /// </summary>
    /// <param name="label">Label of the thought to create.</param>
    /// <param name="parent">Optional parent thought (may be null).</param>
    /// <returns>The newly created thought.</returns>
    public virtual Thought AddThought(string label, Thought? parent)
    {
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
    public Thought Labeled(string label)
    {
        Thought retVal = ThoughtLabels.GetThought(label);
        return retVal;
    }


    private bool HasProperty(Thought t, string propertyName)
    {
        if (t is null) return false;
        var v = t.LinksTo;
        if (v.FindFirst(x => x.To?.Label.ToLower() == propertyName.ToLower() && x.LinkType.Label == "hasProperty") is not null) return true;
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
            if (r1.To is Link  rt3 && r2.To is Link rt4)
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
    public Link GetLink(Thought from, Thought linkType, Thought to)
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
    public Link GetLink(Link r)
    {
        foreach (Link  r1 in r.From?.LinksTo)
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
        foreach (Link r1 in r.From?.LinksTo)
        {
            if (r.LinkType == r1.LinkType && r.To == r1.To)
                retVal.Add(r1);
        }
        return retVal;
    }

    /// <summary>
    /// Recursively removes all the descendants of a Thought. If these descendants have no other parents, they will be deleted as well.
    /// </summary>
    /// <param name="t">The thought to remove the children from.</param>
    public void DeleteAllChildren(Thought t)
    {
        if (t is not null)
        {
            List<Thought> subThoughts = t.EnumerateSubThoughts().ToList();
            foreach (Link t1 in subThoughts)
            {
                if (t1.To is SeqElement s)
                {
                    DeleteSequence(s);
                }
                else
                {
                    t1.Delete();
                }
            }
        }
    }

    /// <summary>
    /// Creates a new Thought in the UKS OR returns an existing Thought, based on the label.
    /// </summary>
    /// <param name="label">Label for the thought. Trailing '*' auto-increments to a unique label.</param>
    /// <param name="parent">Optional parent thought or label; defaults to "Unknown" if null.</param>
    /// <param name="source">Optional source thought used to probe existing numbered links.</param>
    /// <returns>The existing or newly created Thought.</returns>
    public Thought GetOrAddThought(string label, object parent = null, Thought source = null)
    {
        Thought thoughtToReturn = null;

        if (string.IsNullOrEmpty(label)) return thoughtToReturn;

        thoughtToReturn = ThoughtLabels.GetThought(label);
        if (thoughtToReturn is not null) return thoughtToReturn;

        //. are used to indicate attributes to be added
        if (label.Contains(".") && label != "." && !label.Contains(".py"))
        {
            string[] attribs = label.Split(".");
            Thought baseThought = Labeled(attribs[0]);
            if (baseThought is null) baseThought = AddThought(attribs[0], "Unknown");
            Thought instanceThought = Labeled(label);
            if (instanceThought is null)
            {
                instanceThought = AddThought(label, baseThought);
            }
            for (int i = 1; i < attribs.Length; i++)
            {
                Thought attrib = Labeled(attribs[i]);
                if (attrib is null)
                    attrib = AddThought(attribs[i], "Unknown");
                instanceThought.AddLink("is", attrib);
            }
            return instanceThought;
        }

        Thought correctParent = null;
        if (parent is string s)
            correctParent = ThoughtLabels.GetThought(s);
        if (parent is Thought t)
            correctParent = t;
        if (correctParent is null)
            correctParent = ThoughtLabels.GetThought("Unknown");

        if (correctParent is null) return null;
//            throw new ArgumentException("GetOrAddThought: could not find parent");

        if (label.EndsWith("*"))
        {
            string baseLabel = label.Substring(0, label.Length - 1);
            Thought newParent = ThoughtLabels.GetThought(baseLabel);
            //instead of creating a new label, see if the next label for this item already exists and can be reused
            if (source is not null)
            {
                int digit = 0;
                while (source.LinksTo.FindFirst(x => x.LinkType.Label == baseLabel + digit) is not null) digit++;
                Thought labeled = ThoughtLabels.GetThought(baseLabel + digit);
                if (labeled is not null)
                    return labeled;
            }
        }

        thoughtToReturn = AddThought(label, correctParent);
        return thoughtToReturn;
    }


    /// <summary>
    /// Finds or creates a subclass from a phrase, attaching attributes as dotted parts.
    /// Example: "has 4" becomes thought {has.4} and [has.4 is 4].
    /// </summary>
    /// <param name="label">Input phrase to process.</param>
    /// <param name="attributesFollow">True if attributes follow the base term; false if they precede it.</param>
    /// <param name="singularize">When true, singularizes non-capitalized words.</param>
    /// <returns>The created or retrieved Thought.</returns>
    public Thought CreateThoughtFromMultipleAttributes(string label, bool attributesFollow, bool singularize = true)
    {
        if (label.StartsWith("^"))  //if it starts with an ^, it's a sequence
        {
            List<Thought> targets = new();
            string[] targetParts = label[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (string label1 in targetParts)
            {
                Thought t1 = theUKS.GetOrAddThought(label1);
                targets.Add(t1);
            }
            Thought r1 = (Thought)theUKS.CreateRawSequence(targets,"thequery");
            return r1;

        }
        IPluralize pluralizer = new Pluralizer();
        label = label.Trim();
        string[] tempStringArray = label.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tempStringArray.Length == 0 || tempStringArray[0].Length == 0) return null;

        for (int i = 0; i < tempStringArray.Length; i++)
            if (!char.IsUpper(tempStringArray[i][0]) && singularize)
                tempStringArray[i] = pluralizer.Singularize(tempStringArray[i]);

        string thoughtLabel;
        if (attributesFollow)
        {
            thoughtLabel = tempStringArray[0];
            for (int i = 1; i < tempStringArray.Length; i++)
                if (!string.IsNullOrEmpty(tempStringArray[i]))
                    thoughtLabel += "." + tempStringArray[i];
        }
        else
        {
            int last = tempStringArray.Length - 1;
            thoughtLabel = tempStringArray[last];
            for (int i = 0; i < last; i++)
                if (!string.IsNullOrEmpty(tempStringArray[i]))
                    thoughtLabel += "." + tempStringArray[i];
        }

        Thought t = GetOrAddThought(thoughtLabel);
        return t;
    }
}
