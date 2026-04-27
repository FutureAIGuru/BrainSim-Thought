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
using BrainSimulator.Modules;
using UKS;
using Xunit;

namespace UKS.Tests;

public class ModuleAlgorithmTests : IDisposable
{
    private UKS uks;
    private ModuleAlgorithm module;

    public ModuleAlgorithmTests()
    {
        uks = new UKS();
        uks.CreateInitialStructure();
        
        module = new ModuleAlgorithm();
        module.theUKS = uks;
        //module.UKSInitializedNotification();

        string currentDir = Directory.GetCurrentDirectory();
        string brainSimRoot = FindBrainSimRoot(currentDir);


        // Load algorithm.xml from UKSContent folder
        string xmlPath = Path.Combine(brainSimRoot,"brainsimulator", "UKSContent", "algorithm.xml");
        if (!File.Exists(xmlPath))
        {
            // Try alternative path
            xmlPath = Path.Combine("UKSContent", "algorithm.xml");
        }
        
        if (File.Exists(xmlPath))
        {
            uks.LoadUKSfromXMLFile(xmlPath);
        }
        else
        {
            throw new FileNotFoundException($"algorithm.xml not found. Searched paths.");
        }
    }
    private string FindBrainSimRoot(string startPath)
    {
        DirectoryInfo dir = new DirectoryInfo(startPath);

        while (dir != null)
        {
            // Check if current directory name is "BrainSim Thought"
            if (dir.Name.Equals("BrainSim Thought", StringComparison.OrdinalIgnoreCase))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        return null;
    }
    public void Dispose()
    {
        // Cleanup
    }

    [Fact]
    public void Test01_BoolNot_TrueInput()
    {
        // Test: BoolNot with TRUE input
        // Expected: p1->is->FALSE
        
        bool success = module.ExecuteTask("BoolNot", "TRUE", "FALSE");
        
        Assert.True(success, "BoolNot task execution should succeed");
        Assert.NotNull(module.LastLinkWritten);
        Assert.Equal("p1", module.LastLinkWritten.From?.Label.ToLower());
        Assert.Equal("is", module.LastLinkWritten.LinkType?.Label.ToLower());
        Assert.Equal("false", module.LastLinkWritten.To?.Label.ToLower());
    }

    [Fact]
    public void Test02_BoolNot_FalseInput()
    {
        // Test: BoolNot with FALSE input
        // Expected: p1->is->TRUE
        
        bool success = module.ExecuteTask("BoolNot", "FALSE", "TRUE");
        
        Assert.True(success, "BoolNot task execution should succeed");
        Assert.NotNull(module.LastLinkWritten);
        Assert.Equal("p1", module.LastLinkWritten.From?.Label.ToLower());
        Assert.Equal("is", module.LastLinkWritten.LinkType?.Label.ToLower());
        Assert.Equal("true", module.LastLinkWritten.To?.Label.ToLower());
    }

    [Fact]
    public void Test03_VerifyLinkPersistence()
    {
        // Execute task and verify link exists in UKS
        module.ExecuteTask("BoolNot", "TRUE", "FALSE");
        
        Thought p1 = uks.Labeled("p1");
        Assert.NotNull(p1);
        
        Thought isType = uks.Labeled("is");
        Thought falseThought = uks.Labeled("FALSE");
        
        Link writtenLink = p1.HasLink(isType, falseThought);
        Assert.NotNull(writtenLink);
    }

    [Fact]
    public void Test04_SequentialExecution()
    {
        // Test multiple executions
        
        // First: TRUE -> FALSE
        module.ExecuteTask("BoolNot", "TRUE", "FALSE");
        Assert.Equal("false", module.LastLinkWritten.To?.Label.ToLower());
        
        // Second: FALSE -> TRUE
        module.ExecuteTask("BoolNot", "FALSE", "TRUE");
        Assert.Equal("true", module.LastLinkWritten.To?.Label.ToLower());
    }

    [Fact]
    public void Test05_TaskExists()
    {
        // Verify BoolNot task loaded from XML
        Thought boolNotTask = uks.Labeled("BoolNot");
        Assert.NotNull(boolNotTask);
        
        // Verify it has steps
        SeqElement steps = boolNotTask.GetTargetOfFirstLinkOfType("steps") as SeqElement;
        Assert.NotNull(steps);
    }

    [Fact]
    public void Test06_CompareLength_DogVsCat_Equal()
    {
        // Test: CompareLength with dog (3) and cat (3)
        // Expected: dog->spelled->EQ->cat->spelled
    
        bool success = module.ExecuteTask("compareLength", "dog", "cat");
    
        Assert.True(success, "compareLength task execution should succeed");
        Assert.NotNull(module.LastLinkWritten);
    
        // Get the sequences
        Thought dog = uks.Labeled("dog");
        Thought cat = uks.Labeled("cat");
        SeqElement dogSpelled = dog.GetTargetOfFirstLinkOfType("spelled") as SeqElement;
        SeqElement catSpelled = cat.GetTargetOfFirstLinkOfType("spelled") as SeqElement;
    
        Assert.Equal(dogSpelled, module.LastLinkWritten.From);
        Assert.Equal("eq", module.LastLinkWritten.LinkType?.Label.ToLower());
        Assert.Equal(catSpelled, module.LastLinkWritten.To);
    }

    [Fact]
    public void Test07_CompareLength_DogVsPuppy_Less()
    {
        // Test: CompareLength with dog (3) and puppy (5)
        // When dog < puppy, result is reversed: puppy->spelled->GT->dog->spelled
    
        bool success = module.ExecuteTask("compareLength", "dog", "puppy");
    
        Assert.True(success, "compareLength task execution should succeed");
        Assert.NotNull(module.LastLinkWritten);
    
        // Get the sequences
        Thought dog = uks.Labeled("dog");
        Thought puppy = uks.Labeled("puppy");
        SeqElement dogSpelled = dog.GetTargetOfFirstLinkOfType("spelled") as SeqElement;
        SeqElement puppySpelled = puppy.GetTargetOfFirstLinkOfType("spelled") as SeqElement;
    
        // Result is reversed: puppy GT dog (not dog LT puppy)
        Assert.Equal(puppySpelled, module.LastLinkWritten.From);
        Assert.Equal("gt", module.LastLinkWritten.LinkType?.Label.ToLower());
        Assert.Equal(dogSpelled, module.LastLinkWritten.To);
    }

    [Fact]
    public void Test08_CompareLength_PuppyVsDog_Greater()
    {
        // Test: CompareLength with puppy (5) and dog (3)
        // Expected: puppy->spelled->GT->dog->spelled
    
        bool success = module.ExecuteTask("compareLength", "puppy", "dog");
    
        Assert.True(success, "compareLength task execution should succeed");
        Assert.NotNull(module.LastLinkWritten);
    
        // Get the sequences
        Thought puppy = uks.Labeled("puppy");
        Thought dog = uks.Labeled("dog");
        SeqElement puppySpelled = puppy.GetTargetOfFirstLinkOfType("spelled") as SeqElement;
        SeqElement dogSpelled = dog.GetTargetOfFirstLinkOfType("spelled") as SeqElement;
    
        Assert.Equal(puppySpelled, module.LastLinkWritten.From);
        Assert.Equal("gt", module.LastLinkWritten.LinkType?.Label.ToLower());
        Assert.Equal(dogSpelled, module.LastLinkWritten.To);
    }

    [Fact]
    public void Test09_CompareLength_KittenVsPuppy_Greater()
    {
        // Test: CompareLength with kitten (6) and puppy (5)
        // Expected: kitten->spelled->GT->puppy->spelled
    
        bool success = module.ExecuteTask("compareLength", "kitten", "puppy");
    
        Assert.True(success, "compareLength task execution should succeed");
        Assert.NotNull(module.LastLinkWritten);
    
        // Get the sequences
        Thought kitten = uks.Labeled("kitten");
        Thought puppy = uks.Labeled("puppy");
        SeqElement kittenSpelled = kitten.GetTargetOfFirstLinkOfType("spelled") as SeqElement;
        SeqElement puppySpelled = puppy.GetTargetOfFirstLinkOfType("spelled") as SeqElement;
    
        Assert.Equal(kittenSpelled, module.LastLinkWritten.From);
        Assert.Equal("gt", module.LastLinkWritten.LinkType?.Label.ToLower());
        Assert.Equal(puppySpelled, module.LastLinkWritten.To);
    }

    [Fact]
    public void Test10_CompareLength_CatVsKitten_Less()
    {
        // Test: CompareLength with cat (3) and kitten (6)
        // Expected: cat->spelled->LT->kitten->spelled
    
        bool success = module.ExecuteTask("compareLength", "cat", "kitten");
    
        Assert.True(success, "compareLength task execution should succeed");
        Assert.NotNull(module.LastLinkWritten);
    
        // Get the sequences
        Thought cat = uks.Labeled("cat");
        Thought kitten = uks.Labeled("kitten");
        SeqElement catSpelled = cat.GetTargetOfFirstLinkOfType("spelled") as SeqElement;
        SeqElement kittenSpelled = kitten.GetTargetOfFirstLinkOfType("spelled") as SeqElement;
    
        Assert.Equal(catSpelled, module.LastLinkWritten.To);
        Assert.Equal("gt", module.LastLinkWritten.LinkType?.Label.ToLower());
        Assert.Equal(kittenSpelled, module.LastLinkWritten.From);
    }

    [Fact]
    public void Test11_CompareLength_VerifyLinkInUKS()
    {
        // Execute comparison and verify link exists in UKS
        module.ExecuteTask("compareLength", "dog", "cat");
    
        Thought dog = uks.Labeled("dog");
        Thought cat = uks.Labeled("cat");
        SeqElement dogSpelled = dog.GetTargetOfFirstLinkOfType("spelled") as SeqElement;
        SeqElement catSpelled = cat.GetTargetOfFirstLinkOfType("spelled") as SeqElement;
    
        // Verify the link exists
        Thought eqType = uks.Labeled("EQ");
        Link comparisonLink = dogSpelled.HasLink(eqType, catSpelled);
        Assert.NotNull(comparisonLink);
    }

    [Fact]
    public void Test12_CompareLength_TaskExists()
    {
        // Verify compareLength task loaded from XML
        Thought compareLengthTask = uks.Labeled("compareLength");
        Assert.NotNull(compareLengthTask);
        
        // Verify it has steps
        SeqElement steps = compareLengthTask.GetTargetOfFirstLinkOfType("steps") as SeqElement;
        Assert.NotNull(steps);
    }

    [Fact]
    public void Test13_CompareAlpha_SameLetter_Equal()
    {
        // Test: CompareAlpha with A and A
        // Expected: A->EQ->A
        
        bool success = module.ExecuteTask("comparealpha", "A", "A");
        
        Assert.True(success, "comparealpha task execution should succeed");
        Assert.NotNull(module.LastLinkWritten);
        
        Thought a1 = uks.Labeled("A");
        Thought a2 = uks.Labeled("A");
        
        Assert.Equal(a1, module.LastLinkWritten.From);
        Assert.Equal("eq", module.LastLinkWritten.LinkType?.Label.ToLower());
        Assert.Equal(a2, module.LastLinkWritten.To);
    }

    [Fact]
    public void Test14_CompareAlpha_BAfterA_Greater()
    {
        // Test: CompareAlpha with B and A
        // Expected: B->GT->A (B comes after A)
        
        bool success = module.ExecuteTask("comparealpha", "B", "A");
        
        Assert.True(success, "comparealpha task execution should succeed");
        Assert.NotNull(module.LastLinkWritten);
        
        Thought b = uks.Labeled("B");
        Thought a = uks.Labeled("A");
        
        Assert.Equal(b, module.LastLinkWritten.From);
        Assert.Equal("gt", module.LastLinkWritten.LinkType?.Label.ToLower());
        Assert.Equal(a, module.LastLinkWritten.To);
    }

    [Fact]
    public void Test15_CompareAlpha_ABeforeB_Reversed()
    {
        // Test: CompareAlpha with A and B
        // When A < B, result is reversed: B->GT->A
        
        bool success = module.ExecuteTask("comparealpha", "A", "B");
        
        Assert.True(success, "comparealpha task execution should succeed");
        Assert.NotNull(module.LastLinkWritten);
        
        Thought a = uks.Labeled("A");
        Thought b = uks.Labeled("B");
        
        // Result is reversed: B GT A (not A LT B)
        Assert.Equal(b, module.LastLinkWritten.From);
        Assert.Equal("gt", module.LastLinkWritten.LinkType?.Label.ToLower());
        Assert.Equal(a, module.LastLinkWritten.To);
    }

    [Fact]
    public void Test16_CompareAlpha_ZAfterA_Greater()
    {
        // Test: CompareAlpha with Z and A
        // Expected: Z->GT->A (Z comes after A)
        
        bool success = module.ExecuteTask("comparealpha", "Z", "A");
        
        Assert.True(success, "comparealpha task execution should succeed");
        Assert.NotNull(module.LastLinkWritten);
        
        Thought z = uks.Labeled("Z");
        Thought a = uks.Labeled("A");
        
        Assert.Equal(z, module.LastLinkWritten.From);
        Assert.Equal("gt", module.LastLinkWritten.LinkType?.Label.ToLower());
        Assert.Equal(a, module.LastLinkWritten.To);
    }

    [Fact]
    public void Test17_CompareAlpha_MAfterC_Greater()
    {
        // Test: CompareAlpha with M and C
        // Expected: M->GT->C (M comes after C)
        
        bool success = module.ExecuteTask("comparealpha", "M", "C");
        
        Assert.True(success, "comparealpha task execution should succeed");
        Assert.NotNull(module.LastLinkWritten);
        
        Thought m = uks.Labeled("M");
        Thought c = uks.Labeled("C");
        
        Assert.Equal(m, module.LastLinkWritten.From);
        Assert.Equal("gt", module.LastLinkWritten.LinkType?.Label.ToLower());
        Assert.Equal(c, module.LastLinkWritten.To);
    }

    [Fact]
    public void Test18_CompareAlpha_CBeforeM_Reversed()
    {
        // Test: CompareAlpha with C and M
        // When C < M, result is reversed: M->GT->C
        
        bool success = module.ExecuteTask("comparealpha", "C", "M");
        
        Assert.True(success, "comparealpha task execution should succeed");
        Assert.NotNull(module.LastLinkWritten);
        
        Thought c = uks.Labeled("C");
        Thought m = uks.Labeled("M");
        
        // Result is reversed: M GT C (not C LT M)
        Assert.Equal(m, module.LastLinkWritten.From);
        Assert.Equal("gt", module.LastLinkWritten.LinkType?.Label.ToLower());
        Assert.Equal(c, module.LastLinkWritten.To);
    }

    [Fact]
    public void Test19_CompareAlpha_VerifyLinkInUKS()
    {
        // Execute comparison and verify link exists in UKS
        module.ExecuteTask("comparealpha", "B", "A");
        
        Thought b = uks.Labeled("B");
        Thought a = uks.Labeled("A");
        
        // Verify the GT link exists
        Thought gtType = uks.Labeled("GT");
        Link comparisonLink = b.HasLink(gtType, a);
        Assert.NotNull(comparisonLink);
    }

    [Fact]
    public void Test20_CompareAlpha_Sequential()
    {
        // Test multiple sequential comparisons
        
        // Equal case
        module.ExecuteTask("comparealpha", "D", "D");
        Assert.Equal("eq", module.LastLinkWritten.LinkType?.Label.ToLower());
        
        // GT case
        module.ExecuteTask("comparealpha", "X", "Y");
        Thought y = uks.Labeled("Y");
        Assert.Equal(y, module.LastLinkWritten.From);
        Assert.Equal("gt", module.LastLinkWritten.LinkType?.Label.ToLower());
        
        // Another GT case (reversed)
        module.ExecuteTask("comparealpha", "F", "Z");
        Thought z = uks.Labeled("Z");
        Assert.Equal(z, module.LastLinkWritten.From);
        Assert.Equal("gt", module.LastLinkWritten.LinkType?.Label.ToLower());
    }

    [Fact]
    public void Test21_CompareAlpha_TaskExists()
    {
        // Verify comparealpha task loaded from XML
        Thought compareAlphaTask = uks.Labeled("comparealpha");
        Assert.NotNull(compareAlphaTask);
        
        // Verify it has steps
        SeqElement steps = compareAlphaTask.GetTargetOfFirstLinkOfType("steps") as SeqElement;
        Assert.NotNull(steps);
    }
}