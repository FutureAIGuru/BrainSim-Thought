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

using System;
using System.Collections.Generic;
using System.Linq;
using UKS;

namespace BrainSimulator.Modules;

/// <summary>
/// Discovers what the positions of a learned template do, and then what the
/// words which fill those positions are. Nothing here starts from English:
/// positions are told apart by how they sit relative to one another, positions
/// which do the same thing are merged by the populations which fill them, and
/// only the final grounding step gives a role a name — read off the part of an
/// action that role supplies.
/// </summary>
public partial class ModuleText
{
    /// <summary>
    /// One learned template read as an ordered structure. The interpretation
    /// arrays are filled in as function words, then roles, are discovered.
    /// </summary>
    private sealed class TemplateStructure
    {
        public Thought Template;
        public List<Thought> Elements;
        public bool[] IsSlot;
        public bool[] Introduces;
        public string[] Descriptors;
        public Thought[] Roles;
        public int SeparatorPosition = -1;
    }

    /// <summary>
    /// The two closed populations which give a template its shape: values which
    /// introduce a following position, and values which stand between the two
    /// sides of the template.
    /// </summary>
    private sealed class FunctionWords
    {
        public HashSet<Thought> Introducers = new();
        public HashSet<Thought> Separators = new();
    }

    /// <summary>
    /// Runs role and category discovery over the templates already learned.
    /// Repeating the pass over unchanged templates reaches the same conclusions
    /// without adding anything to the UKS.
    /// </summary>
    /// <returns>The number of distinct roles which survived coalescing.</returns>
    public static int DiscoverGrammaticalRoles(
        int minFunctionWordTemplates = 2,
        float minRoleOverlap = 0.5f)
    {
        var theUKS = MainWindow.theUKS;
        List<TemplateStructure> templates = ReadTemplateStructures();
        if (templates.Count == 0) return 0;

        FunctionWords functionWords = DiscoverFunctionWords(templates, minFunctionWordTemplates);
        foreach (TemplateStructure structure in templates)
            MarkStructuralPositions(structure, functionWords);

        Dictionary<Thought, HashSet<Thought>> populations = AssignRoleCandidates(templates);

        // Positions on opposite sides of the separator are never the same role
        // however alike their populations, so each side is coalesced on its own.
        // Without this the population of things which act and the population of
        // things acted upon would collapse into a single role.
        foreach (var partition in populations
            .GroupBy(entry => SideOf(DescriptorOf(entry.Key)))
            .ToList())
        {
            Dictionary<Thought, HashSet<Thought>> group =
                partition.ToDictionary(entry => entry.Key, entry => entry.Value);
            theUKS.CoalesceSimilarRoles(group, minRoleOverlap);
        }

        ResolveRolesFromDescriptors(templates);
        RecoverFrozenPositions(templates);
        WriteRoleSequences(templates);
        GroundRolesInActions(templates);
        DeriveLexicalCategories(templates, functionWords);
        DiscoverNumberRelation();
        return theUKS.GetSlotRoleRoot().Children.Count;
    }

