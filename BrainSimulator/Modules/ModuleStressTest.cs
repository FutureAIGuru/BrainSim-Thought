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
//
// Copyright (c) FutureAI. All rights reserved.
// Contains confidential and  proprietary information and programs which may not be distributed without a separate license
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using UKS;

namespace BrainSimulator.Modules
{
    /// <summary>
    /// Builds a UKS of a requested size and reports what the ordinary
    /// operations cost at that size. The point is not that a large UKS can be
    /// created — creation is cheap — but that reading and reorganizing one
    /// becomes expensive, and this shows where.
    /// </summary>
    public class ModuleStressTest : ModuleBase
    {
        public static string Output = "";

        /// <summary>
        /// Every Thought this module creates carries this prefix, so its work can
        /// be found and removed without disturbing anything else in the UKS.
        /// </summary>
        public const string TestPrefix = "st:";

        /// <summary>Members per group, which sets how wide the test classes are.</summary>
        private const int GroupWidth = 500;

        private const int MaxCount = 1000000;

        public override void Initialize()
        {
        }

        public override void SetUpAfterLoad()
        {
        }

        /// <summary>
        /// Creates the requested number of Thoughts and reports the rate. Labels
        /// include separators so that two different positions cannot produce the
        /// same label and silently become one Thought.
        /// </summary>
        public static string AddManyTestItems(int count, Action<string> progress = null)
        {
            if (count <= 0) return "Enter a count greater than zero.";
            if (count > MaxCount) return $"Count above the {MaxCount:N0} limit.";

            var theUKS = MainWindow.theUKS;
            if (theUKS is null) return "No UKS is available.";

            int startingCount = theUKS.AtomicThoughts.Count;
            Stopwatch stopwatch = Stopwatch.StartNew();
            int groups = Math.Max(1, count / GroupWidth);
            int leaves = Math.Max(0, count - groups);
            long runId = DateTime.Now.Ticks % 100000;

            Thought root = theUKS.GetOrAddThought(TestPrefix + "root", "Thought");
            List<Thought> groupThoughts = new(groups);
            for (int g = 0; g < groups; g++)
                groupThoughts.Add(theUKS.GetOrAddThought($"{TestPrefix}{runId}_g{g}", root));

            for (int i = 0; i < leaves; i++)
            {
                theUKS.GetOrAddThought($"{TestPrefix}{runId}_m{i}", groupThoughts[i % groups]);
                if (progress is not null && i % 10000 == 9999)
                    progress($"created {i + 1:N0} of {leaves:N0}...");
            }

            double elapsed = stopwatch.Elapsed.TotalMilliseconds;
            int added = theUKS.AtomicThoughts.Count - startingCount;
            double perSecond = elapsed <= 0 ? 0 : added / (elapsed / 1000.0);
            return $"Added {added:N0} Thoughts in {elapsed:N0} ms ({perSecond:N0}/sec). " +
                $"UKS now holds {theUKS.AtomicThoughts.Count:N0}.";
        }

        /// <summary>
        /// Creates a UKS of the requested size and measures the operations which
        /// decide whether a UKS that large is usable.
        /// </summary>
        public static string RunBenchmark(int count, Action<string> progress = null)
        {
            if (count <= 0) return "Enter a count greater than zero.";
            if (count > MaxCount) return $"Count above the {MaxCount:N0} limit.";

            var theUKS = MainWindow.theUKS;
            if (theUKS is null) return "No UKS is available.";

            StringBuilder report = new();
            Stopwatch total = Stopwatch.StartNew();
            long runId = DateTime.Now.Ticks % 100000;
            string prefix = $"{TestPrefix}{runId}_";

            // --- creation, measured in blocks so that any slowdown as the UKS
            // --- grows is visible rather than averaged away
            progress?.Invoke("creating Thoughts...");
            Thought root = theUKS.GetOrAddThought(TestPrefix + "root", "Thought");
            int groups = Math.Max(1, count / GroupWidth);
            int leaves = Math.Max(1, count - groups);
            List<Thought> groupThoughts = new(groups);
            for (int g = 0; g < groups; g++)
                groupThoughts.Add(theUKS.GetOrAddThought($"{prefix}g{g}", root));

            int blockSize = Math.Max(1, leaves / 5);
            List<double> blockTimes = new();
            Stopwatch stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < leaves; i++)
            {
                theUKS.GetOrAddThought($"{prefix}m{i}", groupThoughts[i % groups]);
                if ((i + 1) % blockSize == 0)
                {
                    blockTimes.Add(stopwatch.Elapsed.TotalMilliseconds);
                    stopwatch.Restart();
                    progress?.Invoke($"created {i + 1:N0} of {leaves:N0}...");
                }
            }
            double createMs = blockTimes.Sum();
            report.AppendLine($"UKS stress test — {theUKS.AtomicThoughts.Count:N0} Thoughts in the UKS");
            report.AppendLine();
            report.AppendLine("Creation");
            report.AppendLine($"  {leaves:N0} Thoughts in {createMs:N0} ms " +
                $"({(createMs <= 0 ? 0 : leaves / (createMs / 1000.0)):N0}/sec)");
            if (blockTimes.Count >= 2)
                report.AppendLine($"  first block {blockTimes[0]:N0} ms, last block {blockTimes[^1]:N0} ms " +
                    $"(x{(blockTimes[0] <= 0 ? 0 : blockTimes[^1] / blockTimes[0]):F2} as the UKS filled)");
            report.AppendLine();

