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

        string currentDir = Directory.GetCurrentDirectory();
        string brainSimRoot = FindBrainSimRoot(currentDir);


        // Load algorithm.xml from UKSContent folder
        string xmlPath = Path.Combine(brainSimRoot, "BrainSimulator", "UKSContent", "algorithm.xml");
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
        
        bool success = module.ExecuteTask("invertBool", "TRUE", "FALSE");
        
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
        
        bool success = module.ExecuteTask("invertBool", "FALSE", "TRUE");
        
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
        module.ExecuteTask("invertBool", "TRUE", "FALSE");
        
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
        module.ExecuteTask("invertBool", "TRUE", "FALSE");
        Assert.Equal("false", module.LastLinkWritten.To?.Label.ToLower());
        
        // Second: FALSE -> TRUE
        module.ExecuteTask("invertBool", "FALSE", "TRUE");
        Assert.Equal("true", module.LastLinkWritten.To?.Label.ToLower());
    }

    [Fact]
    public void Test05_TaskExists()
    {
        // Verify BoolNot task loaded from XML
        Thought boolNotTask = uks.Labeled("invertBool_main");
        Assert.NotNull(boolNotTask);
        
        // Verify it has steps
        SeqElement steps = boolNotTask.GetTargetOfFirstLinkOfType("steps") as SeqElement;
        Assert.NotNull(steps);
    }

    [Fact]
    public void Test06_SeqLength_DogVsCat_Equal()
    {
        // Test: SeqLength with dog (3) and cat (3)
        // Expected: dog->EQ->cat

        bool success = module.ExecuteTask("SeqLength", "dog", "cat");

        Assert.True(success, "SeqLength task execution should succeed");
        Assert.NotNull(module.LastLinkWritten);

        // Get the thoughts directly
        Thought dog = uks.Labeled("dog");
        Thought cat = uks.Labeled("cat");

        Assert.Equal(dog, module.LastLinkWritten.From);
        Assert.Equal("eq", module.LastLinkWritten.LinkType?.Label.ToLower());
        Assert.Equal(cat, module.LastLinkWritten.To);
    }

    [Fact]
    public void Test07_SeqLength_DogVsPuppy_Less()
    {
        // Test: SeqLength with dog (3) and puppy (5)
        // When dog < puppy, result is reversed: puppy->GT->dog

        bool success = module.ExecuteTask("SeqLength", "dog", "puppy");

        Assert.True(success, "SeqLength task execution should succeed");
        Assert.NotNull(module.LastLinkWritten);

        // Get the thoughts directly
        Thought dog = uks.Labeled("dog");
        Thought puppy = uks.Labeled("puppy");

        // Result is reversed: puppy GT dog (not dog LT puppy)
        Assert.Equal(puppy, module.LastLinkWritten.From);
        Assert.Equal("gt", module.LastLinkWritten.LinkType?.Label.ToLower());
        Assert.Equal(dog, module.LastLinkWritten.To);
    }

    [Fact]
    public void Test08_SeqLength_PuppyVsDog_Greater()
    {
        // Test: SeqLength with puppy (5) and dog (3)
        // Expected: puppy->GT->dog

        bool success = module.ExecuteTask("SeqLength", "puppy", "dog");

        Assert.True(success, "SeqLength task execution should succeed");
        Assert.NotNull(module.LastLinkWritten);

        // Get the thoughts directly
        Thought puppy = uks.Labeled("puppy");
        Thought dog = uks.Labeled("dog");

        Assert.Equal(puppy, module.LastLinkWritten.From);
        Assert.Equal("gt", module.LastLinkWritten.LinkType?.Label.ToLower());
        Assert.Equal(dog, module.LastLinkWritten.To);
    }

    [Fact]
    public void Test09_SeqLength_KittenVsPuppy_Greater()
    {
        // Test: SeqLength with kitten (6) and puppy (5)
        // Expected: kitten->GT->puppy

        bool success = module.ExecuteTask("SeqLength", "kitten", "puppy");

        Assert.True(success, "SeqLength task execution should succeed");
        Assert.NotNull(module.LastLinkWritten);

        // Get the thoughts directly
        Thought kitten = uks.Labeled("kitten");
        Thought puppy = uks.Labeled("puppy");

        Assert.Equal(kitten, module.LastLinkWritten.From);
        Assert.Equal("gt", module.LastLinkWritten.LinkType?.Label.ToLower());
        Assert.Equal(puppy, module.LastLinkWritten.To);
    }

    [Fact]
    public void Test10_SeqLength_CatVsKitten_Less()
    {
        // Test: SeqLength with cat (3) and kitten (6)
        // Expected: kitten->GT->cat (reversed)

        bool success = module.ExecuteTask("SeqLength", "cat", "kitten");

        Assert.True(success, "SeqLength task execution should succeed");
        Assert.NotNull(module.LastLinkWritten);

        // Get the thoughts directly
        Thought cat = uks.Labeled("cat");
        Thought kitten = uks.Labeled("kitten");

        Assert.Equal(cat, module.LastLinkWritten.To);
        Assert.Equal("gt", module.LastLinkWritten.LinkType?.Label.ToLower());
        Assert.Equal(kitten, module.LastLinkWritten.From);
    }

    [Fact]
    public void Test11_SeqLength_VerifyLinkInUKS()
    {
        // Execute comparison and verify link exists in UKS
        module.ExecuteTask("SeqLength", "dog", "cat");

        Thought dog = uks.Labeled("dog");
        Thought cat = uks.Labeled("cat");

        // Verify the link exists
        Thought eqType = uks.Labeled("EQ");
        Link comparisonLink = dog.HasLink(eqType, cat);
        Assert.NotNull(comparisonLink);
    }

    [Fact]
    public void Test12_SeqLength_TaskExists()
    {
        // Verify SeqLength task loaded from XML
        Thought seqLengthTask = uks.Labeled("SeqLength_main");
        Assert.NotNull(seqLengthTask);
        
        // Verify it has steps
        SeqElement steps = seqLengthTask.GetTargetOfFirstLinkOfType("steps") as SeqElement;
        Assert.NotNull(steps);
    }

    [Fact]
    public void Test13_CompareLetters_SameLetter_Equal()
    {
        // Test: CompareLetters with A and A
        // Expected: A->EQ->A
        
        bool success = module.ExecuteTask("CompareLetters", "A", "A");
        
        Assert.True(success, "CompareLetters task execution should succeed");
        Assert.NotNull(module.LastLinkWritten);
        
        Thought a1 = uks.Labeled("A");
        Thought a2 = uks.Labeled("A");
        
        Assert.Equal(a1, module.LastLinkWritten.From);
        Assert.Equal("eq", module.LastLinkWritten.LinkType?.Label.ToLower());
        Assert.Equal(a2, module.LastLinkWritten.To);
    }

    [Fact]
    public void Test14_CompareLetters_BAfterA_Greater()
    {
        // Test: CompareLetters with B and A
        // Expected: B->GT->A (B comes after A)
        
        bool success = module.ExecuteTask("CompareLetters", "B", "A");
        
        Assert.True(success, "CompareLetters task execution should succeed");
        Assert.NotNull(module.LastLinkWritten);
        
        Thought b = uks.Labeled("B");
        Thought a = uks.Labeled("A");
        
        Assert.Equal(b, module.LastLinkWritten.From);
        Assert.Equal("gt", module.LastLinkWritten.LinkType?.Label.ToLower());
        Assert.Equal(a, module.LastLinkWritten.To);
    }

    [Fact]
    public void Test15_CompareLetters_ABeforeB_Reversed()
    {
        // Test: CompareLetters with A and B
        // When A < B, result is reversed: B->GT->A
        
        bool success = module.ExecuteTask("CompareLetters", "A", "B");
        
        Assert.True(success, "CompareLetters task execution should succeed");
        Assert.NotNull(module.LastLinkWritten);
        
        Thought a = uks.Labeled("A");
        Thought b = uks.Labeled("B");
        
        // Result is reversed: B GT A (not A LT B)
        Assert.Equal(b, module.LastLinkWritten.From);
        Assert.Equal("gt", module.LastLinkWritten.LinkType?.Label.ToLower());
        Assert.Equal(a, module.LastLinkWritten.To);
    }

    [Fact]
    public void Test16_CompareLetters_ZAfterA_Greater()
    {
        // Test: CompareLetters with Z and A
        // Expected: Z->GT->A (Z comes after A)
        
        bool success = module.ExecuteTask("CompareLetters", "Z", "A");
        
        Assert.True(success, "CompareLetters task execution should succeed");
        Assert.NotNull(module.LastLinkWritten);
        
        Thought z = uks.Labeled("Z");
        Thought a = uks.Labeled("A");
        
        Assert.Equal(z, module.LastLinkWritten.From);
        Assert.Equal("gt", module.LastLinkWritten.LinkType?.Label.ToLower());
        Assert.Equal(a, module.LastLinkWritten.To);
    }

    [Fact]
    public void Test17_CompareLetters_MAfterC_Greater()
    {
        // Test: CompareLetters with M and C
        // Expected: M->GT->C (M comes after C)
        
        bool success = module.ExecuteTask("CompareLetters", "M", "C");
        
        Assert.True(success, "CompareLetters task execution should succeed");
        Assert.NotNull(module.LastLinkWritten);
        
        Thought m = uks.Labeled("M");
        Thought c = uks.Labeled("C");
        
        Assert.Equal(m, module.LastLinkWritten.From);
        Assert.Equal("gt", module.LastLinkWritten.LinkType?.Label.ToLower());
        Assert.Equal(c, module.LastLinkWritten.To);
    }

    [Fact]
    public void Test18_CompareLetters_CBeforeM_Reversed()
    {
        // Test: CompareLetters with C and M
        // When C < M, result is reversed: M->GT->C
        
        bool success = module.ExecuteTask("CompareLetters", "C", "M");
        
        Assert.True(success, "CompareLetters task execution should succeed");
        Assert.NotNull(module.LastLinkWritten);
        
        Thought c = uks.Labeled("C");
        Thought m = uks.Labeled("M");
        
        // Result is reversed: M GT C (not C LT M)
        Assert.Equal(m, module.LastLinkWritten.From);
        Assert.Equal("gt", module.LastLinkWritten.LinkType?.Label.ToLower());
        Assert.Equal(c, module.LastLinkWritten.To);
    }

    [Fact]
    public void Test19_CompareLetters_VerifyLinkInUKS()
    {
        // Execute comparison and verify link exists in UKS
        module.ExecuteTask("CompareLetters", "B", "A");
        
        Thought b = uks.Labeled("B");
        Thought a = uks.Labeled("A");
        
        // Verify the GT link exists
        Thought gtType = uks.Labeled("GT");
        Link comparisonLink = b.HasLink(gtType, a);
        Assert.NotNull(comparisonLink);
    }

    [Fact]
    public void Test20_CompareLetters_Sequential()
    {
        // Test multiple sequential comparisons
        
        // Equal case
        module.ExecuteTask("CompareLetters", "D", "D");
        Assert.Equal("eq", module.LastLinkWritten.LinkType?.Label.ToLower());
        
        // GT case
        module.ExecuteTask("CompareLetters", "X", "Y");
        Thought y = uks.Labeled("Y");
        Assert.Equal(y, module.LastLinkWritten.From);
        Assert.Equal("gt", module.LastLinkWritten.LinkType?.Label.ToLower());
        
        // Another GT case (reversed)
        module.ExecuteTask("CompareLetters", "F", "Z");
        Thought z = uks.Labeled("Z");
        Assert.Equal(z, module.LastLinkWritten.From);
        Assert.Equal("gt", module.LastLinkWritten.LinkType?.Label.ToLower());
    }

    [Fact]
    public void Test21_CompareLetters_TaskExists()
    {
        // Verify CompareLetters task loaded from XML
        Thought compareLettersTask = uks.Labeled("CompareLetters_main");
        Assert.NotNull(compareLettersTask);
        
        // Verify it has steps
        SeqElement steps = compareLettersTask.GetTargetOfFirstLinkOfType("steps") as SeqElement;
        Assert.NotNull(steps);
    }

    [Fact]
    public void Test22_Alphabetize_EqualWords()
    {
        // Test: alphabetize with "cat" and "cat"
        // Expected: cat->EQ->cat

        bool success = module.ExecuteTask("alphabetize", "cat", "cat");

        Assert.True(success, "alphabetize task execution should succeed");
        Assert.NotNull(module.LastLinkWritten);

        Thought cat = uks.Labeled("cat");

        Assert.Equal(cat, module.LastLinkWritten.From);
        Assert.Equal("eq", module.LastLinkWritten.LinkType?.Label.ToLower());
        Assert.Equal(cat, module.LastLinkWritten.To);
    }

    [Fact]
    public void Test23_Alphabetize_FirstGreater()
    {
        // Test: alphabetize with "zebra" and "apple"
        // Expected: zebra->GT->apple (zebra comes after apple)
        
        bool success = module.ExecuteTask("alphabetize", "zebra", "apple");
        
        Assert.True(success, "alphabetize task execution should succeed");
        Assert.NotNull(module.LastLinkWritten);
        
        Thought zebra = uks.Labeled("zebra");
        Thought apple = uks.Labeled("apple");
        
        Assert.Equal(zebra, module.LastLinkWritten.From);
        Assert.Equal("gt", module.LastLinkWritten.LinkType?.Label.ToLower());
        Assert.Equal(apple, module.LastLinkWritten.To);
    }

    [Fact]
    public void Test24_Alphabetize_SecondGreater()
    {
        // Test: alphabetize with "apple" and "zebra"
        // Expected: zebra->GT->apple (reversed - zebra comes after)
        
        bool success = module.ExecuteTask("alphabetize", "apple", "zebra");
        
        Assert.True(success, "alphabetize task execution should succeed");
        Assert.NotNull(module.LastLinkWritten);
        
        Thought apple = uks.Labeled("apple");
        Thought zebra = uks.Labeled("zebra");
        
        // Result is reversed: zebra GT apple (not apple LT zebra)
        Assert.Equal(zebra, module.LastLinkWritten.From);
        Assert.Equal("gt", module.LastLinkWritten.LinkType?.Label.ToLower());
        Assert.Equal(apple, module.LastLinkWritten.To);
    }

    [Fact]
    public void Test25_Alphabetize_DifferingLength()
    {
        // Test: alphabetize with "car" and "cart"
        // Expected: cart->GT->car (cart comes after car)
        
        bool success = module.ExecuteTask("alphabetize", "car", "cart");
        
        Assert.True(success, "alphabetize task execution should succeed");
        Assert.NotNull(module.LastLinkWritten);
        
        Thought car = uks.Labeled("car");
        Thought cart = uks.Labeled("cart");
        
        // cart comes after car alphabetically
        Assert.Equal(cart, module.LastLinkWritten.From);
        Assert.Equal("gt", module.LastLinkWritten.LinkType?.Label.ToLower());
        Assert.Equal(car, module.LastLinkWritten.To);
    }

    [Fact]
    public void Test26_Alphabetize_NewWordsCreated()
    {
        // Test: alphabetize with arbitrary strings that don't exist yet
        // Expected: Words are created with spelling sequences
        
        bool success = module.ExecuteTask("alphabetize", "hello", "world");
        
        Assert.True(success, "alphabetize task execution should succeed");
        Assert.NotNull(module.LastLinkWritten);
        
        // Verify the words were created
        Thought hello = uks.Labeled("hello");
        Thought world = uks.Labeled("world");
        Assert.NotNull(hello);
        Assert.NotNull(world);
        
        // Verify they have spelling sequences
        SeqElement helloSpelled = hello.GetTargetOfFirstLinkOfType("spelled") as SeqElement;
        SeqElement worldSpelled = world.GetTargetOfFirstLinkOfType("spelled") as SeqElement;
        Assert.NotNull(helloSpelled);
        Assert.NotNull(worldSpelled);
        
        // Verify comparison result (world > hello alphabetically)
        Assert.Equal(world, module.LastLinkWritten.From);
        Assert.Equal("gt", module.LastLinkWritten.LinkType?.Label.ToLower());
        Assert.Equal(hello, module.LastLinkWritten.To);
    }

    [Fact]
    public void Test27_Alphabetize_CaseSensitive()
    {
        // Test: alphabetize with different case variations
        // Expected: Comparison is case-insensitive (uppercase in spelling)
        
        bool success = module.ExecuteTask("alphabetize", "Book", "Apple");
        
        Assert.True(success, "alphabetize task execution should succeed");
        Assert.NotNull(module.LastLinkWritten);
        
        Thought book = uks.Labeled("Book");
        Thought apple = uks.Labeled("Apple");
        
        // Book > Apple alphabetically
        Assert.Equal(book, module.LastLinkWritten.From);
        Assert.Equal("gt", module.LastLinkWritten.LinkType?.Label.ToLower());
        Assert.Equal(apple, module.LastLinkWritten.To);
    }

    [Fact]
    public void Test28_Alphabetize_TaskExists()
    {
        // Verify alphabetize task loaded from XML
        Thought alphabetizeTask = uks.Labeled("alphabetize_main");
        Assert.NotNull(alphabetizeTask);
        
        // Verify it has steps
        SeqElement steps = alphabetizeTask.GetTargetOfFirstLinkOfType("steps") as SeqElement;
        Assert.NotNull(steps);
    }
}