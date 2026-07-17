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

public class ModuleSoundIn : ModuleBase
{

    DateTime lastFiredTime = DateTime.Now;
    DateTime lastCadenceTime = DateTime.Now;

    private readonly Dictionary<int, Thought> _pitchs = new();
    private const int MinNote = 60; // C4
    private const int MaxNote = 76; // E5

    private const int MidiChannel = 1;
    private const int MidiVelocity = 100;
    private const int MidiPatch = 0; // Acoustic Grand Piano


    public static MidiOut midi;
    public static MidiOut Midi => midi ??= InitMidi();

    private static MidiOut InitMidi()
    {
        if (ModuleSoundOut.midi is not null)
        {
            midi = ModuleSoundOut.Midi;
            return midi;
        }
        var m = new MidiOut(0);
        m.Send(MidiMessage.ChangePatch(MidiPatch, MidiChannel).RawData);
        return m;
    }

    public override void Fire()
    {
        Init();

        UpdateDialog();
    }

    // Fill this method in with code which will execute once
    // when the module is added, when "initialize" is selected from the context menu,
    // or when the engine restart button is pressed
    public override void Initialize()
    {
    }


    // called whenever the UKS performs an Initialize()
    public override void UKSInitializedNotification()
    {
        GetUKS();

        theUKS.GetOrAddThought("MusicalPhrase");
        theUKS.GetOrAddThought("MusicalNoteOut", "MusicalPhrase");
        theUKS.GetOrAddThought("MusicalNoteIn", "MusicalPhrase");
        EnsurePitchs();

        var s = theUKS.GetOrAddThought("soundAs", "Action");

        lastFiredTime = DateTime.Now;
    }

    private void EnsurePitchs()
    {
        _pitchs.Clear();
        for (int note = MinNote; note <= MaxNote; note++)
        {
            var t = theUKS.GetOrAddThought($"pitch:{note}", "MusicalNoteIn");
            _pitchs[note] = t;
        }
    }

    public void StartNote(int midiNote)
    {
        Midi.Send(MidiMessage.StartNote(midiNote, MidiVelocity, MidiChannel).RawData);
        HearNote(midiNote);
    }

    public void StopNote(int midiNote)
    {
        Midi.Send(MidiMessage.StopNote(midiNote, 0, MidiChannel).RawData);
        NoteDone(midiNote);
    }

    private ModuleMentalModel GetMentalModel()
    {
        return MainWindow.theWindow?.activeModules.OfType<ModuleMentalModel>().FirstOrDefault();
    }

    public void HearNote(int midiNote)
    {
//        Debug.WriteLine("hearNote: " + midiNote);
        _pitchs.TryGetValue(midiNote, out var noteThought);
        var mm = GetMentalModel();
        if (mm is null || noteThought is null) return;

        float azDeg = (midiNote - 60) * 2.5f;
        var cell = mm.GetCell(Angle.FromDegrees(azDeg), Angle.FromDegrees(0));
        if (cell is not null)
            mm.BindThoughtToMentalModel(noteThought.Label, cell); // renew or add link
    }
    public void NoteDone(int midiNote)
    {
        var mm = GetMentalModel();
        if (mm is null) return;
        if (_pitchs.TryGetValue(midiNote, out var t))
        {
            mm.UnbindThought(t);
        }
    }
}
