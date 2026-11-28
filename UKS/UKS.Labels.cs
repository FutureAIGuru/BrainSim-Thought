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

// Labels: every Thought is found by its label, which is unique in the store and case-insensitive.
// This is the index from label to Thought, the labels the store makes up for links (unl_...), the core
// link types found by label once and then compared by identity, and the label notation that
// GetOrAddThought expands: dotted compounds (big.dog), paths (capability.^a b c) and numbering (dog*).
public partial class UKS
{
    internal readonly System.Collections.Concurrent.ConcurrentDictionary<string, Thought> Labels = new();

    private long nextGeneratedLabel = 999;

    /// <summary>
    /// Indexes <paramref name="thought"/> under <paramref name="newLabel"/>, replacing its current entry,
    /// and returns the label actually used: a trailing * becomes the next free number, and a label
    /// already in use gets a number appended. An empty label is not indexed.
    /// </summary>
    internal string IndexLabel(string newLabel, Thought thought)
    {
        if (newLabel == "") return newLabel;
        if (Labels.TryGetValue(thought.Label.ToLowerInvariant(), out Thought? previous) && ReferenceEquals(previous, thought))
            Labels.TryRemove(thought.Label.ToLowerInvariant(), out _);
        int number = -1;
        string baseLabel = newLabel;
        if (newLabel.EndsWith('*'))
        {
            number = 0;
            baseLabel = newLabel[..^1].TrimEnd("0123456789".ToCharArray());
            newLabel = baseLabel + number;
        }
        while (!Labels.TryAdd(newLabel.ToLowerInvariant(), thought))
        {
            number++;
            newLabel = baseLabel + number;
        }
        ReserveGeneratedLabel(newLabel);
        return newLabel;
    }

    /// <summary>Removes a label from the index.</summary>
    internal void UnindexLabel(string label)
    {
        if (!string.IsNullOrEmpty(label)) Labels.TryRemove(label.ToLowerInvariant(), out _);
    }

    internal string NextGeneratedLabel()
    {
        string label;
        do label = "unl_" + Interlocked.Increment(ref nextGeneratedLabel).ToString(System.Globalization.CultureInfo.InvariantCulture);
        while (Labels.ContainsKey(label));
        return label;
    }

