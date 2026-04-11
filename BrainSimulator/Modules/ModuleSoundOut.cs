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


using NAudio.Midi;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using UKS;
using static UKS.UKS;

namespace BrainSimulator.Modules;

public class ModuleSoundOut : ModuleBase
{

    DateTime lastFiredTime = DateTime.Now;
    DateTime? lastNotePressed = null;
    DateTime lastCadenceTime = DateTime.Now;
    List<Thought> tuneToSearch = null;

    private readonly HashSet<int> _pressedNotes = new();
    private readonly Dictionary<int, Thought> _pitchs = new();
    private const int MinNote = 60; // C4
    private const int MaxNote = 76; // E5

    private const int MidiChannel = 1;
    private const int MidiVelocity = 100;
    private const int MidiPatch = 0; // Acoustic Grand Piano


    IEnumerator<SeqElement> enumerator = null;  //we'll needc multiple enumerators soon

    // Add these properties to the ModuleSound class
    public int Cadence { get; set; } = 100;
    public int PitchOffset { get; set; } = 0;

    public static MidiOut? midi;
    public static MidiOut Midi => midi ??= InitMidi();

    private static MidiOut InitMidi()
    {
        if ( ModuleSoundIn.midi != null)
        {
            midi = ModuleSoundIn.midi;
            return midi;
        }
        var m = new MidiOut(0);
        m.Send(MidiMessage.ChangePatch(MidiPatch, MidiChannel).RawData);
        return m;
    }

    public override void Fire()
    {
        Init();
        lastFiredTime = DateTime.Now;
        UpdateDialog();
    }

    // Fill this method in with code which will execute once
    // when the module is added, when "initialize" is selected from the context menu,
    // or when the engine restart button is pressed
    public override void Initialize()
    {
    }
    public void PlayThePhrase(Thought phrase, bool addToMentalModel = false)
    {
        SeqElement seqStart = (SeqElement)phrase.GetTargetOfFirstLinkOfType("soundAs");
        if (seqStart is null && phrase is SeqElement s)
            seqStart = s; //handle passing in a starting point
        PlayThePhrase(seqStart, addToMentalModel);
    }
    private CancellationTokenSource _phraseCts;
    private Task _phraseTask;

    private void PlayThePhrase(SeqElement start, bool addToMentalModel = false)
    {
        // cancel any current phrase playback
        _phraseCts?.Cancel();
        _phraseTask = null;

        if (start is null) return;

        _phraseCts = new CancellationTokenSource();
        _phraseTask = RunPhraseAsync(start, addToMentalModel, _phraseCts.Token);
    }

    private async Task RunPhraseAsync(SeqElement start, bool addToMentalModel, CancellationToken token)
    {
        var listener = addToMentalModel ? MainWindow.theWindow?.activeModules.OfType<ModuleSoundIn>().FirstOrDefault() : null;
        var enumerator = theUKS.EnumerateSequenceElements(start).GetEnumerator();
        List<int> pitches = new();
        List<int> timesToNextMs = new();
        //get all the relevant info for the phrase
        foreach (var seqNode in theUKS.EnumerateSequenceElements(start) )
        {
            pitches.Add(GetIntegerToRightOfColon(seqNode.VLU.Label));
            Thought timeToNext = seqNode.LinksTo.FindFirst(x => x.LinkType?.Label == "timetonext")?.To;
            timesToNextMs.Add(GetIntegerToRightOfColon(timeToNext?.Label));
        }
        try
        {
            //play the phrase
            for (int i = 0; i < pitches.Count &&!token.IsCancellationRequested;i++ )
            {
                int adjustedNote = pitches[i] + PitchOffset;
                int timeToNextMs = (int)(timesToNextMs[i] * Cadence / 100f);
                int durationMs = 1000; //default duration
                //make sure the current note is not repeated before the duration runs out...causes a mental model timing issue
                int accumTime = timesToNextMs[i];
                for (int j = i+1; j < pitches.Count && accumTime < durationMs; j++)
                {
                    if (pitches[j] == pitches[i])
                    {
                        durationMs = accumTime - 10;
                        break;
                    }
                    accumTime += timesToNextMs[j];
                }
                //output the note
                PlayNote(adjustedNote, addToMentalModel, durationMs);
                //wait the appropriate delay
                if (timeToNextMs > 0)
                    await Task.Delay(timeToNextMs, token);
            }
        }
        catch (OperationCanceledException)
        {
            // ignored
        }
    }
    int GetIntegerToRightOfColon(string value)
    {
        int retVal = -1;
        if (value is null) return retVal;
        int index = value.IndexOf(":");
        string numberString = value[(index + 1)..];
        int.TryParse(numberString, out retVal);

        return retVal;
    }
    public static int GetTimeToNextMs(SeqElement elem)
    {
        var link = elem?.LinksTo.FindFirst(x => x.LinkType?.Label == "timetonext");
        var dtThought = link?.To;
        if (dtThought?.Label is { } lbl && lbl.StartsWith("dt:", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(lbl.AsSpan(3), out int ms))
                return ms;
        }
        return 0;
    }

    // called whenever the UKS performs an Initialize()
    public override void UKSInitializedNotification()
    {
        GetUKS();

        theUKS.GetOrAddThought("MusicalPhrase");
        theUKS.GetOrAddThought("MusicalNoteOut", "MusicalPhrase");
        theUKS.GetOrAddThought("MusicalNoteIn", "MusicalPhrase");
        var s = theUKS.GetOrAddThought("soundAs", "Action");
        lastFiredTime = DateTime.Now;
    }



    public async void PlayCMajorTriad(int timetonextMs = 300)
    {
        int channel = 1;
        int velocity = 100;

        // C major triad: C4, E4, G4
        int c = 60; // C4
        int e = 64; // E4
        int g = 67; // G4

        // Optional: choose instrument (piano)
        Midi.Send(MidiMessage.ChangePatch(0, channel).RawData);

        // Start notes
        Midi.Send(MidiMessage.StartNote(c, velocity, channel).RawData);
        Midi.Send(MidiMessage.StartNote(e, velocity, channel).RawData);
        Midi.Send(MidiMessage.StartNote(g, velocity, channel).RawData);

        await Task.Delay(timetonextMs);

        // Stop notes
        Midi.Send(MidiMessage.StopNote(c, 0, channel).RawData);
        Midi.Send(MidiMessage.StopNote(e, 0, channel).RawData);
        Midi.Send(MidiMessage.StopNote(g, 0, channel).RawData);
    }

    public async void PlayNote(int midiNote, bool AddToMentalModel = false, int timetonextMs = 250)
    {
        //Debug.WriteLine($"PlayNote: {midiNote}  {timetonextMs}");
        var listener = MainWindow.theWindow?.activeModules.OfType<ModuleSoundIn>().FirstOrDefault();
        if (AddToMentalModel)
            listener.StartNote(midiNote);
        Midi.Send(MidiMessage.StartNote(midiNote, MidiVelocity, MidiChannel).RawData);
        _ = Task.Delay(timetonextMs).ContinueWith(_ =>
        {
            if (AddToMentalModel)
                listener.StopNote(midiNote);
            Midi.Send(MidiMessage.StopNote(midiNote, 0, MidiChannel).RawData);
        });
    }
}
