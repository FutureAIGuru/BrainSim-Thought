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
/// A Thought, or the label of one, passed to a UKS method: <c>uks.AddStatement("Fido", isA, "dog")</c>.
/// Converting a string only remembers the label; the method that receives it looks the label up
/// in its own store, so a label always means a Thought of the store being called.
/// </summary>
/// <remarks>
/// Pass <c>default</c>, not a bare <c>null</c>, for "no Thought": a null literal could be either
/// a Thought or a string, so the compiler rejects it as ambiguous.
/// </remarks>
public readonly struct ThoughtRef
{
    private readonly Thought? thought;
    private readonly string? label;

    private ThoughtRef(Thought? thought, string? label)
    {
        this.thought = thought;
        this.label = label?.Trim();
    }

    /// <summary>A reference to a Thought.</summary>
    public static implicit operator ThoughtRef(Thought? thought) => new(thought, null);
    /// <summary>A reference by label, looked up in the store of the method receiving it.</summary>
    public static implicit operator ThoughtRef(string? label) => new(null, label);

    /// <summary>True when it names no Thought (null, or an empty label).</summary>
    public bool IsEmpty => thought is null && string.IsNullOrEmpty(label);

    /// <summary>The Thought's label, or the label given.</summary>
    public override string ToString() => thought?.Label ?? label ?? "";

    /// <summary>
    /// The existing Thought, or null for an empty reference or an unknown label. Nothing is created.
    /// A Thought from another store is a programming error and throws.
    /// </summary>
    public Thought? Find(UKS store)
    {
        if (thought is not null) return InStore(store);
        return string.IsNullOrEmpty(label) ? null : store.Labeled(label);
    }

    /// <summary>
    /// The existing Thought that an operation needs, or null for an empty reference or an unknown
    /// label, in which case the operation does nothing. Resolves like <see cref="Find"/>; kept
    /// separate so that an unknown label here can later be reported.
    /// </summary>
    internal Thought? Require(UKS store) => Find(store);

    /// <summary>
    /// The existing Thought, or a new one for an unknown label, created under
    /// <paramref name="defaultParent"/>. Null only for an empty reference.
    /// </summary>
    internal Thought? GetOrAdd(UKS store, string defaultParent, Thought? source = null)
    {
        if (thought is not null) return InStore(store);
        return string.IsNullOrEmpty(label) ? null : store.ThoughtFromString(label, defaultParent, source);
    }

    private Thought InStore(UKS store) =>
        ReferenceEquals(thought!.Store, store) ? thought
            : throw new ArgumentException($"{thought.Label} belongs to another UKS.");
}
