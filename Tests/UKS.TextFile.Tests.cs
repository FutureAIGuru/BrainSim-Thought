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
using System.IO;
using UKS;
using Xunit;

namespace UKS.Tests;

public class UKSTextFileTests : IDisposable
{
    private UKS uks;
    private string testFilePath;

    public UKSTextFileTests()
    {
        uks = new UKS();
        uks.CreateInitialStructure();
        uks.GetOrAddThought("Thing", "Thought");
        testFilePath = Path.Combine(Path.GetTempPath(), $"uks_test_{Guid.NewGuid():N}.txt");
    }

    public void Dispose()
    {
        if (File.Exists(testFilePath))
            File.Delete(testFilePath);
    }

    private void ClearUKS()
    {
        uks = new UKS();
        uks.CreateInitialStructure();
    }

    private void VerifyThought(string label, string expectedValue = null)
    {
        Thought t = uks.Labeled(label);
        Assert.NotNull(t);
        if (expectedValue != null)
            Assert.Equal(expectedValue, t.V?.ToString());
    }

    private void VerifyLink(string fromLabel, string linkTypeLabel, string toLabel)
    {
        Thought from = uks.Labeled(fromLabel);
        Thought linkType = uks.Labeled(linkTypeLabel);
        Thought to = uks.Labeled(toLabel);
        
        Assert.NotNull(from);
        Assert.NotNull(linkType);
        Assert.NotNull(to);

        Link link = from.HasLink(linkType, to);
        Assert.NotNull(link);
    }

    [Fact]
    public void Test01_SimpleThought()
    {
        // Create a simple thought
        Thought dog = uks.GetOrAddThought("dog", "Thing");
        
        // Export and reimport
        uks.ExportTextFile("Thing", testFilePath);
        ClearUKS();
        uks.ImportTextFile(testFilePath);
        
        // Verify
        VerifyThought("dog");
        Thought reimported = uks.Labeled("dog");
        Assert.True(reimported.HasAncestor(uks.Labeled("Thing")));
    }

    [Fact]
    public void Test02_SimpleLink()
    {
        // Create a simple link: dog->has->tail
        Thought dog = uks.GetOrAddThought("dog", "Thing");
        Thought tail = uks.GetOrAddThought("tail", "Thing");
        Thought hasType = uks.GetOrAddThought("has", "LinkType");
        
        Link link = dog.AddLink(hasType, tail);
        
        // Export and reimport
        uks.ExportTextFile("Thought", testFilePath);
        ClearUKS();
        uks.ImportTextFile(testFilePath);
        
        // Verify
        VerifyThought("dog");
        VerifyThought("tail");
        VerifyThought("has");
        VerifyLink("dog", "has", "tail");
    }

    [Fact]
    public void Test03_MultipleLinks()
    {
        // Create multiple links
        Thought dog = uks.GetOrAddThought("dog", "Thing");
        Thought tail = uks.GetOrAddThought("tail", "Thing");
        Thought fur = uks.GetOrAddThought("fur", "Thing");
        Thought hasType = uks.GetOrAddThought("has", "LinkType");
        
        dog.AddLink(hasType, tail);
        dog.AddLink(hasType, fur);
        
        // Export and reimport
        uks.ExportTextFile("Thought", testFilePath);
        ClearUKS();
        uks.ImportTextFile(testFilePath);
        
        // Verify both links
        VerifyLink("dog", "has", "tail");
        VerifyLink("dog", "has", "fur");
    }

    [Fact]
    public void Test04_LinkWithValue()
    {
        /*  FOR NOW, VALUES ARE NOT BEING EXPORTED/IMPORTED, SO THIS TEST IS COMMENTED OUT. REENABLE ONCE THAT FUNCTIONALITY IS ADDED
         *  
         *  // Create a thought with a value
                Thought age = uks.GetOrAddThought("age", "Attribute");
                age.V = "5";

                Thought dog = uks.GetOrAddThought("dog", "Thing");
                Thought hasType = uks.GetOrAddThought("has", "LinkType");
                dog.AddLink(hasType, age);

                // Export and reimport
                uks.ExportTextFile("Thought", testFilePath);
                ClearUKS();
                uks.ImportTextFile(testFilePath);

                // Verify
                VerifyThought("age", "5");
                VerifyLink("dog", "has", "age");
          */
    }

