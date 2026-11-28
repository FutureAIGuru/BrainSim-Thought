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
/// Determines whether links are in conflict with one another.
/// TODO: the entire process can be simplified and made more accurate.
/// </summary>
public partial class UKS
{
    /// <summary>Checks whether two links conflict under this store's exclusivity rules.</summary>
    public bool LinksAreExclusive(Link r1, Link r2)
    {
        //are two links mutually exclusive?
        //yes if they differ by a single component property
        //   which is exclusive on a property
        //      which source and target are the ancestor of one another

        //TODO:  expand this to handle
        //  is lessthan is greaterthan
        //  several other cases

        if (r1.To != r2.To && (r1.To is null || r2.To is null)) return false;
        if (r1.To == r2.To && r1.LinkType == r2.LinkType) return false;
        // Historical assertions can describe different states of the same Thought.
        if (r1.LinkType?.HasProperty("isHistorical") == true || r2.LinkType?.HasProperty("isHistorical") == true) return false;
        // Rule descriptions and possible links are not competing assertions.
        if (r1.LinkType?.HasProperty("isPossible") == true || IsConditionalLinkType(r1.LinkType)) return false;
        if (r2.LinkType?.HasProperty("isPossible") == true || IsConditionalLinkType(r2.LinkType)) return false;

        if (LinkTypesAreExclusive(r1, r2))
            return true;

        // Exclusive targets compete for the same link type. A location
        // assertion can coexist with an activity involving another location.
        bool sameLinkType = r1.LinkType == r2.LinkType;

        if (r1.From is not null && r2.From is not null &&
            (r1.From == r2.From ||
            r1.From.HasAncestor(r2.From) ||
            r2.From.HasAncestor(r1.From) ||
            FindCommonParents(r1.From, r2.From).Count > 0))
        {

            IReadOnlyList<Thought> r1LinkiProps = r1.LinkType is { } r1LinkType ? r1LinkType.DirectAttributes().Concat(r1LinkType.Parents).Distinct().ToList() : Array.Empty<Thought>();
            IReadOnlyList<Thought> r2LinkProps = r2.LinkType is { } r2LinkType ? r2LinkType.DirectAttributes().Concat(r2LinkType.Parents).Distinct().ToList() : Array.Empty<Thought>();
            //handle case with properties of the target
            if (r1.To is not null && r1.To == r2.To &&
                (r1.To.HasAncestor(r2.To) || r2.To.HasAncestor(r1.To)))
            {
                IReadOnlyList<Thought> r1TargetProps = r1.To.DirectAttributes();
                IReadOnlyList<Thought> r2TargetProps = r2.To.DirectAttributes();
                foreach (Thought t1 in r1TargetProps)
                    foreach (Thought t2 in r2TargetProps)
                    {
                        List<Thought> commonParents = FindCommonParents(t1, t2);
                        foreach (Thought t3 in commonParents)
                        {
                            if (HasProperty(t3, "isexclusive") || HasProperty(t3, "allowMultiple"))
                                return true;
                        }
                    }
            }
            //handle case with conflicting targets
            if (r1.To is not null && r2.To is not null)
            {
                List<Thought> commonParents = FindCommonParents(r1.To!, r2.To!);
                foreach (Thought t3 in commonParents)
                {
                    if ((sameLinkType && HasProperty(t3, "isexclusive")) || HasProperty(t3, "allowMultiple"))
                        return true;
                }
            }
            if (r1.To == r2.To)
            {
                foreach (Thought t1 in r1LinkiProps)
                    foreach (Thought t2 in r2LinkProps)
                    {
                        if (t1 == t2) continue;
                        List<Thought> commonParents = FindCommonParents(t1, t2);
                        foreach (Thought t3 in commonParents)
                        {
                            if (HasProperty(t3, "isexclusive") || HasProperty(t3, "allowMultiple"))
                                return true;
                        }
                    }
            }
            //if source and target are the same and one contains a number, assume that the other contains "1"
            // fido has leg -> fido has 1 leg  
            bool hasNumber1 = (r1LinkiProps.FirstOrDefault(x => x.HasAncestor("number")) is not null);
            bool hasNumber2 = (r2LinkProps.FirstOrDefault(x => x.HasAncestor("number")) is not null);
            if (r1.To == r2.To &&
                (hasNumber1 || hasNumber2))
                return true;

            //if one of the linkypes contains negation and not the other
            bool r1Negative = IsNegative(r1.LinkType);
            bool r2Negative = IsNegative(r2.LinkType);
            if (r1.From is not null && r2.From is not null &&
                (r1.From == r2.From || r1.From.Ancestors.Contains(r2.From) ||
                r2.From.Ancestors.Contains(r1.From)) &&
                r1.To == r2.To &&
                r1Negative != r2Negative)
                return true;
        }
        else
        {
            // Unrelated sources: only targets with an exclusive common parent conflict.
            List<Thought> commonParents = FindCommonParents(r1.To!, r2.To!);
            foreach (Thought t3 in commonParents)
            {
                if (sameLinkType && HasProperty(t3, "isexclusive"))
                    return true;
                if (HasProperty(t3, "allowMultiple") && r1.From != r2.From)
                    return true;
            }

        }
        return false;
    }

    private bool LinkTypesAreExclusive(Link r1, Link r2)
    {
        if (r1.LinkType is null || r2.LinkType is null) return false;
        if (r1.To == r2.To && IsNegative(r1.LinkType) != IsNegative(r2.LinkType))
            return true;
        return false;
    }

    private static bool IsNegative(Thought? t)
    {
        if (t is null) return false;
        // Old knowledge may attach the marker as an attribute instead of an is-a parent.
        // Both forms obtain negation from the marker's property, never its label.
        return t.HasProperty("isNegative") || t.DirectAttributes().Any(attribute => attribute.HasProperty("isNegative"));
    }

    // A property the Thought holds itself, not by inheritance.
    private bool HasProperty(Thought t, string propertyLabel) =>
        HasPropertyType is Thought hasProperty && Labeled(propertyLabel) is Thought property &&
        t.LinksTo.Any(link => link.LinkType == hasProperty && link.To == property);

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
}