    /// <summary>
    /// Learns the relationship between singular and plural word classes from the
    /// spellings the words already carry, rather than from a built-in
    /// pluralizer. The suffix which most often turns one noun into another is
    /// taken as the number transformation, and every noun formed that way is
    /// recognized as the plural of its singular: it denotes the same thing, and
    /// is itself a thing.
    ///
    /// This is what lets a plural seen only as a complement — "dogs are
    /// animals", where "animals" never appears as a subject — be understood as a
    /// thing rather than a quality, which in turn tells that classification
    /// apart from the assertion "dogs are brown".
    /// </summary>
    /// <returns>The number of singular/plural pairs related.</returns>
    public static int DiscoverNumberRelation(int minPairs = 3)
    {
        var theUKS = MainWindow.theUKS;
        Thought nounCategory = theUKS.Labeled("noun");
        Thought wordRoot = theUKS.Labeled("Word");
        if (nounCategory is null || wordRoot is null) return 0;

        Dictionary<string, Thought> vocabulary = wordRoot.Children
            .Where(word => word.Label.StartsWith("w:", StringComparison.Ordinal))
            .GroupBy(WordLabel, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        List<Thought> nouns = nounCategory.Children
            .Where(member => member is not SeqElement && !member.HasAncestor("Wildcard"))
            .ToList();
        HashSet<string> nounLabels = nouns.Select(WordLabel).ToHashSet(StringComparer.Ordinal);

        // The transformation is whatever most often carries one known noun onto
        // another. In English this discovers "s"; nothing here assumes it, and a
        // corpus in another language would settle on a different ending.
        Dictionary<string, int> suffixCounts = new(StringComparer.Ordinal);
        foreach (string singular in nounLabels)
            foreach (string longer in nounLabels)
                if (longer.Length > singular.Length &&
                    longer.StartsWith(singular, StringComparison.Ordinal))
                {
                    string suffix = longer[singular.Length..];
                    suffixCounts[suffix] = suffixCounts.GetValueOrDefault(suffix) + 1;
                }
        if (suffixCounts.Count == 0) return 0;

        var dominant = suffixCounts.OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal).First();
        if (dominant.Value < minPairs) return 0;
        string pluralSuffix = dominant.Key;

        // The discovered rule is kept as ordinary knowledge, not left implicit
        // in this method.
        theUKS.GetOrAddThought("NumberTransform", "LanguageElement");
        theUKS.GetOrAddThought("pluralSuffix:" + pluralSuffix, "NumberTransform");
        Thought meansType = theUKS.GetOrAddThought("means", "LinkType");
        Thought pluralOfType = theUKS.GetOrAddThought("pluralOf", "LinkType");
        Thought verbCategory = theUKS.Labeled("verb");
        Thought adjectiveCategory = theUKS.Labeled("adjective");

        int pairs = 0;
        foreach (Thought singular in nouns)
        {
            string pluralLabel = WordLabel(singular) + pluralSuffix;
            if (!vocabulary.TryGetValue(pluralLabel, out Thought plural) || plural == singular)
                continue;

            // A plural is a thing, so correct any earlier guess that placed it
            // among the qualities or the actions.
            plural.AddParent(nounCategory);
            if (adjectiveCategory is not null) plural.RemoveParent(adjectiveCategory);
            if (verbCategory is not null) plural.RemoveParent(verbCategory);

            // It denotes the same thing the singular denotes, so "dogs are
            // animals" can resolve to the concepts dog and animal.
            Thought concept = GetOrCreateMeaning(singular);
            if (plural.GetTargetOfFirstLinkOfType("means") is null)
                theUKS.AddStatement(plural, meansType, concept);
            if (theUKS.GetLink(plural, pluralOfType, singular) is null)
                theUKS.AddStatement(plural, pluralOfType, singular);
            pairs++;
        }
        return pairs;
    }

    /// <summary>
    /// Reports the closed populations discovered from the current templates, for
    /// inspection from the dialog or from tests.
    /// </summary>
    public static (List<string> introducers, List<string> separators) DescribeFunctionWords(
        int minFunctionWordTemplates = 2)
    {
        List<TemplateStructure> templates = ReadTemplateStructures();
        FunctionWords functionWords = DiscoverFunctionWords(templates, minFunctionWordTemplates);
        return (functionWords.Introducers.Select(word => word.Label).OrderBy(x => x).ToList(),
            functionWords.Separators.Select(word => word.Label).OrderBy(x => x).ToList());
    }

    /// <summary>
    /// A one-line account of what the last discovery pass concluded, for the
    /// dialog's status line.
    /// </summary>
    public static string DescribeGrammarSummary()
    {
        var theUKS = MainWindow.theUKS;
        Thought roleRoot = theUKS.Labeled("SlotRole");
        Thought categoryRoot = theUKS.Labeled("LexicalCategory");
        if (roleRoot is null || categoryRoot is null) return "no grammatical roles yet";

        string categories = string.Join(", ", categoryRoot.Children
            .Select(category => $"{category.Children.Count} {category.Label}")
            .OrderBy(text => text, StringComparer.Ordinal));
        int grounded = roleRoot.Children.Count(role =>
            role.Parents.Any(parent => parent.HasAncestor("GrammaticalRole")));
        return $"{roleRoot.Children.Count} slot roles ({grounded} grounded in actions); {categories}";
    }

