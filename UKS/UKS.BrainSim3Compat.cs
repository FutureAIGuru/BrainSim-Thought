/*
 * BrainSim3 / Vision API compatibility on UKS.
 */

namespace UKS;

public partial class UKS
{
    private List<(Thought t, float conf)>? _closestMatchResults;
    private int _closestMatchIndex;

    public IReadOnlyList<Thought> UKSList
    {
        get
        {
            lock (AtomicThoughts)
                return AtomicThoughts.ToList();
        }
    }

    public Thought? GetOrAddThing(string label, object? parent = null, Thought? source = null) =>
        GetOrAddThought(label, parent, source);

    public Thought? AddThing(string label, Thought parent) =>
        GetOrAddThought(label, parent);

    public void DeleteAllChildren(Thought t)
    {
        if (t is null) return;
        foreach (Thought child in t.Children.ToList())
            child.Delete();
    }

    public Relationship? GetRelationship(string fromLabel, string linkTypeLabel, string toLabel)
    {
        Thought? from = Labeled(fromLabel);
        Thought? linkType = Labeled(linkTypeLabel);
        Thought? to = Labeled(toLabel);
        if (from is null || linkType is null || to is null) return null;
        return Relationship.FromLink(GetLink(from, linkType, to));
    }

    public Thought? SearchForClosestMatch(Thought target, Thought root, ref float bestValue)
    {
        _closestMatchResults = SearchForClosestMatch(target, root);
        _closestMatchIndex = 0;
        if (_closestMatchResults.Count == 0)
        {
            bestValue = 0;
            return null;
        }
        bestValue = _closestMatchResults[0].conf;
        return _closestMatchResults[0].t;
    }

    public Thought? GetNextClosestMatch(ref float bestValue)
    {
        if (_closestMatchResults is null) return null;
        _closestMatchIndex++;
        if (_closestMatchIndex >= _closestMatchResults.Count)
            return null;
        bestValue = _closestMatchResults[_closestMatchIndex].conf;
        return _closestMatchResults[_closestMatchIndex].t;
    }

    /// <summary>Compare rotational go* feature chains between stored and live shapes.</summary>
    public float HasSequence(Thought candidate, Thought pattern, out int offset, bool circular)
    {
        offset = 0;
        List<Link> patternLinks = pattern.LinksTo
            .Where(x => x.LinkType?.Label?.StartsWith("go", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();
        List<Link> candidateLinks = candidate.LinksTo
            .Where(x => x.LinkType?.Label?.StartsWith("go", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();
        if (patternLinks.Count == 0 || candidateLinks.Count == 0)
            return 0f;

        float bestScore = 0f;
        int bestOffset = 0;
        int maxStart = circular ? candidateLinks.Count : 1;
        for (int start = 0; start < maxStart; start++)
        {
            int matches = 0;
            for (int i = 0; i < patternLinks.Count; i++)
            {
                int ci = circular ? (start + i) % candidateLinks.Count : start + i;
                if (ci >= candidateLinks.Count) break;
                if (patternLinks[i].To?.Label == candidateLinks[ci].To?.Label)
                    matches++;
            }
            float score = (float)matches / patternLinks.Count;
            if (score > bestScore)
            {
                bestScore = score;
                bestOffset = start;
            }
        }
        offset = bestOffset;
        return bestScore;
    }
}