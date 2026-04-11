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
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Forms.Design;
using UKS;
using static BrainSimulator.Modules.ModuleOnlineInfo;

namespace BrainSimulator.Modules;

public class ModuleLearnMelody : ModuleBase
{
    private DateTime _lastProcessTime = DateTime.MinValue;
    private int _processIntervalMs = 250; // Default 1 second between lines
    private List<string> _melodyLines = new();
    private List<string> _randomizedLines = new();
    private int _currentIndex = 0;
    private int _remainingCycles = 0;
    private readonly Random _random = new();
    private Thought responseTarget;
    private Thought stimulous;

    // Fill this method in with code which will execute
    // once for each cycle of the engine
    public override void Fire()
    {
        Init();

        // Check if enough time has elapsed and we have work to do
        if (_remainingCycles > 0 &&
            DateTime.Now - _lastProcessTime >= TimeSpan.FromMilliseconds(_processIntervalMs))
        {
            ProcessNextLine();
            _lastProcessTime = DateTime.Now;
        }

        UpdateDialog();
    }

    // Fill this method in with code which will execute once
    // when the module is added, when "initialize" is selected from the context menu,
    // or when the engine restart button is pressed
    public override void Initialize()
    {
        _lastProcessTime = DateTime.MinValue;
        _currentIndex = 0;
        _remainingCycles = 0;
    }

    // called whenever the UKS performs an Initialize()
    public override void UKSInitializedNotification()
    {
    }

    // Process one line from the randomized list
    private void ProcessNextLine()
    {
        if (_currentIndex == 0 && _remainingCycles > 0)
        {
            RandomizeMelodies();
        }

        if (_randomizedLines.Count == 0) return;

        // Get and parse current line
        string line = _randomizedLines[_currentIndex];
        var entry = ParseMelodyLine(line);

        if (entry is not null)
        {
            ProcessMelodyEntry(entry);
        }

        // Move to next entry
        _currentIndex++;

        // Check if we've finished the current pass
        if (_currentIndex >= _randomizedLines.Count)
        {
            _remainingCycles--;
            _currentIndex = 0;

            // If we still have cycles remaining, randomize again
            if (_remainingCycles > 0)
            {
                RandomizeMelodies();
            }
        }
    }

    // Process a single melody entry - to be implemented
    private void ProcessMelodyEntry(MelodyEntry entry)
    {
        Thought thePhrase = theUKS.GetOrAddThought(entry.Name, "MusicalPhrase");
        SeqElement seq1 = CreatePhraseFromNotes(thePhrase, entry.InputPhrase.Notes);
        thePhrase.AddLink("soundAs", seq1);
        thePhrase.AddParent("context");
        stimulous = thePhrase;

        var moduleAction = MainWindow.theWindow?.activeModules.OfType<ModuleAction>().FirstOrDefault();
        moduleAction?.NewContext(thePhrase);

        //decode the test response
        SeqElement seq2 = CreatePhraseFromNotes("temp*", entry.ResponsePhrase.Notes);
        foreach (Thought t in theUKS.Labeled("possibleAction").Children)
        {
            SeqElement seq3 = GetTargetOfFirstLinkOfType(t, "soundAs");
            var val = theUKS.CompareSequences(seq2, seq3);
            if (val == 1)
            {
                responseTarget = theUKS.GetReferringThoughts(seq3, "soundAs")[0];
                Debug.WriteLine("Response Target Detected  Stimulous: " + thePhrase.Label + " Desired Respons: " + responseTarget.Label);
                break;
            }
        }
    }

    //This is the learning algorithm...
    //we're given the proposed response, the stimulous, and the correct response.
    public bool ResponseHandled (Thought theResponse)
    {
        if (responseTarget is null) return false;  //we are not learning, it's OK to play the phrase

        if (theResponse == responseTarget)
        {
            Debug.WriteLine("CORRECT response:  " + stimulous.Label + " " + theResponse);
            ModuleWellBeing.Increase();
        }
        else
        {
            Debug.WriteLine("INCorrect response:  " + stimulous.Label + " " + theResponse +" target: "+responseTarget.Label);
            ModuleWellBeing.Decrease();
        }
        stimulous = null;
        responseTarget = null;
        return true;
    }

    //TODO Move to UKS
    public SeqElement GetTargetOfFirstLinkOfType(Thought thePhrase, string v)
    {
        foreach (var link in thePhrase.LinksTo)
        {
            if (link.LinkType.Label == v && link.To is SeqElement seq)
            {
                return seq;
            }
        }
        return null;
    }


    // Randomize the melody lines
    private void RandomizeMelodies()
    {
        _randomizedLines = _melodyLines.OrderBy(x => _random.Next()).ToList();
    }

    // Start processing the loaded melodies N times
    public void StartProcessing(int cycles)
    {
        if (_melodyLines.Count == 0) return;

        _remainingCycles = cycles;
        _currentIndex = 0;
        _lastProcessTime = DateTime.MinValue;
        RandomizeMelodies();
    }

    // Set the processing interval in milliseconds
    public void SetProcessInterval(int intervalMs)
    {
        _processIntervalMs = Math.Max(100, intervalMs); // Minimum 100ms
    }

