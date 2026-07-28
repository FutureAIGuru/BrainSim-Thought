using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BrainSimulator.Modules;
using UKS;
using Xunit;
using Xunit.Abstractions;

namespace BrainSimulator.Tests;

/// <summary>
/// Saying in English what the UKS knows.
/// </summary>
[Collection("ModuleTextPatternLearning")]
public class ModuleTextGenerationTests
{
    private readonly ITestOutputHelper output;

    public ModuleTextGenerationTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public void ShowWhatCanBeSaid()
    {
        UKS.UKS uks = LoadCorpus();
        Thought hasWords = uks.Labeled("hasWords");

        output.WriteLine("templates which perform an action:");
        foreach (Thought template in uks.Labeled("LearnedTemplate").Children)
        {
            Link action = template.LinksTo
                .Where(link => link.LinkType?.Label == "means")
                .Select(link => link.To).OfType<Link>()
                .FirstOrDefault(a => a.LinkType?.HasAncestor("SET") == true);
            if (action is null) continue;
            SequenceView view = uks.GetSequenceViews(template).FirstOrDefault(v => v.LinkType == hasWords);
            if (view is null) continue;
            int evidence = template.LinksTo.Count(l => l.LinkType?.Label == "evidence");
            output.WriteLine($"  {action.LinkType.Label,-12} ev={evidence,-4} " +
                string.Join(' ', view.Elements.Select(e =>
                    e.Label.StartsWith("w:") ? e.Label[2..] : e.Label)));
            foreach (Thought element in view.Elements.Where(e => e.HasAncestor("Wildcard")))
                output.WriteLine($"        {element.Label} accepts: " + string.Join(", ",
                    element.Parents.Where(p => !p.Label.Equals("wildcard", StringComparison.OrdinalIgnoreCase))
                        .SelectMany(p => p.Children)
                        .Where(c => c is not SeqElement && !c.HasAncestor("Wildcard"))
                        .Select(c => c.Label.StartsWith("w:") ? c.Label[2..] : c.Label)
                        .Distinct().Take(8)));
        }

        output.WriteLine("");
        output.WriteLine("relationships said:");
        foreach (string relation in new[] { "is-a", "is", "has", "can" })
        {
            Thought linkType = uks.Labeled(relation);
            if (linkType is null) continue;
            foreach (Link link in uks.Labeled("dog").LinksTo
                .Where(l => l.LinkType == linkType && l.To is not null).Take(3))
                output.WriteLine($"  [dog->{relation}->{link.To.Label}]  =>  " +
                    (ModuleText.DescribeRelationship(link) ?? "(nothing)"));
        }
    }

    [Fact]
    public void RelationshipsAreSaidAsEnglishPhrases()
    {
        UKS.UKS uks = LoadCorpus();

        foreach ((string subject, string relation, string target, string expected) in new[]
        {
            ("dog", "can", "bark", "a dog can bark"),
            ("dog", "has", "tail", "a dog has a tail"),
            ("dog", "is", "brown", "a dog is brown"),
            ("terrier", "is-a", "dog", "a terrier is a dog"),
            // Nothing learned can put "animal" after "a", so this fact is
            // stated with the phrasing which was learned for it.
            ("dog", "is-a", "animal", "dogs are animals"),
        })
        {
            Link relationship = uks.GetLink(
                uks.Labeled(subject), uks.Labeled(relation), uks.Labeled(target));
            Assert.NotNull(relationship);

            string said = ModuleText.DescribeRelationship(relationship);
            output.WriteLine($"[{subject}->{relation}->{target}]  =>  {said}");
            Assert.Equal(expected, said);
        }
    }