    [Fact]
    public void Test05_SimpleSequence()
    {
        // Create a simple sequence
        Thought cat = uks.GetOrAddThought("cat", "Thing");
        Thought c = uks.GetOrAddThought("C", "Letter");
        Thought a = uks.GetOrAddThought("A", "Letter");
        Thought t = uks.GetOrAddThought("T", "Letter");
        
        List<Thought> letters = new List<Thought> { c, a, t };
        SeqElement seq = uks.AddSequence("cat", letters);
        
        Thought spelled = uks.GetOrAddThought("spelled", "LinkType");
        cat.AddLink(spelled, seq);
        
        // Export and reimport
        uks.ExportTextFile("Thought", testFilePath);
        ClearUKS();
        uks.ImportTextFile(testFilePath);
        
        // Verify sequence exists
        Thought reimportedCat = uks.Labeled("cat");
        Assert.NotNull(reimportedCat);
        
        // Find the sequence
        Link spelledLink = reimportedCat.LinksTo.FirstOrDefault(l => l.LinkType?.Label == "spelled");
        Assert.NotNull(spelledLink);
        Assert.True(spelledLink.To is SeqElement);
        
        // Verify sequence content
        List<Thought> flatSequence = uks.FlattenSequence((SeqElement)spelledLink.To);
        Assert.Equal(3, flatSequence.Count);
        Assert.Equal("C", flatSequence[0].Label);
        Assert.Equal("A", flatSequence[1].Label);
        Assert.Equal("T", flatSequence[2].Label);
    }

    [Fact]
    public void Test06_NestedLink_LinkAsSource()
    {
        // Create a nested link where the source is a link
        // [dog->has->tail]->color->brown
        
        Thought dog = uks.GetOrAddThought("dog", "Thing");
        Thought tail = uks.GetOrAddThought("tail", "Thing");
        Thought brown = uks.GetOrAddThought("brown", "Color");
        Thought hasType = uks.GetOrAddThought("has", "LinkType");
        Thought colorType = uks.GetOrAddThought("color", "LinkType");
        
        Link innerLink = dog.AddLink(hasType, tail);
        innerLink.Label = "dogTail";
        
        Link outerLink = innerLink.AddLink(colorType, brown);
        
        // Export and reimport
        uks.ExportTextFile("Thought", testFilePath);
        ClearUKS();
        uks.ImportTextFile(testFilePath);
        
        // Verify the nested structure
        Thought reimportedInner = uks.Labeled("dogTail");
        Assert.NotNull(reimportedInner);
        Assert.True(reimportedInner is Link);
        
        Link innerLinkReimported = (Link)reimportedInner;
        Assert.Equal("dog", innerLinkReimported.From?.Label);
        Assert.Equal("has", innerLinkReimported.LinkType?.Label);
        Assert.Equal("tail", innerLinkReimported.To?.Label);
        
        // Verify outer link
        Link outerLinkReimported = innerLinkReimported.HasLink(uks.Labeled("color"), uks.Labeled("brown"));
        Assert.NotNull(outerLinkReimported);
    }

    [Fact]
    public void Test07_NestedLink_LinkAsTarget()
    {
        // Create a nested link where the target is a link
        // dog->owns->[cat->has->whiskers]
        
        Thought dog = uks.GetOrAddThought("dog", "Thing");
        Thought cat = uks.GetOrAddThought("cat", "Thing");
        Thought whiskers = uks.GetOrAddThought("whiskers", "Thing");
        Thought hasType = uks.GetOrAddThought("has", "LinkType");
        Thought ownsType = uks.GetOrAddThought("owns", "LinkType");
        
        Link innerLink = cat.AddLink(hasType, whiskers);
        innerLink.Label = "catWhiskers";
        
        Link outerLink = dog.AddLink(ownsType, innerLink);
        
        // Export and reimport
        uks.ExportTextFile("Thought", testFilePath);
        ClearUKS();
        uks.ImportTextFile(testFilePath);
        
        // Verify
        Thought reimportedDog = uks.Labeled("dog");
        Link ownsLink = reimportedDog.HasLink(uks.Labeled("owns"), uks.Labeled("catWhiskers"));
        Assert.NotNull(ownsLink);
        Assert.True(ownsLink.To is Link);
        
        Link innerReimported = (Link)ownsLink.To;
        Assert.Equal("cat", innerReimported.From?.Label);
        Assert.Equal("has", innerReimported.LinkType?.Label);
        Assert.Equal("whiskers", innerReimported.To?.Label);
    }

