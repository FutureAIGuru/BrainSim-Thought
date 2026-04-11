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

public class ModuleAction : ModuleBase
{
    private readonly Random _rng = new();

    public Thought _lastContext;
    public Thought _lastSelectedAction;

    public override void Fire()
    {
        Init();
        // look for any new actions to take (children of "do"), dispatch them to the appropriate module, and then remove them from "do"
        Thought doRoot = theUKS.GetOrAddThought("do");
        for (int i = 0; i < doRoot.Children.Count; i++)
        {
            Thought actionToTake = doRoot.Children[i];
            bool isMusic = actionToTake.HasAncestor("musicalPhrase");
            if (!isMusic && actionToTake is SeqElement s) //hack needed to play partial phrases
                isMusic = s.VLU.HasAncestor("musicalPhrase");
            var learnMelodyModule = MainWindow.theWindow?.activeModules.OfType<ModuleLearnMelody>().FirstOrDefault();
            if (learnMelodyModule is not null)
            {
                NewAction(actionToTake);
                if (learnMelodyModule.ResponseHandled(actionToTake))
                {
                    actionToTake.RemoveParent(doRoot);
                    i--;
                    continue;
                }
            }

            if (isMusic)
            {
                var soundOutModule = MainWindow.theWindow?.activeModules.OfType<ModuleSoundOut>().FirstOrDefault();
                if (soundOutModule is not null)  //we need a case statement to dispatch different action types to different modules,
                                                 //but for now we just have one action type and one module that can handle it
                {
                    Debug.WriteLine($"play phrase {actionToTake.Label}");
                    soundOutModule.PlayThePhrase(actionToTake, false);
                }
            }
            NewAction(actionToTake);
            actionToTake.RemoveParent(doRoot);
            i--;
        }
        UpdateDialog();
    }

    public void TakeActrion(Thought action)
    {
        Thought doRoot = theUKS.GetOrAddThought("do");
        _lastSelectedAction = action;
        action.AddParent(doRoot);
    }

    public override void Initialize()
    {
        GetUKS();
        EnsureSchema();
        _lastContext = null;
        _lastSelectedAction = null;
    }

    public override void UKSInitializedNotification()
    {
        EnsureSchema();
        _lastContext = null;
        _lastSelectedAction = null;
    }

    public void NewAction(Thought action)
    {
        Debug.WriteLine($"New action: {action.Label}");
        SetResponseLink(_lastContext, action, 0);
        if (action is not SeqElement)
            action.AddParent("possibleAction");
        _lastSelectedAction = action;
    }

    public void NewContext(Thought context)
    {
        if (context == _lastSelectedAction)
            return;
        if (context is not null)
        {
            Debug.WriteLine($"New context: {context.Label}");
            Thought action = SelectAction(context);
            if (action is not null)
            {
                TakeActrion(action);
            }
            context.AddParent("context");
            _lastContext = context;
        }
    }

    /// <summary>
    /// Record the wellbeing delta for the most recent action/context as an action triple:
    /// Action(child of "Action") --contextOf--> Context, --actionTaken--> ActionType, --deltaOf--> DeltaThought.
    /// </summary>
    public void NewResult(float delta)
    {
        Debug.WriteLine($"New result: {_lastContext?.Label}, {_lastSelectedAction?.Label}, {delta}");
        SetResponseLink(_lastContext, _lastSelectedAction, delta);
    }

    public Thought SetResponseLink(Thought context, Thought actionTaken, float delta)
    {
        if (context == actionTaken) return null; //You can't respond with a replay
        if (actionTaken is SeqElement s) return null;  // you don't save a partial
        if (context is null) return null;
        //first check if it already exists
        foreach (Link l in context.LinksTo.Where(x => x.LinkType.Label == "response" && x.To == actionTaken))
        {
            if (delta == 0f) return l.From;
            float oldDelta = l.Weight;
            float newDelta = oldDelta + delta;
            l.Weight = newDelta;
//            if (newDelta < 0)
//                l.From.RemoveLink(l);
            return l.From;
        }
        Thought actionRecord = context;
        actionRecord.AddParent("context");
        if (actionTaken is not null)
        {
            actionRecord.AddLink("response", actionTaken).Weight = delta;
        }
        return actionRecord;
    }