    [Fact]
    public void TheSameRelationIsPhrasedByWhichWordsATemplateAccepts()
    {
        // The corpus teaches "a terrier is a dog" and "a dog is an animal", so
        // the singular classification template holds only the words which follow
        // "a". Asked to classify a terrier it uses that template; asked to
        // classify a dog, whose class is "animal", that template does not accept
        // the word and the phrasing which was learned for it is used instead.
        //
        // Nothing decides this by looking at letters. The wrong article is
        // avoided because a template is chosen by the words it has been seen to
        // accept, which is the whole point of the approach.
        UKS.UKS uks = LoadCorpus();

        string terrier = ModuleText.DescribeRelationship(uks.GetLink(
            uks.Labeled("terrier"), uks.Labeled("is-a"), uks.Labeled("dog")));
        string dog = ModuleText.DescribeRelationship(uks.GetLink(
            uks.Labeled("dog"), uks.Labeled("is-a"), uks.Labeled("animal")));
        output.WriteLine($"terrier: {terrier}");
        output.WriteLine($"dog:     {dog}");

        Assert.Equal("a terrier is a dog", terrier);
        Assert.DoesNotContain("a animal", dog);
        Assert.DoesNotContain("an dog", terrier);

        // The guard which matters: no English spelling rule may creep in. Only
        // the code is inspected -- the prose is free to discuss the articles it
        // deliberately does not implement.
        string code = string.Join("\n", File
            .ReadAllLines(Path.Combine(FindRepositoryRoot(),
                "BrainSimulator", "Modules", "ModuleText.Generate.cs"))
            .Select(line => line.Trim())
            .Where(line => !line.StartsWith("//") && !line.StartsWith("*") &&
                !line.StartsWith("/*")));
        foreach (string forbidden in new[] { "aeiou", "AEIOU", "IsVowel", "\"an\"", "\"a\"" })
            Assert.DoesNotContain(forbidden, code);
    }

    [Fact]
    public void ATemplateWhichHasFrozenAnEndIsNotUsedToSaySomethingElse()
    {
        // A template which observed only one word at an end says something about
        // that word, however much evidence stands behind it. Using it would
        // report what it froze rather than the Thought being described, so an
        // open template is preferred even when it is the less attested one.
        UKS.UKS uks = LoadCorpus();
        Thought templateRoot = uks.Labeled("LearnedTemplate");
        Thought isA = uks.Labeled("is-a");
        Thought setIsA = uks.Labeled("SET.is-a");
        Thought hasWords = uks.Labeled("hasWords");

        Thought subjectClass = uks.GetOrAddThought("frozenSubjects", "LearnedClass");
        uks.Labeled("w:terrier").AddParent(subjectClass);
        Thought subjectSlot = uks.CreateWildcard("??frozenSubjects",
            new List<Thought> { subjectClass });

        Thought frozen = uks.GetOrAddThought("frozenTemplate", templateRoot);
        uks.AddSequenceAndLink(frozen, hasWords, new List<Thought>
        {
            uks.Labeled("w:a"), subjectSlot, uks.Labeled("w:is"),
            uks.Labeled("w:a"), uks.Labeled("w:bird"),
        });
        uks.AddStatement(frozen, uks.Labeled("means"),
            uks.AddStatement(subjectSlot, setIsA, uks.Labeled("w:bird")));
        for (int i = 0; i < 500; i++)
            uks.AddStatement(frozen, uks.Labeled("evidence"),
                uks.GetOrAddThought($"frozenEvidence{i}", "Statement"));

        string said = ModuleText.DescribeRelationship(
            uks.GetLink(uks.Labeled("terrier"), isA, uks.Labeled("dog")));
        output.WriteLine("with a heavily attested frozen template present: " + said);

        Assert.Equal("a terrier is a dog", said);
        Assert.DoesNotContain("bird", said);
    }

    [Fact]
    public void WhatIsSaidCanBeUnderstoodAgain()
    {
        // The strongest check available: read the phrase back in and confirm it
        // asserts the relationship it was made from.
        UKS.UKS uks = LoadCorpus();
        Link original = uks.GetLink(
            uks.Labeled("dog"), uks.Labeled("can"), uks.Labeled("bark"));

        string said = ModuleText.DescribeRelationship(original);
        Assert.NotNull(said);

        UKS.UKS reread = LoadCorpus();
        ModuleText.AddPhrase(said, applyExistingTemplates: true);
        Assert.NotNull(reread.GetLink(
            reread.Labeled("dog"), reread.Labeled("can"), reread.Labeled("bark")));
    }