    [Fact]
    public void Test08_Context_Simple()
    {
        // Create a simple context with children
        Thought rootContext = uks.GetOrAddThought("Algorithm", "Context");
        Thought c1 = uks.GetOrAddThought("c1", rootContext);
        Thought c2 = uks.GetOrAddThought("c2", rootContext);
        
        // Add relationships to contexts
        Thought r1 = uks.GetOrAddThought("R1", "Thing");
        Thought hasType = uks.GetOrAddThought("has", "LinkType");
        
        c1.AddLink(hasType, r1);
        
        // Export and reimport
        uks.ExportTextFile("Thought", testFilePath);
        ClearUKS();
        uks.ImportTextFile(testFilePath);
        
        // Verify context structure
        Thought reimportedRoot = uks.Labeled("Algorithm");
        Assert.NotNull(reimportedRoot);
        
        Thought reimportedC1 = uks.Labeled("c1");
        Assert.NotNull(reimportedC1);
        Assert.True(reimportedC1.HasAncestor(reimportedRoot));
        
        Thought reimportedC2 = uks.Labeled("c2");
        Assert.NotNull(reimportedC2);
        Assert.True(reimportedC2.HasAncestor(reimportedRoot));
        
        // Verify relationship
        VerifyLink("c1", "has", "R1");
    }

    [Fact]
    public void Test09_Context_WithNestedLinks()
    {
        // Create a context with nested link relationships
        Thought rootContext = uks.GetOrAddThought("Algorithm", "Context");
        Thought c1 = uks.GetOrAddThought("c1", rootContext);
        
        // Create nested relationship: c1->has->[p1->is->EXIST]
        Thought p1 = uks.GetOrAddThought("p1", "Param");
        Thought exist = uks.GetOrAddThought("EXIST", "State");
        Thought isType = uks.GetOrAddThought("is", "LinkType");
        Thought hasType = uks.GetOrAddThought("has", "LinkType");
        
        Link innerLink = p1.AddLink(isType, exist);
        innerLink.Label = "p1Exists";
        
        Link outerLink = c1.AddLink(hasType, innerLink);
        
        // Export and reimport
        uks.ExportTextFile("Thought", testFilePath);
        ClearUKS();
        uks.ImportTextFile(testFilePath);
        
        // Verify context
        Thought reimportedC1 = uks.Labeled("c1");
        Assert.NotNull(reimportedC1);
        
        // Verify nested link
        Link hasLink = reimportedC1.HasLink(uks.Labeled("has"), uks.Labeled("p1Exists"));
        Assert.NotNull(hasLink);
        Assert.True(hasLink.To is Link);
        
        Link innerReimported = (Link)hasLink.To;
        Assert.Equal("p1", innerReimported.From?.Label);
        Assert.Equal("is", innerReimported.LinkType?.Label);
        Assert.Equal("EXIST", innerReimported.To?.Label);
    }

