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

using System.Diagnostics;
using System.Text;

namespace UKS;

/// <summary>
/// Records where sequence discovery spends its effort. It is off by default and
/// costs one boolean test when off.
///
/// Work is counted exactly and timed only in coarse phases. Timing each call of
/// a small method which runs millions of times would cost more than the method
/// and would report the measurement rather than the work.
/// </summary>
public static class SequenceDiscoveryDiagnostics
{
    public static bool Enabled;

    // Counts of work done.
    public static long FindCommonSequenceCalls;
    public static long LcsCalls;
    public static long LcsCells;
    public static long MatchCalls;
    public static long MatchPositionSteps;
    public static long SignatureCalls;
    public static long SignatureChars;

    // Elapsed time of each phase, in milliseconds.
    public static double GroupingMs;
    public static double PairProposalMs;
    public static double ConsolidationMs;
    public static double MaterializeMs;

    // Within the materialize phase.
    public static double MemberSelectionMs;
    public static double ClassCreationMs;
    public static double TemplateLookupMs;
    public static double EvidenceMs;
    public static long EvidenceStatements;

    public static void Reset()
    {
        FindCommonSequenceCalls = 0;
        LcsCalls = 0;
        LcsCells = 0;
        MatchCalls = 0;
        MatchPositionSteps = 0;
        SignatureCalls = 0;
        SignatureChars = 0;
        GroupingMs = 0;
        PairProposalMs = 0;
        ConsolidationMs = 0;
        MaterializeMs = 0;
        MemberSelectionMs = 0;
        ClassCreationMs = 0;
        TemplateLookupMs = 0;
        EvidenceMs = 0;
        EvidenceStatements = 0;
    }

    /// <summary>Starts a phase timer. Returns a timestamp to pass to Stop.</summary>
    public static long Start() => Enabled ? Stopwatch.GetTimestamp() : 0;

    public static double Elapsed(long startTimestamp)
    {
        if (!Enabled) return 0;
        return (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;
    }

    public static string Report()
    {
        double totalMs = GroupingMs + PairProposalMs + ConsolidationMs + MaterializeMs;
        StringBuilder report = new();
        report.AppendLine($"Sequence discovery: {totalMs:F0} ms measured across phases");
        report.AppendLine($"  grouping/dedupe        {GroupingMs,8:F0} ms  {Share(GroupingMs, totalMs)}");
        report.AppendLine($"  pair proposals         {PairProposalMs,8:F0} ms  {Share(PairProposalMs, totalMs)}");
        report.AppendLine($"  consolidation/expand   {ConsolidationMs,8:F0} ms  {Share(ConsolidationMs, totalMs)}");
        report.AppendLine($"  materialize/create     {MaterializeMs,8:F0} ms  {Share(MaterializeMs, totalMs)}");
        report.AppendLine($"      select members     {MemberSelectionMs,8:F0} ms  {Share(MemberSelectionMs, MaterializeMs)}");
        report.AppendLine($"      create classes     {ClassCreationMs,8:F0} ms  {Share(ClassCreationMs, MaterializeMs)}");
        report.AppendLine($"      find template      {TemplateLookupMs,8:F0} ms  {Share(TemplateLookupMs, MaterializeMs)}");
        report.AppendLine($"      attach evidence    {EvidenceMs,8:F0} ms  {Share(EvidenceMs, MaterializeMs)}" +
            $"  ({EvidenceStatements:N0} statements)");
        report.AppendLine();
        report.AppendLine("  work performed");
        report.AppendLine($"    FindCommonSequence   {FindCommonSequenceCalls,12:N0} calls");
        report.AppendLine($"    LCS                  {LcsCalls,12:N0} calls, " +
            $"{LcsCells:N0} matrix cells");
        report.AppendLine($"    SequenceMatchesPattern {MatchCalls,10:N0} calls, " +
            $"{MatchPositionSteps:N0} position steps");
        report.AppendLine($"    signatures           {SignatureCalls,12:N0} built, " +
            $"{SignatureChars:N0} chars");
        return report.ToString();

        static string Share(double part, double whole) =>
            whole <= 0 ? "" : $"({part / whole:P0})";
    }
}