    /// <summary>
    /// Reads every learned template as an ordered element list.
    /// </summary>
    private static List<TemplateStructure> ReadTemplateStructures()
    {
        var theUKS = MainWindow.theUKS;
        Thought templateRoot = theUKS.Labeled("LearnedTemplate");
        if (templateRoot is null) return new List<TemplateStructure>();

        List<TemplateStructure> structures = new();
        foreach (Thought template in templateRoot.Children)
        {
            SequenceView sequence = theUKS.GetSequenceViews(template)
                .FirstOrDefault(view => view.LinkType?.Label == "hasWords");
            if (sequence is null || sequence.Elements.Count < 2) continue;

            List<Thought> elements = sequence.Elements.ToList();
            structures.Add(new TemplateStructure
            {
                Template = template,
                Elements = elements,
                IsSlot = elements.Select(element => element.HasAncestor("Wildcard")).ToArray(),
                Introduces = new bool[elements.Count],
                Descriptors = new string[elements.Count],
                Roles = new Thought[elements.Count],
            });
        }
        return structures;
    }

    /// <summary>
    /// Separates the fixed values which introduce a following position from
    /// those which stand between two positions. A value doing neither, or
    /// appearing in too few templates to be structural rather than content, is
    /// left alone.
    /// </summary>
    private static FunctionWords DiscoverFunctionWords(
        List<TemplateStructure> templates,
        int minTemplates)
    {
        Dictionary<Thought, int> occurrences = new();
        Dictionary<Thought, int> afterSlot = new();
        Dictionary<Thought, HashSet<Thought>> appearsIn = new();

        foreach (TemplateStructure structure in templates)
        {
            for (int position = 0; position < structure.Elements.Count; position++)
            {
                if (structure.IsSlot[position]) continue;
                Thought value = structure.Elements[position];
                Increment(occurrences, value);
                if (position > 0 && structure.IsSlot[position - 1])
                    Increment(afterSlot, value);
                if (!appearsIn.TryGetValue(value, out HashSet<Thought> owners))
                    appearsIn[value] = owners = new HashSet<Thought>();
                owners.Add(structure.Template);
            }
        }

        FunctionWords retVal = new();
        foreach (var entry in occurrences)
        {
            Thought value = entry.Key;
            // A value seen in a single template cannot be told apart from the
            // content of that template, however it happens to be placed.
            if (appearsIn[value].Count < minTemplates) continue;
            if (Count(afterSlot, value) / (float)entry.Value >= 0.7f)
                retVal.Separators.Add(value);
        }

        // Introducing is recognized only after separating is. What a value
        // introduces is content, whether or not that content was ever learned as
        // a position: counting learned positions alone makes "an" introducing in
        // "is an ??" but not in "is an animal", so it is taken for neither.
        Dictionary<Thought, int> beforeContent = new();
        foreach (TemplateStructure structure in templates)
        {
            for (int position = 0; position + 1 < structure.Elements.Count; position++)
            {
                if (structure.IsSlot[position]) continue;
                if (structure.IsSlot[position + 1] ||
                    !retVal.Separators.Contains(structure.Elements[position + 1]))
                    Increment(beforeContent, structure.Elements[position]);
            }
        }

        foreach (var entry in occurrences)
        {
            Thought value = entry.Key;
            if (retVal.Separators.Contains(value)) continue;
            if (Count(beforeContent, value) / (float)entry.Value < 0.75f) continue;

            // A value which never follows a position, and only ever precedes
            // content, cannot be that content and cannot be separating anything.
            // That is conclusive on its own, which matters for "an": it occurs
            // in too few templates to be counted, but is never anywhere else.
            if (appearsIn[value].Count >= minTemplates || Count(afterSlot, value) == 0)
                retVal.Introducers.Add(value);
        }
        return retVal;
    }

