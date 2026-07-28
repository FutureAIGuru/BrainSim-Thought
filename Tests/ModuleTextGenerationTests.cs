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

    [Theory]
    [InlineData(903)]
    [InlineData(500)]
    public void ShowTheAccountOfABirdAsTheDialogWouldGiveIt(int lines)
    {
        // Exactly what the running program does: load, Process, and no more.
        // 500 is what one press of Load ingests, so it shows what a half-loaded
        // corpus produces.
        UKS.UKS uks = LoadCorpus(applyActions: false, lineLimit: lines);
        Thought bird = uks.Labeled("bird");
        output.WriteLine($"--- {lines} lines ---");
        if (bird is null) { output.WriteLine("no bird"); return; }
        foreach (Link link in bird.LinksTo.Where(l => l.To is not null))
            output.WriteLine($"  [bird -{link.LinkType?.Label}-> {link.To.Label}]  =>  " +
                (ModuleText.DescribeRelationship(link) ?? "(nothing)"));
        output.WriteLine("  account:");
        foreach (string phrase in ModuleText.DescribeThought(bird))
            output.WriteLine("    " + phrase);
    }

    [Theory]
    [InlineData(903)]
    [InlineData(500)]
    [InlineData(250)]
    public void ShowWhetherCommonWordsCanBeDescribed(int lines)
    {
        UKS.UKS uks = LoadCorpus(applyActions: false, lineLimit: lines);
        output.WriteLine($"--- {lines} lines loaded ---");
        foreach (string name in new[] { "dog", "cat", "bird", "fish", "ball" })
        {
            List<string> account = ModuleText.DescribeThought(name);
            output.WriteLine($"  {name,-6} {account.Count} phrases" +
                (account.Count == 0 ? "   because: " + ModuleText.ExplainNothingSaid(name) : ""));
            foreach (string phrase in account) output.WriteLine("           " + phrase);
        }
    }

    [Fact]
    public void TheReasonNothingIsSaidIsReported()
    {
        // Silence has several causes and they need telling apart: nothing
        // learned yet, a word never seen, a word seen but not understood, and
        // knowledge which no learned phrase can express.
        UKS.UKS empty = new(clear: true);
        empty.CreateInitialStructure();
        MainWindow.theUKS = empty;
        Assert.Contains("Load a corpus", ModuleText.ExplainNothingSaid("dog"));

        UKS.UKS uks = LoadCorpus(applyActions: false, lineLimit: int.MaxValue);
        Assert.Contains("never been seen", ModuleText.ExplainNothingSaid("aardvark"));
        Assert.Contains("nothing has been understood",
            ModuleText.ExplainNothingSaid("ball"));

        // And when a relationship can be said, the diagnosis says so with it.
        Link canFly = uks.GetLink(uks.Labeled("bird"), uks.Labeled("can"), uks.Labeled("fly"));
        string diagnosis = ModuleText.DiagnoseRelationship(canFly);
        output.WriteLine(diagnosis);
        Assert.Contains("can be said", diagnosis);
        Assert.Contains("a bird can fly", diagnosis);

        // A relationship no phrase covers names the position which refused it,
        // or says no phrase performs that relation at all.
        Thought odd = uks.GetOrAddThought("smellsLike", "LinkType");
        string refused = ModuleText.DiagnoseRelationship(
            uks.AddStatement(uks.Labeled("bird"), odd, uks.Labeled("blue")));
        output.WriteLine(refused);
        Assert.Contains("no learned phrase performs", refused);
    }

    [Fact]
    public void WordsAreStillWordsWhenTheirClassMembershipHasChanged()
    {
        // A running program had every relationship intact and could say none of
        // them, because the words denoting things were no longer under the Word
        // class and so were not recognised as words at all. A Thought carrying
        // the spelling prefix is a word whatever has become of its parentage.
        UKS.UKS uks = LoadCorpus();
        Thought bird = uks.Labeled("bird");
        Assert.NotEmpty(ModuleText.DescribeThought(bird));

        Thought wordRoot = uks.Labeled("Word");
        List<Thought> reparented = wordRoot.Children
            .Where(word => word.Label.StartsWith("w:", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(reparented);
        Thought elsewhere = uks.GetOrAddThought("SomewhereElse", "Thought");
        foreach (Thought word in reparented)
        {
            word.AddParent(elsewhere);
            word.RemoveParent(wordRoot);
        }
        Assert.DoesNotContain(uks.Labeled("w:bird").Parents, parent => parent == wordRoot);

        List<string> account = ModuleText.DescribeThought(bird);
        foreach (string phrase in account) output.WriteLine("  " + phrase);
        Assert.NotEmpty(account);
        Assert.Contains("a bird can fly", account);
    }

    [Fact]
    public void SilenceForWantOfAWordSaysSo()
    {
        UKS.UKS uks = LoadCorpus();
        Thought bird = uks.Labeled("bird");
        Link canFly = uks.GetLink(bird, uks.Labeled("can"), uks.Labeled("fly"));

        // Take away what denotes the bird, leaving the knowledge untouched.
        foreach (Link link in bird.LinksFrom
            .Where(l => l.LinkType?.Label == "means").ToList())
            link.From?.RemoveLink(link);

        string diagnosis = ModuleText.DiagnoseRelationship(canFly);
        output.WriteLine(diagnosis);
        Assert.Contains("no word denotes", diagnosis);
        Assert.Contains("means", diagnosis);
    }

    [Fact]
    public void ShowWhatIsKnownAboutABall()
    {
        UKS.UKS uks = LoadCorpus(applyActions: false, lineLimit: int.MaxValue);

        foreach (string name in new[] { "ball", "bird" })
        {
            Thought thought = uks.Labeled(name);
            output.WriteLine($"--- {name} --- (exists: {thought is not null})");
            if (thought is null) continue;
            foreach (Link link in thought.LinksTo)
                output.WriteLine($"    [{name} -{link.LinkType?.Label}-> {link.To?.Label}]");
            output.WriteLine($"    account: {ModuleText.DescribeThought(thought).Count} phrases");
        }

        output.WriteLine("");
        output.WriteLine("what reading the corpus asserted (bird only):");
        ModuleText.UnderstandStoredPhrases(line =>
        {
            if (line.Contains("bird")) output.WriteLine("    " + line);
        });

        output.WriteLine("");
        output.WriteLine("corpus lines mentioning ball:");
        foreach (string line in File.ReadLines(Path.Combine(FindRepositoryRoot(),
            "BrainSimulator", "WordFIles", "bst_true_template_corpus.txt"))
            .Where(l => l.ToLowerInvariant().Contains("ball")).Take(6))
            output.WriteLine("    " + line);
    }

    [Fact]
    public void ShowTheAccountOfABird()
    {
        UKS.UKS uks = LoadCorpus();
        Thought bird = uks.Labeled("bird");

        output.WriteLine("bird's relationships:");
        foreach (Link link in bird.LinksTo.Where(l => l.To is not null))
            output.WriteLine($"  [bird -{link.LinkType?.Label}-> {link.To.Label}]  =>  " +
                (ModuleText.DescribeRelationship(link) ?? "(nothing)"));

        output.WriteLine("");
        output.WriteLine("account: ");
        foreach (string phrase in ModuleText.DescribeThought(bird))
            output.WriteLine("  " + phrase);

        output.WriteLine("");
        Thought isAsubject = uks.Labeled("LearnedClass")?.Children
            .FirstOrDefault(c => c.Children.Any(m => m.Label == "w:birds"));
        foreach (Thought cls in uks.Labeled("LearnedClass").Children.Take(12))
            output.WriteLine($"  {cls.Label}: " + string.Join(", ", cls.Children
                .Where(m => m is not SeqElement && !m.HasAncestor("Wildcard"))
                .Select(m => m.Label).Take(12)));
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
    public void TheThingAskedAboutCanBeNamedAsItWouldBeSpoken()
    {
        // What is typed into the dialog is whatever came to mind, so the same
        // account is given for the thing however it was written.
        UKS.UKS uks = LoadCorpus();
        List<string> expected = ModuleText.DescribeThought(uks.Labeled("dog"));
        Assert.NotEmpty(expected);

        foreach (string spoken in new[] { "dog", "Dog", "a dog", "the dog", "dog.", "  dog  " })
        {
            output.WriteLine($"\"{spoken}\" -> {ModuleText.DescribeThought(spoken).Count} phrases");
            Assert.Equal(expected, ModuleText.DescribeThought(spoken));
        }

        // A plural names the same thing as its singular.
        Assert.Equal(expected, ModuleText.DescribeThought("dogs"));

        Assert.Empty(ModuleText.DescribeThought(""));
        Assert.Empty(ModuleText.DescribeThought("   "));
        Assert.Empty(ModuleText.DescribeThought("nothingKnownByThisName"));
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
    public void TheUksOwnBookkeepingIsNeverSaid()
    {
        // A Thought whose parent is not known is filed under "Unknown". That is
        // the UKS keeping house, not something to report: "birds are Unknown"
        // states the absence of knowledge as though it were knowledge.
        UKS.UKS uks = LoadCorpus(applyActions: false, lineLimit: int.MaxValue);

        // Anything the corpus mentions but says nothing about keeps the
        // placeholder parent; once something is understood about a Thought the
        // UKS drops it, so the example is found rather than assumed.
        Thought unknown = uks.Labeled("Unknown");
        Link toUnknown = uks.AtomicThoughts
            .SelectMany(thought => thought.LinksTo)
            .FirstOrDefault(link => link.To == unknown && link.LinkType?.Label == "is-a" &&
                link.From is not null && link.From is not SeqElement);
        Assert.NotNull(toUnknown);
        output.WriteLine($"placeholder link: [{toUnknown.From.Label} -is-a-> Unknown]");
        Assert.Null(ModuleText.DescribeRelationship(toUnknown));
        Assert.DoesNotContain(ModuleText.DescribeThought(toUnknown.From),
            phrase => phrase.Contains("Unknown"));

        List<string> account = ModuleText.DescribeThought(uks.Labeled("bird"));
        foreach (string phrase in account) output.WriteLine("  " + phrase);
        Assert.NotEmpty(account);
        Assert.DoesNotContain(account, phrase => phrase.Contains("Unknown"));
    }

    [Fact]
    public void AWordAPositionHasNotAcceptedIsNeverPutInIt()
    {
        // Filling a plural frame with singular words produces "bird are
        // animal". A template whose position has never accepted any available
        // form of the word is not used at all.
        UKS.UKS uks = LoadCorpus();

        // "wing" is known only as something a bird has, never as a kind of
        // thing, so no classification template has a position which accepts it.
        Thought wing = uks.Labeled("wing");
        Link invented = uks.AddStatement(wing, uks.Labeled("is-a"), uks.Labeled("animal"));
        string said = ModuleText.DescribeRelationship(invented);
        output.WriteLine("[wing->is-a->animal] => " + (said ?? "(nothing)"));

        // Whatever comes out, it may not be a frame filled with words the frame
        // never took.
        if (said is not null)
        {
            Assert.DoesNotContain("wing are", said);
            Assert.DoesNotContain("are animal.", said + ".");
        }

        // The accounts which were already right stay right.
        Assert.Equal("birds are animals", ModuleText.DescribeRelationship(
            uks.GetLink(uks.Labeled("bird"), uks.Labeled("is-a"), uks.Labeled("animal"))));
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

    private static UKS.UKS LoadCorpus() => LoadCorpus(applyActions: true, lineLimit: int.MaxValue);

    private static UKS.UKS LoadCorpus(bool applyActions, int lineLimit)
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
        int used = 0;
        foreach (string line in File.ReadLines(corpusPath))
        {
            if (used >= lineLimit) break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            string[] fields = line.Split('\t', 2);
            string phraseText = fields[0].Trim();
            ModuleText.AddPhrase(phraseText, applyExistingTemplates: false);
            if (fields.Length == 2 && !string.IsNullOrWhiteSpace(fields[1]))
                ModuleText.AddActionExemplar(phraseText, fields[1]);
            used++;
        }
        ModuleText.ProcessTheExistingText();
        if (!applyActions) return uks;

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
