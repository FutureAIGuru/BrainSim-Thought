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
/// Contains a collection of Thoughts linked by Links to implement Common Sense and general knowledge.
/// This file has methods related to determining if two links are mutually exclusive, and other related methods.
/// TODO: the entire process can be simplified an made more accurate.
/// </summary>
public partial class UKS
{

    private bool ThoughtInTree(Thought t1, Thought t2)
    {
        if (t2 is null) return false;
        if (t1 is null) return false;
        if (t1 == t2) return true;
        if (t1.AncestorsWithSelf.Contains(t2)) return true;
        if (t2.AncestorsWithSelf.Contains(t1)) return true;
        return false;
    }


    //TODO: This method has gotten out of hand and needs a rewrite
    private bool LinksAreExclusive(Thought r1, Thought r2)
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
        //TODO Verify this:
        if (r1.HasProperty("isResult")) return false;
        if (r1.HasProperty("isCondition")) return false;
        if (r2.HasProperty("isResult")) return false;
        if (r2.HasProperty("isCondition")) return false;

        if (r1.From == r2.From ||
            r1.From.AncestorsWithSelf.Contains(r2.From) ||
            r2.From.AncestorsWithSelf.Contains(r1.From) ||
            FindCommonParents(r1.From, r1.From).Count() > 0)
        {

            IReadOnlyList<Thought> r1LinkiProps = r1.LinkType.GetAttributes();
            IReadOnlyList<Thought> r2LinkProps = r2.LinkType.GetAttributes();
            //handle case with properties of the target
            if (r1.To is not null && r1.To == r2.To &&
                (r1.To.AncestorsWithSelf.Contains(r2.To) ||
                r2.To.AncestorsWithSelf.Contains(r1.To) ||
                FindCommonParents(r1.To, r1.To).Count() > 0))
            {
                IReadOnlyList<Thought> r1TargetProps = r1.To.GetAttributes();
                IReadOnlyList<Thought> r2TargetProps = r2.To.GetAttributes();
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
                List<Thought> commonParents = FindCommonParents(r1.To, r2.To);
                foreach (Thought t3 in commonParents)
                {
                    if (HasProperty(t3, "isexclusive") || HasProperty(t3, "allowMultiple"))
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
            bool hasNumber1 = (r1LinkiProps.FindFirst(x => x.HasAncestor("number")) is not null);
            bool hasNumber2 = (r2LinkProps.FindFirst(x => x.HasAncestor("number")) is not null);
            if (r1.To == r2.To &&
                (hasNumber1 || hasNumber2))
                return true;

            //if one of the linkypes contains negation and not the other
            Thought r1Not = r1LinkiProps.FindFirst(x => x.Label == "not" || x.Label == "no");
            Thought r2Not = r2LinkProps.FindFirst(x => x.Label == "not" || x.Label == "no");
            if ((r1.From.Ancestors.Contains(r2.From) ||
                r2.From.Ancestors.Contains(r1.From)) &&
                r1.To == r2.To &&
                (r1Not is null && r2Not is not null || r1Not is not null && r2Not is null))
                return true;
        }
        else
        {
            //this appears to duplicate code at line 226
            List<Thought> commonParents = FindCommonParents(r1.To, r2.To);
            foreach (Thought t3 in commonParents)
            {
                if (HasProperty(t3, "isexclusive"))
                    return true;
                if (HasProperty(t3, "allowMultiple") && r1.From != r2.From)
                    return true;
            }

        }
        return false;
    }

    private bool LinkTypesAreExclusive(Thought r1, Thought r2)
    {
        IReadOnlyList<Thought> r1RelProps = r1.LinkType.GetAttributes();
        IReadOnlyList<Thought> r2RelProps = r2.LinkType.GetAttributes();
        Thought r1Not = r1RelProps.FindFirst(x => x.Label == "not" || x.Label == "no");
        Thought r2Not = r2RelProps.FindFirst(x => x.Label == "not" || x.Label == "no");
        if (r1.To == r2.To &&
            (r1Not is null && r2Not is not null || r1Not is not null && r2Not is null))
            return true;
        return false;
    }

    private bool HasAttribute(Thought t, string name)
    {
        if (t is null) return false;
        foreach (Thought r in t.LinksTo)
        {
            if (r.LinkType is not null && r.LinkType.Label == "is" && r.To.Label == name)
                return true;
        }
        return false;
    }

    private Thought ThoughtFromString(string label, string defaultParent, Thought source = null)
    {
        GetOrAddThought("Thought"); //safety
        GetOrAddThought("Unknown", "Thought"); //safety
        if (string.IsNullOrEmpty(label)) return null;
        if (label == "") return null;
        Thought t = Labeled(label);

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

    private Thought ThoughtFromObject(object o, string parentLabel = "", Thought source = null)
    {
        if (parentLabel == "")
            parentLabel = "Unknown";
        if (o is string s3)
            return ThoughtFromString(s3.Trim(), parentLabel, source);
        else if (o is Thought t3)
            return t3;
        else if (o is null)
            return null;
        else
            return null;
    }
}