    [Fact]
    public void Test10_Context_Complex()
    {
        // Create a complex context like the one shown in the image
        Thought rootContext = uks.GetOrAddThought("Algorithm", "Context");
        
        // Create multiple child contexts
        Thought c1 = uks.GetOrAddThought("c1", rootContext);
        Thought c2 = uks.GetOrAddThought("c2", rootContext);
        Thought c3 = uks.GetOrAddThought("c3", rootContext);
        
        // Create relationships for c1
        Thought p1 = uks.GetOrAddThought("p1", "Param");
        Thought p2 = uks.GetOrAddThought("p2", "Param");
        Thought exist = uks.GetOrAddThought("EXIST", "State");
        Thought nxt = uks.GetOrAddThought("NXT", "State");
        Thought isType = uks.GetOrAddThought("is", "LinkType");
        Thought hasType = uks.GetOrAddThought("has", "LinkType");
        Thought responseType = uks.GetOrAddThought("response", "LinkType");
        Thought continueResponse = uks.GetOrAddThought("continue", "Response");
        
        // c1->has->[p1->is->EXIST.NXT]
        Link p1Relation = p1.AddLink(isType, nxt);
        p1Relation.Label = "R0";
        c1.AddLink(hasType, p1Relation);
        
        // c1->has->[p2->is->EXIST.NXT]
        Link p2Relation = p2.AddLink(isType, nxt);
        p2Relation.Label = "R1";
        c1.AddLink(hasType, p2Relation);
        
        // c1->response->continue
        c1.AddLink(responseType, continueResponse);
        
        // Similar for c2 and c3
        Thought notExist = uks.GetOrAddThought("NOT.EXIST", "State");
        Link c2R0 = p1.AddLink(isType, notExist);
        c2R0.Label = "R2";
        c2.AddLink(hasType, c2R0);
        
        Thought p1Longer = uks.GetOrAddThought("p1Longer", "Response");
        c2.AddLink(responseType, p1Longer);
        
        // Export and reimport
        uks.ExportTextFile("Thought", testFilePath);
        ClearUKS();
        uks.ImportTextFile(testFilePath);
        
        // Verify entire structure
        Thought reimportedRoot = uks.Labeled("Algorithm");
        Assert.NotNull(reimportedRoot);
        
        // Verify all contexts exist
        Thought reimportedC1 = uks.Labeled("c1");
        Thought reimportedC2 = uks.Labeled("c2");
        Thought reimportedC3 = uks.Labeled("c3");
        Assert.NotNull(reimportedC1);
        Assert.NotNull(reimportedC2);
        Assert.NotNull(reimportedC3);
        
        // Verify c1 relationships
        var c1HasLinks = reimportedC1.LinksTo.Where(l => l.LinkType?.Label == "has").ToList();
        Assert.Equal(2, c1HasLinks.Count);
        
        var c1ResponseLinks = reimportedC1.LinksTo.Where(l => l.LinkType?.Label == "response").ToList();
        Assert.Single(c1ResponseLinks);
        Assert.Equal("continue", c1ResponseLinks[0].To?.Label);
        
        // Verify nested links
        Link r0Link = uks.Labeled("R0") as Link;
        Assert.NotNull(r0Link);
        Assert.Equal("p1", r0Link.From?.Label);
        Assert.Equal("is", r0Link.LinkType?.Label);
    }

    [Fact]
    public void Test11_WeightPreservation()
    {
        // Test that weights are preserved
        Thought dog = uks.GetOrAddThought("dog", "Thing");
        Thought tail = uks.GetOrAddThought("tail", "Thing");
        Thought hasType = uks.GetOrAddThought("has", "LinkType");
        
        Link link = dog.AddLink(hasType, tail);
        link.Weight = 2.5f;
        
        // Export and reimport
        uks.ExportTextFile("Thought", testFilePath);
        ClearUKS();
        uks.ImportTextFile(testFilePath);
        
        // Verify weight
        Thought reimportedDog = uks.Labeled("dog");
        Link reimportedLink = reimportedDog.HasLink(uks.Labeled("has"), uks.Labeled("tail"));
        Assert.NotNull(reimportedLink);
        Assert.Equal(2.5f, reimportedLink.Weight, 2);
    }

    [Fact]
    public void Test12_LinkWeights_Comprehensive()
    {
        // Test various weight scenarios
        Thought dog = uks.GetOrAddThought("dog", "Thing");
        Thought tail = uks.GetOrAddThought("tail", "Thing");
        Thought fur = uks.GetOrAddThought("fur", "Thing");
        Thought ears = uks.GetOrAddThought("ears", "Thing");
        Thought nose = uks.GetOrAddThought("nose", "Thing");
        Thought hasType = uks.GetOrAddThought("has", "LinkType");
        
        // Link with custom weight
        Link link1 = dog.AddLink(hasType, tail);
        link1.Weight = 2.5f;
        
        // Link with default weight (should be 1.0)
        Link link2 = dog.AddLink(hasType, fur);
        
        // Link with zero weight
        Link link3 = dog.AddLink(hasType, ears);
        link3.Weight = 0.0f;
        
        // Link with very small weight
        Link link4 = dog.AddLink(hasType, nose);
        link4.Weight = 0.1f;
        
        // Export and reimport
        uks.ExportTextFile("Thought", testFilePath);
        ClearUKS();
        uks.ImportTextFile(testFilePath);
        
        // Verify all weights
        Thought reimportedDog = uks.Labeled("dog");
        
        Link reimportedLink1 = reimportedDog.HasLink(uks.Labeled("has"), uks.Labeled("tail"));
        Assert.NotNull(reimportedLink1);
        Assert.Equal(2.5f, reimportedLink1.Weight, 2);
        
        Link reimportedLink2 = reimportedDog.HasLink(uks.Labeled("has"), uks.Labeled("fur"));
        Assert.NotNull(reimportedLink2);
        Assert.Equal(1.0f, reimportedLink2.Weight, 2);
        
        Link reimportedLink3 = reimportedDog.HasLink(uks.Labeled("has"), uks.Labeled("ears"));
        Assert.NotNull(reimportedLink3);
        Assert.Equal(0.0f, reimportedLink3.Weight, 2);
        
        Link reimportedLink4 = reimportedDog.HasLink(uks.Labeled("has"), uks.Labeled("nose"));
        Assert.NotNull(reimportedLink4);
        Assert.Equal(0.1f, reimportedLink4.Weight, 2);
    }