    /// <summary>
    /// Choose the best action for the current context (highest positive delta); otherwise return a random known action.
    /// Stores the chosen action in _lastSelectedAction and the evaluated context in _lastContext.
    /// </summary>
    public Thought SelectAction(Thought newContext)
    {
        if (newContext is null) return null;

        Thought bestAction = null;
        float bestDelta = float.NegativeInfinity;

        foreach (Link l in newContext?.LinksTo)
        {
            if (l.LinkType.Label != "response") continue;
            Thought actionTaken = l.To;
            if (actionTaken is null) continue;

            float d = l.Weight;
            if (d > bestDelta)
            {
                bestDelta = d;
                bestAction = actionTaken;
            }
        }

        if (bestDelta > 0 && bestAction is not null)
        {
            TakeActrion(bestAction);
            Debug.WriteLine("Best action Chosen: " + bestAction.Label);
            return bestAction;
        }

        var actionToTake = PickRandomKnownAction(newContext);
        return actionToTake;
    }

    private void EnsureSchema()
    {
        GetUKS();
        if (theUKS is null) return;

        theUKS.GetOrAddThought("do", "Action");
        theUKS.GetOrAddThought("context", "Action");
        theUKS.GetOrAddThought("response", "LinkType");
        theUKS.GetOrAddThought("possibleAction", "Action");
        theUKS.GetOrAddThought("noAction", "possibleAction");
    }

    //This will be needed to AND mu;ltiple contextx
    private Thought CaptureCurrentContext()
    {
        EnsureSchema();
        List<Thought> items = GetActiveInputs();
        if (items.Count == 0) return null;
        var contextRoot = theUKS.GetOrAddThought("context");

        foreach (Thought existing in contextRoot.Children)
            if (ContextEquals(existing, items))
                return existing;

        Thought ctx = theUKS.GetOrAddThought("context:*", contextRoot);
        //foreach (Thought item in items)
        //    ctx.AddLink(_ltContextItem, item);
        return ctx;
    }

    private List<Thought> GetActiveInputs()
    {
        Thought active = ThoughtLabels.GetThought("activeThought");
        if (active is null) return new List<Thought>();

        return active.LinksFrom
            .Where(x => x.LinkType?.Label == "is-a" && x.From is not null)
            .Select(x => x.From)
            .Distinct()
            .ToList();
    }

    private bool ContextMatches(Thought actionRecord, Thought ctx)
    {
        /*        Thought storedCtx = actionRecord.LinksTo.FindFirst(x => x.LinkType == _ltContextOf)?.To;
                if (storedCtx is null || ctx is null) return false;
                return ContextEquals(storedCtx, GetContextItems(ctx));
          */
        return false;
    }

    private bool ContextEquals(Thought ctxNode, IReadOnlyCollection<Thought> items)
    {
        return ContextEquals(ctxNode, items.ToList());
    }

    private bool ContextEquals(Thought ctxNode, List<Thought> items)
    {
        var stored = GetContextItems(ctxNode);
        if (stored.Count != items.Count) return false;
        return !stored.Except(items).Any();
    }

    private List<Thought> GetContextItems(Thought ctxNode)
    {
        /*        return ctxNode?.LinksTo
                    .Where(x => x.LinkType == _ltContextItem && x.To is not null)
                    .Select(x => x.To)
                    .Distinct()
                    .ToList() ?? new List<Thought>();
          */
        return new List<Thought>();
    }

    /*    private double GetDelta(Thought actionRecord)
        {
            Thought deltaThought = actionRecord.LinksTo.FindFirst(x => x.LinkType == _ltDeltaOf)?.To;
            if (deltaThought?.V is double d) return d;
            if (deltaThought?.V is float f) return f;
            if (deltaThought?.Label?.StartsWith("delta:") == true &&
                double.TryParse(deltaThought.Label["delta:".Length..], out double parsed))
                return parsed;
            if (actionRecord.V is double da) return da;
            if (actionRecord.V is float fa) return fa;
            return 0;
        }
    */
    private Thought PickRandomKnownAction(Thought t)
    {
        var options = ((Thought)"possibleAction").Children
            .Where(t1 => t1 is not null)
            .Distinct()
            .ToList();

        //remove options which have already been tried
        // Remove options which have already been tried
        if (t is not null)
        {
            options = options.Where(option =>
                !t.LinksTo.Any(link => link.LinkType?.Label == "response" && link.To == option)
            ).ToList();
        }


        if (options.Count ==0)
            return null; // fallback default
        int val = (int)_rng.Next(options.Count);
        Thought retVal = options[val];
        return retVal;

    }
}