    /// <summary>
    /// Records which positions introduce the position after them and which
    /// position separates the two sides of the template. The separator is
    /// normally a fixed value; where a template has none, the one position
    /// which nothing introduces serves the same purpose.
    /// </summary>
    private static void MarkStructuralPositions(
        TemplateStructure structure,
        FunctionWords functionWords)
    {
        for (int position = 0; position < structure.Elements.Count; position++)
        {
            Thought element = structure.Elements[position];
            structure.Introduces[position] = structure.IsSlot[position]
                ? IsIntroducingSlot(element, functionWords)
                : functionWords.Introducers.Contains(element);
        }

        structure.SeparatorPosition = -1;
        for (int position = 0; position < structure.Elements.Count; position++)
        {
            if (!structure.IsSlot[position] &&
                functionWords.Separators.Contains(structure.Elements[position]))
            {
                structure.SeparatorPosition = position;
                return;
            }
        }

        // "the dog sees a pig" has no separating value drawn from the closed
        // population, because that verb occurs in too few templates to have been
        // recognized as one. Whatever stands between two introduced positions
        // and is itself introduced by nothing is separating them, however rare.
        for (int position = 1; position < structure.Elements.Count - 1; position++)
        {
            if (structure.Introduces[position] || structure.Introduces[position - 1]) continue;

            bool introducedBefore = Enumerable.Range(1, position - 1)
                .Any(earlier => structure.IsSlot[earlier] && structure.Introduces[earlier - 1]);
            bool slotAfter = Enumerable.Range(position + 1, structure.Elements.Count - position - 1)
                .Any(later => structure.IsSlot[later]);
            if (introducedBefore && slotAfter)
            {
                structure.SeparatorPosition = position;
                return;
            }
        }
    }

    /// <summary>
    /// A position whose observed fillers are mostly introducing values is itself
    /// an introducing position, however it came to be learned as a position.
    /// </summary>
    private static bool IsIntroducingSlot(Thought slot, FunctionWords functionWords)
    {
        if (functionWords.Introducers.Count == 0) return false;
        List<Thought> members = SlotMembers(slot);
        if (members.Count == 0) return false;
        return members.Count(functionWords.Introducers.Contains) > members.Count / 2;
    }

    /// <summary>
    /// Gives each position a role candidate keyed by how it sits relative to the
    /// separator, to the values which introduce positions, and to which
    /// separator governs it. Returns the population filling each candidate,
    /// which is what later decides whether two candidates are one role.
    /// </summary>
    private static Dictionary<Thought, HashSet<Thought>> AssignRoleCandidates(
        List<TemplateStructure> templates)
    {
        var theUKS = MainWindow.theUKS;
        theUKS.GetSlotRoleRoot();
        Dictionary<Thought, HashSet<Thought>> populations = new();

        foreach (TemplateStructure structure in templates)
        {
            for (int position = 0; position < structure.Elements.Count; position++)
            {
                string descriptor = DescribePosition(structure, position);
                structure.Descriptors[position] = descriptor;
                if (descriptor is null) continue;

                Thought role = GetOrCreateRole(descriptor);
                structure.Roles[position] = role;
                if (!populations.TryGetValue(role, out HashSet<Thought> pool))
                    populations[role] = pool = new HashSet<Thought>();
                pool.UnionWith(FillersAt(structure, position));
            }
        }
        return populations;
    }

    /// <summary>
    /// Finds the role already discovered for a structural situation, or creates
    /// one. Reusing the previous pass's roles is what makes a rerun stable.
    /// </summary>
    private static Thought GetOrCreateRole(string descriptor)
    {
        var theUKS = MainWindow.theUKS;
        Thought marker = theUKS.GetOrAddThought("position:" + descriptor, "LanguageElement");
        Thought existing = marker.LinksFrom
            .Where(link => link.LinkType?.Label == "describes" && link.From is not null)
            .Select(link => link.From)
            .FirstOrDefault(candidate => candidate.HasAncestor("SlotRole"));
        if (existing is not null) return existing;

        Thought role = theUKS.AddSlotRole();
        theUKS.AddStatement(role, theUKS.GetOrAddThought("describes", "LinkType"), marker);
        return role;
    }

    /// <summary>
    /// Names the structural situation of one position. A position which is
    /// neither structural nor filled is left undescribed so that later recovery
    /// can decide what it is.
    /// </summary>
    private static string DescribePosition(TemplateStructure structure, int position)
    {
        if (structure.Introduces[position]) return "introducer";
        if (position == structure.SeparatorPosition) return "separator";
        if (!structure.IsSlot[position]) return null;

        int separator = structure.SeparatorPosition;
        string side = separator < 0 ? "unsplit" : position < separator ? "before" : "after";
        string introduced = position > 0 && structure.Introduces[position - 1] ? "introduced" : "bare";
        string distance = separator >= 0 && Math.Abs(position - separator) == 1 ? "adjacent" : "distant";

        // Which separator governs a position matters: the same bare position
        // after one separator holds a quality and after another holds an action.
        string governor = separator < 0 ? "none" : structure.Elements[separator].Label;
        return $"{side}|{introduced}|{distance}|{governor}";
    }