    internal void ReserveGeneratedLabel(string label)
    {
        if (!label.StartsWith("unl_", StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(label.AsSpan(4), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out int number)) return;
        long current = Interlocked.Read(ref nextGeneratedLabel);
        while (number > current)
        {
            long previous = Interlocked.CompareExchange(ref nextGeneratedLabel, number, current);
            if (previous == current) break;
            current = previous;
        }
    }

    // The link types the store itself relies on, found by label (the index keys are lower case).
    internal Thought? IsAType => CoreType("is-a");

    internal Thought? HasPropertyType => CoreType("hasproperty");

    internal Thought? FrstType => CoreType("frst");

    internal Thought? NxtType => CoreType("nxt");

    internal Thought? VluType => CoreType("vlu");

    private Thought? CoreType(string key) => Labels.TryGetValue(key, out Thought? type) ? type : null;

    internal Thought? ThoughtFromString(string label, string defaultParent, Thought? source = null)
    {
        GetOrAddThought("Thought"); //safety
        GetOrAddThought("Unknown", "Thought"); //safety
        if (string.IsNullOrEmpty(label)) return null;
        if (label == "") return null;
        Thought? t = Labeled(label);

        if (t is null)
        {
            if (Labeled(defaultParent) is null)
            {
                GetOrAddThought(defaultParent, Labeled("Object"), source);
            }
            t = GetOrAddThought(label, defaultParent, source);
        }
        return t;
    }

    // An existing Thought asked for again with a parent: one under Unknown (or a parentless non-root)
    // moves to the given parent, and a ? compound gains the ? marker.
    private Thought Reclassify(Thought existing, string label, Thought? parent)
    {
        // The roots stay roots.
        if (existing.Parents.Count == 0 &&
            existing != Labeled("BrainSim") &&
            existing != Labeled("Thought") &&
            parent is not null)
            existing.AddParent(parent);
        if (parent is not null && parent != Labeled("Unknown"))
        {
            existing.RemoveParent("Unknown");
            existing.AddParent(parent);
        }
        if (label.Split('.').Contains("?") && Labeled("?") is Thought possibleType)
            existing.AddParent(possibleType);
        return existing;
    }

    // A path, capability.^a b c: an ordinary, uncompressed sequence of the values a b c, labeled with
    // the whole label and a child of the capability (so it inherits its properties).
    private SeqElement? AddPath(string label, int pathMarker, Thought? parent)
    {
        Thought? capability = GetOrAddThought(label[..pathMarker]);
        string[] elements = label[(pathMarker + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (capability is null || elements.Length == 0) return null;
        List<Thought> values = new();
        foreach (string element in elements)
        {
            Thought? value = GetOrAddThought(element);
            if (value is null) return null;
            values.Add(value);
        }

        SeqElement sequence = CreateFirstElement("path*", values[0]);
        SeqElement last = sequence;
        foreach (Thought value in values.Skip(1))
            last = InsertAfter(last, value);
        sequence.Label = label;
        sequence.AddParent(capability);
        if (parent is not null && parent != Labeled("Unknown"))
            sequence.AddParent(parent);
        return sequence;
    }

    // A dotted label composes its parts (big.dog), unless it is a file name (a script or an image).
    private static bool IsDottedLabel(string label) =>
        label.Contains('.') && label != "." && !label.Contains(".py") &&
        !new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" }.Contains(Path.GetExtension(label), StringComparer.OrdinalIgnoreCase);

    // A compound, base.attribute...: a child of the base and of each attribute (big.dog is-a big and
    // dog). For an action type such as is.not.?, the ordinary compound is.not is made too and becomes a
    // parent, so the type it stands for is a Thought of its own and never has to be rebuilt from text.
    private Thought? AddCompound(string label)
    {
        string[] attribs = label.Split(".");
        List<Thought> components = new();
        foreach (string componentLabel in attribs)
        {
            Thought? component = Labeled(componentLabel) ?? AddThought(componentLabel, "Unknown");
            if (component is not null) components.Add(component);
        }
        Thought? ordinaryCompound = null;
        if (components.Any(component => component.HasProperty("isPossible") || component.HasProperty("setRelationship")))
        {
            List<Thought> ordinaryComponents = components.Where(component =>
                !component.HasProperty("isPossible") && !component.HasProperty("setRelationship")).ToList();
            if (ordinaryComponents.Count > 1)
                ordinaryCompound = GetOrAddThought(string.Join(".", ordinaryComponents.Select(component => component.Label)));
        }
        Thought? baseThought = Labeled(attribs[0]) ?? AddThought(attribs[0], "Unknown");
        Thought? compound = Labeled(label) ?? AddThought(label, baseThought);
        for (int i = 1; i < attribs.Length; i++)
        {
            // Compound components contribute through ordinary inheritance.
            Thought? attrib = Labeled(attribs[i]) ?? AddThought(attribs[i], "Unknown");
            if (attrib is not null) compound.AddParent(attrib);
        }
        if (ordinaryCompound is not null)
            compound.AddParent(ordinaryCompound);
        return compound;
    }

    // For label*, the first numbered Thought (label0, label1, ...) that source has no link of, if it
    // exists already: each of source's links gets its own number, reusing numbers across sources.
    private Thought? ReusableNumberedThought(string label, Thought source)
    {
        string baseLabel = label[..^1];
        int digit = 0;
        while (source.LinksTo.FirstOrDefault(x => x.LinkType?.Label == baseLabel + digit) is not null) digit++;
        return Labeled(baseLabel + digit);
    }
}
