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
using System.Xml;

namespace UKS;

public partial class UKS
{
    public void CreateMinimumStructureForTests()
    {
        AtomicThoughts.Clear();
        ClearSequenceCache();
        ThoughtLabels.ClearLabelList();
        AddThought("Thought", null);
        Thought isA = AddThought("is-a", null);
        Thought linkType = AddThought("LinkType", "Thought");
        isA.AddParent(linkType);
        GetOrAddThought("Unknown", "Thought");
        GetOrAddThought("VLU", "LinkType");
        GetOrAddThought("NXT", "LinkType");
        GetOrAddThought("FRST", "LinkType");
    }


    public void CreateInitialStructure()
    {
        //this hack is needed to preserve the info relating to module layout
        for (int i = 0; i < AtomicThoughts.Count; i++)
        {
            Thought t = AtomicThoughts[i];
            if (t.Label == "BrainSim") continue;
            if (t.HasAncestor("BrainSim")) continue;
            if (t.Label == "is-a") continue;
            if (t.Label == "hasAttribute") continue;

            t.Delete();
            i--;
        }

        //This deletes all the labels then adds back in the ones still in the AtomicThoughts list

        ThoughtLabels.ClearLabelList();
        Thought.ClearRecentlyFiredQueue();
        foreach (Thought t in AtomicThoughts)
            ThoughtLabels.AddThoughtLabel(t.Label, t);
        ClearSequenceCache();

        //Bootstrapping is needed for is-a, Unknown, and the root: Thought
        //because AddStatement and GetOrAddThought won't work without them (legacy GetOrAddThing alias removed)
        //Thought
        if (Labeled("Thought") is null) AddThought("Thought", null);
        Thought isA = Labeled("is-a");
        if (isA is null) isA = AddThought("is-a", null);
        Thought hasChild = Labeled("has-child");
        if (hasChild is null) hasChild = AddThought("has-child", null);
        Thought linkType = AddThought("LinkType", "Thought");
        isA.AddParent(linkType);
        hasChild.AddParent(linkType);
        GetOrAddThought("Unknown", "Thought");

        GetOrAddThought("Abstract", "Thought");
        GetOrAddThought("Object", "Thought");
        GetOrAddThought("Action", "Thought");
        GetOrAddThought("Link", "Thought");
        GetOrAddThought("LinkType", "Thought");
        GetOrAddThought("Thought", "Thought");
        GetOrAddThought("is-a", "LinkType");
        GetOrAddThought("inverseOf", "LinkType");
        GetOrAddThought("hasProperty", "LinkType");
        GetOrAddThought("is", "LinkType");

        AddStatement("has-child", "inverseOf", "is-a");
        AddStatement("hasAttribute", "is-a", "LinkType");
        AddStatement("can", "is-a", "LinkType");
        AddStatement("mostRecent", "is-a", "LinkType");
        AddStatement("contains", "is-a", "LinkType");
        AddStatement("is-part-of", "is-a", "LinkType");
        AddStatement("contains", "inverseOf", "is-part-of");
        AddStatement("has", "is-a", "LinkType");

        //properties are internal capabilities of Thoughts
        AddStatement("Property", "is-a", "LinkType");
        AddStatement("isExclusive", "is-a", "Property");
        AddStatement("isTransitive", "is-a", "Property");
        AddStatement("isInstance", "is-a", "Property");
        AddStatement("isCommutative", "is-a", "Property");
        AddStatement("allowMultiple", "is-a", "Property");
        AddStatement("inheritable", "is-a", "Property");
        AddStatement("isCondition", "is-a", "Property");
        AddStatement("isResult", "is-a", "Property");

        //sequence search options
        AddStatement("Wildcard", "is-a", "Thought");
        AddStatement("??", "is-a", "Wildcard");
        AddStatement("w:??", "is-a", "Wildcard");
        AddStatement("SearchOption", "is-a", "Property");
        AddStatement("SequenceSearchOption", "is-a", "SearchOption");
        AddStatement("mustMatchFirst", "is-a", "SequenceSearchOption");
        AddStatement("mustMatchLast", "is-a", "SequenceSearchOption");
        AddStatement("allowWildcards", "is-a", "SequenceSearchOption");
        AddStatement("allowNestedSequences", "is-a", "SequenceSearchOption");
        AddStatement("allowCircularSearch", "is-a", "SequenceSearchOption");
        AddStatement("allowOutOfOrder", "is-a", "SequenceSearchOption");
        AddStatement("preferFirstLast", "is-a", "SequenceSearchOption");
        AddStatement("allowPartialMatch", "is-a", "SequenceSearchOption");
        AddStatement("SequenceSearchOptions", "is-a", "Thought");
        AddStatement("ExactSequenceSearch", "is-a", "SequenceSearchOptions");
        AddStatement("TemplateSequenceSearch", "is-a", "ExactSequenceSearch");
        AddStatement("MelodySearchOptions", "is-a", "SequenceSearchOptions");
        AddStatement("OrderSearchOptions", "is-a", "SequenceSearchOptions");
        AddStatement("ExactSequenceSearch", "hasProperty", "mustMatchFirst");
        AddStatement("ExactSequenceSearch", "hasProperty", "mustMatchLast");
        AddStatement("ExactSequenceSearch", "hasProperty", "allowNestedSequences");
        AddStatement("TemplateSequenceSearch", "hasProperty", "allowWildcards");
        AddStatement("MelodySearchOptions", "hasProperty", "allowNestedSequences");
        AddStatement("MelodySearchOptions", "hasProperty", "preferFirstLast");
        AddStatement("OrderSearchOptions", "hasProperty", "mustMatchFirst");
        AddStatement("OrderSearchOptions", "hasProperty", "mustMatchLast");
        AddStatement("OrderSearchOptions", "hasProperty", "allowNestedSequences");

        //colors
        AddStatement("color", "is-a", "Abstract");
        AddStatement("color", "hasProperty", "isExclusive");
        AddStatement("red", "is-a", "color");
        AddStatement("orange", "is-a", "color");
        AddStatement("yellow", "is-a", "color");
        AddStatement("green", "is-a", "color");
        AddStatement("blue", "is-a", "color");
        AddStatement("purple", "is-a", "color");
        AddStatement("brown", "is-a", "color");
        AddStatement("pink", "is-a", "color");
        AddStatement("black", "is-a", "color");
        AddStatement("white", "is-a", "color");
        AddStatement("gray", "is-a", "color");

        //underlying properties
        AddStatement("is-a", "hasProperty", "isTransitive");
        AddStatement("is-a", "hasProperty", "inheritable");
        AddStatement("has", "hasProperty", "isTransitive");
        AddStatement("has", "hasProperty", "inheritable");

        //Clauses
        AddStatement("ClauseType", "is-a", "LinkType");
        AddStatement("IF", "is-a", "ClauseType");
        AddStatement("BECAUSE", "is-a", "ClauseType");
        AddStatement("AFTER", "is-a", "ClauseType");
        AddStatement("BEFORE", "is-a", "ClauseType");
        AddStatement("BEFORE", "inverseOf", "AFTER");
        AddStatement("NXT", "is-a", "ClauseType");
        AddStatement("VLU", "is-a", "ClauseType");
        AddStatement("AND", "is-a", "ClauseType");
        AddStatement("OR", "is-a", "ClauseType");
        AddStatement("NOT", "is-a", "ClauseType");
        AddStatement("NO", "is-a", "ClauseType");
        AddStatement("FRST", "is-a", "ClauseType");

        //Numbers
        GetOrAddThought("number", "abstract");
        AddStatement("Comparison", "is-a", "LinkType");
        AddStatement("order", "is-a", "Comparison");
        AddStatement("greaterThan", "is-a", "Comparison");
        AddStatement("greaterThan", "hasProperty", "isTransitive");
        AddStatement("lessThan", "inverseOf", "greaterThan");
        AddStatement("lessThan", "is-a", "Comparison");
        AddStatement("number", "hasProperty", "isExclusive");
        GetOrAddThought("digit", "number");
        GetOrAddThought("isSimilarTo", "Comparison");
        AddStatement("isSimilarTo", "hasProperty", "isCommutative");
        AddStatement("hasDigit", "is-a", "has");


       //put in digits
        GetOrAddThought("some", "number");
        GetOrAddThought("many", "number");
        GetOrAddThought("none", "number");
        GetOrAddThought("-", "digit");
        GetOrAddThought(".", "digit");
        for (int i = 0; i < 10; i++)
            GetOrAddThought(i.ToString(), "digit");
        for (int i = 9; i > 0; i--)
            AddStatement(i.ToString(), "greaterThan", (i - 1).ToString());
        AddSequenceAndLink("digit", "order", new List<Thought> { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9" });

        //demo to add PI to the structure
        AddStatement("pi", "is-a", "number");
        AddSequenceAndLink("pi", "hasDigit", new List<Thought> { "3", ".", "1", "4", "1", "5", "9" });

        //put in letters
        GetOrAddThought("letter", "Abstract");
        List<Thought> theAlphabet = new();
        for (char c = 'A'; c <= 'Z'; c++)
        {
            theAlphabet.Add(GetOrAddThought("l:"+c.ToString(), "Letter"));
        }
        GetOrAddThought("alphabet", "abstract");
        AddSequenceAndLink("alphabet", "order", theAlphabet);

        AddBrainSimConfigSectionIfNeeded();
    }

    void AddBrainSimConfigSectionIfNeeded()
    {
        if (Labeled("BrainSim") is null)
            AddThought("BrainSim", null);
        GetOrAddThought("AvailableModule", "BrainSim");
        GetOrAddThought("ActiveModule", "BrainSim");
    }
}