    /// <summary>
    /// Re-reads each position's role after coalescing, so that positions which
    /// referred to a role which has since been merged away refer to the role
    /// which replaced it.
    /// </summary>
    private static void ResolveRolesFromDescriptors(List<TemplateStructure> templates)
    {
        var theUKS = MainWindow.theUKS;
        foreach (TemplateStructure structure in templates)
        {
            for (int position = 0; position < structure.Descriptors.Length; position++)
            {
                string descriptor = structure.Descriptors[position];
                if (descriptor is null)
                {
                    structure.Roles[position] = null;
                    continue;
                }
                Thought marker = theUKS.Labeled("position:" + descriptor);
                structure.Roles[position] = marker?.LinksFrom
                    .Where(link => link.LinkType?.Label == "describes" && link.From is not null)
                    .Select(link => link.From)
                    .FirstOrDefault(candidate => candidate.HasAncestor("SlotRole"));
            }
        }
    }

    /// <summary>
    /// Some templates froze a position into one value because that value
    /// dominated the observations, as in "the ?? can bark". Where a template of
    /// the same shape keeps a position there, the frozen value is doing the same
    /// job and is given the same role.
    /// </summary>
    private static void RecoverFrozenPositions(List<TemplateStructure> templates)
    {
        Dictionary<string, TemplateStructure> byShape = new(StringComparer.Ordinal);
        foreach (TemplateStructure structure in templates)
        {
            string shape = DescribeShape(structure);
            if (!byShape.TryGetValue(shape, out TemplateStructure best) ||
                CountRoles(best) < CountRoles(structure))
                byShape[shape] = structure;
        }

        foreach (TemplateStructure structure in templates)
        {
            if (!byShape.TryGetValue(DescribeShape(structure), out TemplateStructure model)) continue;
            if (model == structure || model.Roles.Length != structure.Roles.Length) continue;
            for (int position = 0; position < structure.Roles.Length; position++)
                if (structure.Roles[position] is null && model.Roles[position] is not null)
                    structure.Roles[position] = model.Roles[position];
        }

        // A shape deliberately treats a frozen value and a position alike, which
        // is what lets "the ?? can bark" borrow from "the ?? can ??".
        static string DescribeShape(TemplateStructure structure)
        {
            return string.Join("", Enumerable.Range(0, structure.Elements.Count).Select(position =>
                structure.Introduces[position] ? "D" :
                position == structure.SeparatorPosition ? "R" : "_"));
        }

        static int CountRoles(TemplateStructure structure) =>
            structure.Roles.Count(role => role is not null);
    }

    /// <summary>
    /// Writes each template's roles beside its words, so the assignment becomes
    /// ordinary knowledge rather than a result held only inside this pass.
    /// </summary>
    private static void WriteRoleSequences(List<TemplateStructure> templates)
    {
        var theUKS = MainWindow.theUKS;
        Thought hasWords = theUKS.Labeled("hasWords");
        if (hasWords is null) return;
        foreach (TemplateStructure structure in templates)
            theUKS.AssignSlotRoles(structure.Template, hasWords, structure.Roles);
    }