    [Fact]
    public void Test13_NestedLinkWeights()
    {
        // Test that weights are preserved in nested link structures
        Thought dog = uks.GetOrAddThought("dog", "Thing");
        Thought tail = uks.GetOrAddThought("tail", "Thing");
        Thought brown = uks.GetOrAddThought("brown", "Color");
        Thought hasType = uks.GetOrAddThought("has", "LinkType");
        Thought colorType = uks.GetOrAddThought("color", "LinkType");
        
        // Create nested link with weights
        Link innerLink = dog.AddLink(hasType, tail);
        innerLink.Label = "dogTail";
        innerLink.Weight = 3.0f;
        
        Link outerLink = innerLink.AddLink(colorType, brown);
        outerLink.Weight = 1.5f;
        
        // Export and reimport
        uks.ExportTextFile("Thought", testFilePath);
        ClearUKS();
        uks.ImportTextFile(testFilePath);
        
        // Verify nested structure with weights
        Thought reimportedInner = uks.Labeled("dogTail");
        Assert.NotNull(reimportedInner);
        Assert.True(reimportedInner is Link);
        
        Link innerLinkReimported = (Link)reimportedInner;
        Assert.Equal(3.0f, innerLinkReimported.Weight, 2);
        
        // Verify outer link weight
        Link outerLinkReimported = innerLinkReimported.HasLink(uks.Labeled("color"), uks.Labeled("brown"));
        Assert.NotNull(outerLinkReimported);
        Assert.Equal(1.5f, outerLinkReimported.Weight, 2);
    }

    [Fact]
    public void Test14_NegativeAndLargeWeights()
    {
        // Test edge cases for weights
        Thought dog = uks.GetOrAddThought("dog", "Thing");
        Thought tail = uks.GetOrAddThought("tail", "Thing");
        Thought fur = uks.GetOrAddThought("fur", "Thing");
        Thought ears = uks.GetOrAddThought("ears", "Thing");
        Thought hasType = uks.GetOrAddThought("has", "LinkType");
        
        // Negative weight
        Link link1 = dog.AddLink(hasType, tail);
        link1.Weight = -1.5f;
        
        // Very large weight
        Link link2 = dog.AddLink(hasType, fur);
        link2.Weight = 1000.0f;
        
        // Very precise weight
        Link link3 = dog.AddLink(hasType, ears);
        link3.Weight = 3.14159f;
        
        // Export and reimport
        uks.ExportTextFile("Thought", testFilePath);
        ClearUKS();
        uks.ImportTextFile(testFilePath);
        
        // Verify all weights
        Thought reimportedDog = uks.Labeled("dog");
        
        Link reimportedLink1 = reimportedDog.HasLink(uks.Labeled("has"), uks.Labeled("tail"));
        Assert.NotNull(reimportedLink1);
        Assert.Equal(-1.5f, reimportedLink1.Weight, 2);
        
        Link reimportedLink2 = reimportedDog.HasLink(uks.Labeled("has"), uks.Labeled("fur"));
        Assert.NotNull(reimportedLink2);
        Assert.Equal(1000.0f, reimportedLink2.Weight, 2);
        
        Link reimportedLink3 = reimportedDog.HasLink(uks.Labeled("has"), uks.Labeled("ears"));
        Assert.NotNull(reimportedLink3);
        Assert.Equal(3.14159f, reimportedLink3.Weight, 2);
    }
}