    // This method will be called by the dialog when a file is loaded
    public int LoadMelodiesFromFile(string filePath)
    {
        _melodyLines.Clear();
        _randomizedLines.Clear();
        _currentIndex = 0;
        _remainingCycles = 1;

        try
        {
            foreach (string line in File.ReadLines(filePath))
            {
                string trimmed = line.Trim();

                // Skip blank lines and comments
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#"))
                    continue;

                _melodyLines.Add(trimmed);
            }

            LoadOutputPhrases(_melodyLines);
            return _melodyLines.Count;
        }
        catch (Exception ex)
        {
            // Handle file reading errors
            System.Diagnostics.Debug.WriteLine($"Error loading file: {ex.Message}");
            return 0;
        }
    }

    private void LoadOutputPhrases(List<string> melodyLines)
    {
        //only do once
        if (theUKS.Labeled("phraseO0") != null) return;
        foreach (string line in melodyLines)
        {
            var entry = ParseMelodyLine(line);
            Thought thePhrase = theUKS.GetOrAddThought("phraseO*","MusicalPhrase");
            SeqElement seq1 = CreatePhraseFromNotes(thePhrase,entry.ResponsePhrase.Notes);
            thePhrase.AddLink("soundAs", seq1);
            thePhrase.AddParent("possibleAction");
        }
    }

    SeqElement CreatePhraseFromNotes(Thought t, List<(int Pitch, int TimeToNext)> notes)
    {
        List<Thought> targets = new();
        foreach (var note in notes)
            targets .Add(theUKS.GetOrAddThought("pitch:" + note.Pitch));
        var existing = theUKS.RawSearchExact(targets);
        if (existing.Count > 0)
        {
            //check to make sure the durations match too
            SeqElement start =  existing[0].seqNode;
            SeqElement current = start;
            foreach (var note in notes)
            {
                //add check here... jump to create new if wrong
            }
            return start;
        }

        SeqElement theSeq = null;
        foreach (var note in notes)
        {
            if (theSeq is null)
            {
                theSeq = theUKS.CreateFirstElement(t.Label, theUKS.GetOrAddThought("pitch:" + note.Pitch,"MusicalNoteIn"));
                theSeq.ToString();
                t.AddLink("soundAs", theSeq);
            }
            else
            {
                theSeq = theUKS.AddElement(theSeq, theUKS.GetOrAddThought("pitch:" + note.Pitch));
            }
            theSeq.AddLink("timetonext", theUKS.GetOrAddThought($"dt:{note.TimeToNext}", "timetonext"));
        }
        return theSeq.FRST;
    }


    // Represents a parsed melody line
    private class MelodyEntry
    {
        public string Name { get; set; }
        public Phrase InputPhrase { get; set; }
        public Phrase ResponsePhrase { get; set; }
    }

    // Represents a musical phrase
    private class Phrase
    {
        public List<int> Metadata { get; set; } = new();
        public List<(int Pitch, int TimeToNext)> Notes { get; set; } = new();
    }

    // Parses a line in format: <MelodyName> | <input phrase> => <response phrase>
    private MelodyEntry ParseMelodyLine(string line)
    {
        try
        {
            // Split by "|" to get melody name and phrases
            var parts = line.Split('|');
            if (parts.Length != 2) return null;

            string melodyName = parts[0].Trim();

            // Split by "=>" to get input and response phrases
            var phraseParts = parts[1].Split("=>");
            if (phraseParts.Length != 2) return null;

            string inputPhraseStr = phraseParts[0].Trim();
            string responsePhraseStr = phraseParts[1].Trim();

            return new MelodyEntry
            {
                Name = melodyName,
                InputPhrase = ParsePhrase(inputPhraseStr),
                ResponsePhrase = ParsePhrase(responsePhraseStr)
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error parsing melody line: {ex.Message}");
            return null;
        }
    }

    // Parses a phrase in format: (integers) pitch1:timetonext1 pitch2:timetonext2 ...
    private Phrase ParsePhrase(string phraseStr)
    {
        var phrase = new Phrase();
        phraseStr = phraseStr.Trim();

        try
        {
            // Check for optional metadata in parentheses
            if (phraseStr.StartsWith("("))
            {
                int closeParen = phraseStr.IndexOf(')');
                if (closeParen > 0)
                {
                    string metadataStr = phraseStr.Substring(1, closeParen - 1);
                    phraseStr = phraseStr[(closeParen + 1)..].Trim();

                    // Parse metadata integers
                    foreach (string num in metadataStr.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (int.TryParse(num, out int val))
                            phrase.Metadata.Add(val);
                    }
                }
            }

            // Parse pitch:timetonext pairs
            var noteParts = phraseStr.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string notePart in noteParts)
            {
                var pitchDur = notePart.Split(':');
                if (pitchDur.Length == 2 &&
                    int.TryParse(pitchDur[0], out int pitch) &&
                    int.TryParse(pitchDur[1], out int timetonext))
                {
                    phrase.Notes.Add((pitch, timetonext));
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error parsing phrase: {ex.Message}");
        }

        return phrase;
    }
}