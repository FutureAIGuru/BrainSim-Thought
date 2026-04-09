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
using System.Diagnostics;
using System.Linq;
using UKS;

namespace BrainSimulator.Modules;

public class ModuleAttentionS : ModuleBase
{
    private DateTime _lastEventTime = DateTime.MinValue;
    SeqElement currentSeq = null;

    public override void Fire()
    {
        Init();

        foreach (Link l in ((Thought)"activeThought").LinksFrom.Where(x=>x.LinkType.Label== "is-a"))
        {
            if (l.HasLink(l,"handled", null) is not null) continue; //only handle this event once
            l.AddLink("handled", null);
            if (!l.From.HasAncestor("musicalPhrase")) continue;

            //Add the new node to the phrase sequence
            Thought theNote = l.From;
            if (currentSeq is null)
            {
                theUKS.GetOrAddThought("phrase", "musicalPhrase");
                currentSeq = theUKS.CreateFirstElement("phrase", theNote);
            }
            else

            {
                AddTimeToNextToSeqStep();
                //This code could be modified so that the sequences stored deltas rather than note values
                //var deltaNote = GetIntAfterColon(theNote.Label)-GetIntAfterColon(currentSeq.VLU.Label) ;
                //Thought theInterval = theUKS.GetOrAddThought($"interval:{deltaNote}", "musicalNote");
                //currentSeq.AddLink("VLU", theInterval);
                currentSeq = theUKS.AddElement(currentSeq, theNote);
                Debug.WriteLine("Note Added:" + theNote.Label);

            }
            _lastEventTime = DateTime.Now;
        }

        //Following might be useful to store note durations
        //foreach (Link l in ((Thought)"inActiveThought").LinksFrom.Where(x => x.LinkType.Label == "is-a"))
        //{
        //    if (l.HasLink(l, "handled", null) is not null) continue; //only handle this event once
        //    l.AddLink("handled", null);
        //    if (currentSeq is null) continue; //never start a sequence with a note-end
        //    AddTimeToNextToSeqStep();
        //    currentSeq = theUKS.AddElement(currentSeq, l.From);
        //    Debug.WriteLine($"added end event {l.From}");
        //    _lastEventTime = DateTime.Now;
        //}

        //save the sequence as a phrase
        if (_lastEventTime != DateTime.MinValue &&
          DateTime.Now - _lastEventTime >= TimeSpan.FromSeconds(2) && currentSeq is not null)
        {
            var theFlattenedSequence = theUKS.FlattenSequence(currentSeq.FRST);
            //does this sequence already exist? (note, it always findes the currentSeq PLUS any others)
            Thought existing = null;
            bool completeMatch = false;
            var existing1 = theUKS.HasSequence(theFlattenedSequence, "soundAs", true);
            if (existing1.Count > 0)
            {
                var existing2 = theUKS.GetReferringThoughts(existing1[0].seqNode, "soundAs");
                if (existing2.Count > 0)
                    existing = existing2[0];
                int existingLength = theUKS.GetSequenceLength(existing1[0].seqNode);
                if (existingLength == theFlattenedSequence.Count)
                    completeMatch = true;
            }
            if (existing is null)
            {
                //solidify the new sequence (phrase)
                theUKS.GetOrAddThought("musicalPhrase");
                theUKS.GetOrAddThought("soundAs", "LinkType");
                var newThought = theUKS.GetOrAddThought("phrase*", "musicalPhrase").AddLink("soundAs", currentSeq.FRST);
                int totalTime = 5000;
                foreach (SeqElement t in theUKS.EnumerateSequenceElements(currentSeq.FRST))
                    totalTime += ModuleSoundOut.GetTimeToNextMs(t) * 3;
                Debug.WriteLine($"new phrase heard {newThought.Label}");
                newThought.From.TimeToLive = TimeSpan.FromMilliseconds(totalTime*3);
                //newThought.From.TimeToLive = TimeSpan.MaxValue; //permanent for debugging
                // Notify ModuleAction of the new context
                var moduleAction = MainWindow.theWindow?.activeModules.OfType<ModuleAction>().FirstOrDefault();
                moduleAction?.NewContext(newThought.From);
            }
            else
            {
                existing?.Fire();
                Debug.WriteLine($"phrase recognized {existing.Label}");
                var moduleAction = MainWindow.theWindow?.activeModules.OfType<ModuleAction>().FirstOrDefault();
                if (completeMatch)
                {
                    // Notify ModuleAction of the new context
                    Debug.WriteLine($"phrase exact match {existing.Label}");
                    moduleAction.NewContext(existing);
                    ModuleWellBeing.Increase();  //we're happy we recognized something
                }
                else //perhaps complete the phrase
                {
                    Debug.WriteLine($"phrase init match {existing.Label}");
                    var e = theUKS.EnumerateSequenceElements(existing1[0].seqNode).GetEnumerator();
                    e.MoveNext();

                    for (int i = 0; i < theFlattenedSequence.Count;i++)
                        e.MoveNext();
                    moduleAction.TakeActrion(e.Current);
                }
                theUKS.DeleteSequence(currentSeq.FRST);
            }

            currentSeq = null;
            _lastEventTime = DateTime.MinValue;
        }

        UpdateDialog();
    }

/*  
    //useful for deltas
    static int GetIntAfterColon(string s)
    {
        int idx = s.IndexOf(':');
        if (idx < 0 || idx == s.Length - 1) return 0;
        return int.TryParse(s[(idx + 1)..], out var val) ? val : 0;
    }

  */
    private int AddTimeToNextToSeqStep()
    {
        TimeSpan delta = _lastEventTime == DateTime.MinValue ? TimeSpan.Zero : DateTime.Now - _lastEventTime;
        if (delta.TotalMilliseconds < 100) delta = TimeSpan.Zero;
        var dt = theUKS.GetOrAddThought($"dt:{(int)delta.TotalMilliseconds}", "timetonext"); // Keep timetonext helper intact
        currentSeq.AddLink("timetonext", dt);
        return (int)delta.TotalMilliseconds;
    }