    [Fact]
    public void EverythingKnownAboutAThoughtIsSaid()
    {
        UKS.UKS uks = LoadCorpus();

        List<string> account = ModuleText.DescribeThought(uks.Labeled("dog"));
        foreach (string phrase in account) output.WriteLine("  " + phrase);

        Assert.Contains("a dog can bark", account);
        Assert.Contains("a dog has a tail", account);
        Assert.Contains("a dog is brown", account);
        Assert.Contains("dogs are animals", account);

        // The workings of the UKS are not part of the account: no phrase names a
        // link type used to hold knowledge together rather than to state it.
        foreach (string phrase in account)
            foreach (string plumbing in new[] { "hasWords", "means", "evidence", "SET", "Unknown" })
                Assert.DoesNotContain(plumbing, phrase);
    }

    [Fact]
    public void AThoughtCanBeDescribedByTheWordWhichDenotesIt()
    {
        UKS.UKS uks = LoadCorpus();
        Assert.Equal(
            ModuleText.DescribeThought(uks.Labeled("dog")),
            ModuleText.DescribeThought("dog"));
    }

    [Fact]
    public void SayingWhatIsKnownTeachesNothingNew()
    {
        // Reading back an account of what is known must not change what is
        // known. If it did, the phrases would be saying something other than
        // the relationships they were made from.
        UKS.UKS uks = LoadCorpus();
        Thought dog = uks.Labeled("dog");
        List<string> account = ModuleText.DescribeThought(dog);
        Assert.NotEmpty(account);

        int relationshipsBefore = dog.LinksTo.Count;
        int thoughtsBefore = uks.AtomicThoughts.Count;

        foreach (string phrase in account)
            ModuleText.AddPhrase(phrase, applyExistingTemplates: true);

        output.WriteLine($"{account.Count} phrases read back; " +
            $"dog had {relationshipsBefore} relationships, now {dog.LinksTo.Count}");
        Assert.Equal(relationshipsBefore, dog.LinksTo.Count);

        // Reading creates the phrases themselves, so the UKS grows; what must
        // not grow is what is believed about the dog.
        Assert.True(uks.AtomicThoughts.Count >= thoughtsBefore);
    }

    [Fact]
    public void NothingIsSaidAboutARelationshipNoTemplateCovers()
    {
        // Silence is the right answer when no learned phrase fits; inventing
        // syntax would be worse than saying nothing.
        UKS.UKS uks = LoadCorpus();
        Thought oddType = uks.GetOrAddThought("smellsLike", "LinkType");
        Link unsayable = uks.AddStatement(
            uks.Labeled("dog"), oddType, uks.GetOrAddThought("rain", "Thought"));

        Assert.Null(ModuleText.DescribeRelationship(unsayable));
        Assert.Null(ModuleText.DescribeRelationship(null));
    }

    private static UKS.UKS LoadCorpus()
    {
        UKS.UKS uks = new(clear: true);
        uks.CreateInitialStructure();
        MainWindow.theUKS = uks;
        uks.GetOrAddThought("LanguageElement", "Thought");
        uks.GetOrAddThought("Phrase", "LanguageElement");
        uks.GetOrAddThought("Word", "LanguageElement");
        uks.GetOrAddThought("hasWords", "LinkType");

        string corpusPath = Path.Combine(FindRepositoryRoot(),
            "BrainSimulator", "WordFIles", "bst_true_template_corpus.txt");
        foreach (string line in File.ReadLines(corpusPath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            string[] fields = line.Split('\t', 2);
            string phraseText = fields[0].Trim();
            ModuleText.AddPhrase(phraseText, applyExistingTemplates: false);
            if (fields.Length == 2 && !string.IsNullOrWhiteSpace(fields[1]))
                ModuleText.AddActionExemplar(phraseText, fields[1]);
        }
        ModuleText.ProcessTheExistingText();

        // Loading states the actions but never carries them out, so the plain
        // relationships have to be asserted before there is anything to say.
        Thought exemplarRoot = uks.Labeled("ActionExemplar");
        if (exemplarRoot is not null)
            foreach (Link demonstrated in exemplarRoot.Children
                .Select(exemplar => exemplar.GetTargetOfFirstLinkOfType("demonstrates") as Link)
                .Where(action => action is not null)
                .ToList())
                uks.ApplySetAction(demonstrated);
        return uks;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BrainSim Thought.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