    /// <summary>
    /// Binds discovered roles to the parts of the actions their templates
    /// perform. A role which supplies the source of an action is thereby
    /// understood as a subject rather than merely labeled as one.
    /// </summary>
    private static void GroundRolesInActions(List<TemplateStructure> templates)
    {
        var theUKS = MainWindow.theUKS;
        theUKS.GetOrAddThought("ActionArgument", "LanguageElement");
        Thought sourceArgument = theUKS.GetOrAddThought("actionSource", "ActionArgument");
        Thought relationArgument = theUKS.GetOrAddThought("actionRelation", "ActionArgument");
        Thought targetArgument = theUKS.GetOrAddThought("actionTarget", "ActionArgument");
        Thought supplies = theUKS.GetOrAddThought("supplies", "LinkType");

        foreach (TemplateStructure structure in templates)
        {
            Link action = structure.Template.LinksTo
                .Where(link => link.LinkType?.Label == "means")
                .Select(link => link.To)
                .OfType<Link>()
                .FirstOrDefault(candidate => candidate.LinkType?.HasAncestor("SET") == true);
            if (action?.From is null || action.To is null) continue;

            Bind(structure.Elements.IndexOf(action.From), sourceArgument);
            Bind(structure.Elements.IndexOf(action.To), targetArgument);
            Bind(structure.SeparatorPosition, relationArgument);

            void Bind(int position, Thought argument)
            {
                if (position < 0 || position >= structure.Roles.Length) return;
                if (structure.Roles[position] is Thought role)
                    theUKS.AddStatement(role, supplies, argument);
            }
        }

        // The English names are read off the bindings. They exist so a person
        // can inspect the result; no step of discovery consults them. They are
        // parents rather than labels because one language may separate a role
        // the names do not: singular and plural subjects are two discovered
        // roles, and both are subjects.
        theUKS.GetOrAddThought("GrammaticalRole", "LanguageElement");
        NameFromBinding(sourceArgument, "subjectRole");
        NameFromBinding(targetArgument, "predicateRole");
        NameFromBinding(relationArgument, "verbRole");
        foreach (Thought role in theUKS.GetSlotRoleRoot().Children.ToList())
            if (DescriptorOf(role) == "introducer") Name(role, "articleRole");

        void NameFromBinding(Thought argument, string name)
        {
            foreach (Thought role in argument.LinksFrom
                .Where(link => link.LinkType?.Label == "supplies" && link.From is not null)
                .Select(link => link.From)
                .Where(candidate => candidate.HasAncestor("SlotRole"))
                .Distinct()
                .ToList())
                Name(role, name);
        }

        void Name(Thought role, string name)
        {
            role.AddParent(theUKS.GetOrAddThought(name, "GrammaticalRole"));
        }
    }

    /// <summary>
    /// Turns role populations into classes of words. A role is a property of a
    /// position; a category is a property of a word, reached by pooling every
    /// position which plays the same part. Categories which cannot be told apart
    /// by where they occur are told apart by where they never occur.
    /// </summary>
    private static void DeriveLexicalCategories(
        List<TemplateStructure> templates,
        FunctionWords functionWords)
    {
        var theUKS = MainWindow.theUKS;
        Thought categoryRoot = theUKS.GetOrAddThought("LexicalCategory", "LanguageElement");

        HashSet<Thought> articles = new(functionWords.Introducers);
        HashSet<Thought> separators = new(functionWords.Separators);
        HashSet<Thought> things = new();
        HashSet<Thought> qualifiers = new();
        HashSet<Thought> governedBare = new();

        // A separator which can take an introduced complement links its subject
        // to another thing; one which never does links it to an action. This
        // tells "is an animal" apart from "can bark" without inspecting words.
        HashSet<Thought> thingLinkingSeparators = new();
        Dictionary<Thought, HashSet<Thought>> bareComplements = new();
        foreach (TemplateStructure structure in templates)
        {
            if (structure.SeparatorPosition < 0) continue;
            Thought separator = structure.Elements[structure.SeparatorPosition];
            for (int position = structure.SeparatorPosition + 1;
                position < structure.Elements.Count; position++)
            {
                if (!structure.IsSlot[position]) continue;
                if (position > 0 && structure.Introduces[position - 1])
                {
                    thingLinkingSeparators.Add(separator);
                    continue;
                }
                if (!bareComplements.TryGetValue(separator, out HashSet<Thought> pool))
                    bareComplements[separator] = pool = new HashSet<Thought>();
                pool.UnionWith(FillersAt(structure, position));
            }
        }

        // Two separators which accept the same bare complements are doing the
        // same job, so one which is never seen taking an introduced complement
        // still links its subject to a thing. This is what carries "is an
        // animal" over to "are", which the corpus never writes that way.
        foreach (var candidate in bareComplements)
        {
            if (thingLinkingSeparators.Contains(candidate.Key)) continue;
            bool matchesKnown = thingLinkingSeparators.Any(known =>
                bareComplements.TryGetValue(known, out HashSet<Thought> knownPool) &&
                knownPool.Count > 0 &&
                candidate.Value.Intersect(knownPool).Count() >
                    Math.Max(candidate.Value.Count, knownPool.Count) / 2);
            if (matchesKnown) thingLinkingSeparators.Add(candidate.Key);
        }

        foreach (TemplateStructure structure in templates)
        {
            for (int position = 0; position < structure.Elements.Count; position++)
            {
                string descriptor = structure.Descriptors[position];
                if (descriptor is null) continue;
                List<Thought> fillers = FillersAt(structure, position);

                if (descriptor == "introducer") { articles.UnionWith(fillers); continue; }
                if (descriptor == "separator") { separators.UnionWith(fillers); continue; }

                string[] parts = descriptor.Split('|');
                bool introduced = parts[1] == "introduced";
                bool adjacent = parts[2] == "adjacent";

                if (parts[0] == "before")
                {
                    // Of two positions before the separator, the one next to it
                    // names the thing and the earlier one qualifies it.
                    if (adjacent) things.UnionWith(fillers);
                    else qualifiers.UnionWith(fillers);
                }
                else if (parts[0] == "after")
                {
                    // An introduced complement is another thing.
                    if (introduced) things.UnionWith(fillers);
                    else if (structure.SeparatorPosition >= 0 &&
                        thingLinkingSeparators.Contains(structure.Elements[structure.SeparatorPosition]))
                        qualifiers.UnionWith(fillers);
                    else governedBare.UnionWith(fillers);
                }
            }
        }

        // What a thing-linking separator governs, or what appears qualifying a
        // thing, is a quality. What remains after a separator is an action.
        HashSet<Thought> adjectives = new(qualifiers);
        adjectives.ExceptWith(things);
        HashSet<Thought> verbs = new(separators);
        verbs.UnionWith(governedBare.Where(word =>
            !things.Contains(word) && !adjectives.Contains(word)));
        HashSet<Thought> nouns = new(things);

        foreach (HashSet<Thought> category in new[] { adjectives, verbs, nouns })
            category.ExceptWith(articles);
        verbs.ExceptWith(adjectives);
        nouns.ExceptWith(verbs);

        Populate("article", articles);
        Populate("noun", nouns);
        Populate("verb", verbs);
        Populate("adjective", adjectives);

        void Populate(string name, HashSet<Thought> members)
        {
            if (members.Count == 0) return;
            Thought category = theUKS.GetOrAddThought(name, categoryRoot);
            foreach (Thought member in members)
                member.AddParent(category);
            category.Weight = Math.Max(category.Weight, members.Count);
        }
    }