    public override void Initialize()
    {
        _lastEventTime = DateTime.MinValue;
    }

    public override void UKSInitializedNotification()
    {
        EnsureSequenceRoots();
        _lastEventTime = DateTime.MinValue;
    }

    private void EnsureSequenceRoots()
    {
        theUKS.GetOrAddThought("handled", "LinkType");
        theUKS.GetOrAddThought("timetonext", "LinkType");
    }

    private List<Thought> PredictNextNote()
    {
        if (currentSeq?.FRST is null) return null;

        List<Thought> retVal = new();
        List<Thought> played = theUKS.FlattenSequence(currentSeq.FRST);
        if (played.Count == 0) return null;

        // Find all known phrases that start with the current sequence
        Thought predicted = null;
        var foundSequences = theUKS.HasSequence(played, "soundAs");
        if (foundSequences.Count > 0)
        {
            foreach (var foundSequence in foundSequences)
            {
                var sequenceContent = theUKS.FlattenSequence(foundSequence.seqNode);
                for (int i = 0; i < played.Count && i < sequenceContent.Count; i++)
                {
                    if (sequenceContent[i] != played[i]) goto misMatch;
                }
                if (sequenceContent.Count > played.Count)
                    predicted = sequenceContent[played.Count];
            misMatch: continue;
            }
            if (predicted != null)
                retVal.Add(predicted);
        }
        return retVal;
    }


    private ModuleMentalModel GetMentalModel()
    {
        return MainWindow.theWindow?.activeModules.OfType<ModuleMentalModel>().FirstOrDefault();
    }
    private ModuleWellBeing GetWellBeing()
    {
        return MainWindow.theWindow?.activeModules.OfType<ModuleWellBeing>().FirstOrDefault();
    }
}