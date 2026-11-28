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
/// One element of a sequence: a Thought with a FRST link to the sequence's first element, an NXT link to
/// the next element (none on the last), and a VLU link to its value. A value can itself be a sequence,
/// so sequences nest; identical sequences are stored once and shared.
/// </summary>
public class SeqElement : Thought
{
    /// <summary>Creates an unlinked sequence element.</summary>
    public SeqElement(UKS store) : base(store) { }

    /// <summary>The first element of this element's sequence (itself, for the first element).</summary>
    public SeqElement? FRST
    {
        get => Store.FrstType is Thought frst ? LinksToWriteable.FirstOrDefault(x => x.LinkType == frst)?.To as SeqElement : null;
        set => SetSequenceLink("FRST", value);
    }

    /// <summary>The next element of the sequence, or null for the last element.</summary>
    public SeqElement? NXT
    {
        get => Store.NxtType is Thought nxt ? LinksTo.FirstOrDefault(x => x.LinkType == nxt)?.To as SeqElement : null;
        set => SetSequenceLink("NXT", value);
    }

    /// <summary>The element's primary value (a non-metadata VLU target), or null.</summary>
    public Thought? VLU
    {
        get => Store.GetElementValue(this);
        set
        {
            if (value is not null && !ReferenceEquals(value.Store, Store))
                throw new ArgumentException("Sequence values must belong to the same UKS.", nameof(value));
            // Replace the primary value without discarding timing/other metadata.
            bool metadata = value?.HasProperty("isSequenceMetadata") == true;
            foreach (var link in LinksTo.Where(link => link.LinkType == Store.Labeled("VLU") &&
                (link.To?.HasProperty("isSequenceMetadata") == true) == metadata).ToList()) RemoveLink(link);
            if (value is not null)
                AddLink(Store.GetOrAddThought("VLU", "LinkType")!, value);
        }
    }

    /// <summary>Returns the sequence in text-file notation, e.g. ^c a t.</summary>
    public override string ToString() => "^" + string.Join(" ", Store.FlattenSequence(this));

    // Replaces this element's single FRST or NXT link.
    private void SetSequenceLink(string linkTypeLabel, SeqElement? value)
    {
        if (value is not null && !ReferenceEquals(value.Store, Store))
            throw new ArgumentException("Sequence values must belong to the same UKS.", nameof(value));
        foreach (Link old in LinksTo.Where(link => link.LinkType == Store.Labeled(linkTypeLabel)))
            RemoveLink(old);
        if (value is not null)
            AddLink(Store.GetOrAddThought(linkTypeLabel, "LinkType")!, value);
    }
}