    /// <summary>
    /// The ordinary words a position accepts. A fixed position accepts only its
    /// own value.
    /// </summary>
    private static List<Thought> FillersAt(TemplateStructure structure, int position)
    {
        return structure.IsSlot[position]
            ? SlotMembers(structure.Elements[position])
            : new List<Thought> { structure.Elements[position] };
    }

    /// <summary>
    /// The words a learned position accepts, ignoring the wildcard itself and
    /// the sequence nodes which implement it.
    /// </summary>
    private static List<Thought> SlotMembers(Thought slot)
    {
        if (slot is null) return new List<Thought>();
        List<Thought> retVal = new();
        foreach (Thought parent in slot.Parents)
        {
            if (string.Equals(parent.Label, "Wildcard", StringComparison.OrdinalIgnoreCase)) continue;
            retVal.AddRange(parent.Children.Where(child =>
                child is not SeqElement && !child.HasAncestor("Wildcard")));
        }
        return retVal;
    }

    /// <summary>
    /// The structural situation a role was discovered from. A coalesced role
    /// describes several situations; they agree on everything this is used for.
    /// </summary>
    private static string DescriptorOf(Thought role)
    {
        string label = role?.LinksTo
            .Where(link => link.LinkType?.Label == "describes")
            .Select(link => link.To?.Label)
            .FirstOrDefault(value => value is not null &&
                value.StartsWith("position:", StringComparison.Ordinal));
        return label?[9..];
    }

    private static string SideOf(string descriptor)
    {
        if (descriptor is null) return "none";
        int separator = descriptor.IndexOf('|');
        return separator < 0 ? descriptor : descriptor[..separator];
    }

    /// <summary>
    /// The spelling a word carries, without the "w:" the corpus loader prefixes.
    /// Comparisons must use this rather than the raw label, or a value created by
    /// one path fails to match the same value created by another.
    /// </summary>
    private static string WordLabel(Thought word)
    {
        return word.Label.StartsWith("w:", StringComparison.Ordinal) ? word.Label[2..] : word.Label;
    }

    private static void Increment(Dictionary<Thought, int> counts, Thought key)
    {
        counts[key] = counts.TryGetValue(key, out int existing) ? existing + 1 : 1;
    }

    private static int Count(Dictionary<Thought, int> counts, Thought key)
    {
        return counts.TryGetValue(key, out int existing) ? existing : 0;
    }
}