            // --- reading the graph
            progress?.Invoke("measuring graph access...");
            Thought hub = groupThoughts[0];
            int hubWidth = hub.Children.Count;
            Thought leaf = theUKS.Labeled($"{prefix}m0");
            int sink = 0;

            double children = Measure(50, () => sink += hub.Children.Count);
            double linksFrom = Measure(50, () => sink += hub.LinksFrom.Count);
            double parents = Measure(2000, () => sink += leaf.Parents.Count);
            double ancestor = Measure(2000, () => sink += leaf.HasAncestor(TestPrefix + "root") ? 1 : 0);
            double labeled = Measure(2000, () => sink += theUKS.Labeled($"{prefix}m1") is null ? 0 : 1);

            report.AppendLine($"Graph access (test classes hold {hubWidth:N0} members each)");
            report.AppendLine($"  Children of a class      {children * 1000,9:F1} us" +
                $"   ({(hubWidth == 0 ? 0 : children * 1_000_000 / hubWidth):F0} ns per member)");
            report.AppendLine($"  LinksFrom, same links    {linksFrom * 1000,9:F1} us" +
                $"   (Children costs {(linksFrom <= 0 ? 0 : children / linksFrom):F1}x this)");
            report.AppendLine($"  Parents of one Thought   {parents * 1000,9:F2} us");
            report.AppendLine($"  HasAncestor              {ancestor * 1000,9:F2} us");
            report.AppendLine($"  Labeled (by name)        {labeled * 1000,9:F2} us");
            report.AppendLine();

            // --- reorganizing the graph
            progress?.Invoke("measuring a class merge...");
            Thought keep = theUKS.GetOrAddThought($"{prefix}keep", root);
            Thought drop = theUKS.GetOrAddThought($"{prefix}drop", root);
            for (int i = 0; i < 100; i++)
            {
                Thought shared = theUKS.GetOrAddThought($"{prefix}s{i}", keep);
                shared.AddParent(drop);
            }
            stopwatch.Restart();
            theUKS.ReplaceThoughtReferences(drop, keep);
            double mergeMs = stopwatch.Elapsed.TotalMilliseconds;

            progress?.Invoke("measuring deletion...");
            int deleteSample = Math.Min(200, leaves / 2);
            stopwatch.Restart();
            for (int i = 0; i < deleteSample; i++)
                theUKS.Labeled($"{prefix}m{i}")?.Delete();
            double deleteMs = stopwatch.Elapsed.TotalMilliseconds;
            double perDelete = deleteSample == 0 ? 0 : deleteMs / deleteSample;

            report.AppendLine("Reorganizing");
            report.AppendLine($"  Merge two classes        {mergeMs,9:F0} ms   for a single merge");
            report.AppendLine($"  Delete one Thought       {perDelete * 1000,9:F0} us   " +
                $"(sampled over {deleteSample})");
            report.AppendLine($"    clearing this UKS one Thought at a time would take about " +
                $"{FormatDuration(perDelete * theUKS.AtomicThoughts.Count)}");
            report.AppendLine();
            report.AppendLine($"Total elapsed {total.Elapsed.TotalSeconds:F1} s   (checksum {sink})");
            return report.ToString();
        }

        /// <summary>
        /// Removes everything this module created, leaving the rest of the UKS
        /// alone. Deletion is presently costly on a large UKS, so the caller is
        /// told how much there is to remove before it starts.
        /// </summary>
        public static string ClearTestItems(Action<string> progress = null)
        {
            var theUKS = MainWindow.theUKS;
            if (theUKS is null) return "No UKS is available.";

            List<Thought> testThoughts = theUKS.AtomicThoughts
                .Where(thought => thought.Label.StartsWith(TestPrefix, StringComparison.Ordinal))
                .ToList();
            if (testThoughts.Count == 0) return "No stress-test Thoughts to remove.";

            Stopwatch stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < testThoughts.Count; i++)
            {
                testThoughts[i].Delete();
                if (progress is not null && i % 2000 == 1999)
                    progress($"removed {i + 1:N0} of {testThoughts.Count:N0}...");
            }

            return $"Removed {testThoughts.Count:N0} Thoughts in " +
                $"{stopwatch.Elapsed.TotalMilliseconds:N0} ms. " +
                $"UKS now holds {theUKS.AtomicThoughts.Count:N0}.";
        }

        /// <summary>
        /// The cost of one call, as the median of three runs after a warm-up.
        /// Without the warm-up the first call also measures compilation.
        /// </summary>
        private static double Measure(int iterations, Action action)
        {
            action();
            List<double> runs = new();
            for (int run = 0; run < 3; run++)
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                for (int i = 0; i < iterations; i++) action();
                runs.Add(stopwatch.Elapsed.TotalMilliseconds / iterations);
            }
            runs.Sort();
            return runs[1];
        }

        private static string FormatDuration(double milliseconds)
        {
            if (milliseconds < 1000) return $"{milliseconds:N0} ms";
            if (milliseconds < 60000) return $"{milliseconds / 1000:N1} seconds";
            return $"{milliseconds / 60000:N1} minutes";
        }

        public override void Fire()
        {
            Init();  //be sure to leave this here
            UpdateDialog();
        }
    }
}
