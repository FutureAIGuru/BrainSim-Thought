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

namespace UKS;

public partial class UKS
{
	/// <summary>
	/// Creates a Thought. <br/>
	/// Parameters are strings. If the Thoughts with those labels
	/// do not exist, they will be created. <br/>
	/// If the LinkType has an inverse, the inverse will be used and the Thought will be reversed so that 
	/// Fido Is-a Dog become Dog Has-child Fido.<br/>
	/// </summary>
	/// <param name="sSource">string or Thought for the source.</param>
	/// <param name="sLinkType">string or Thought for the link type.</param>
	/// <param name="sTarget">string or Thought (or null) for the target.</param>
	/// <param name="label">Optional label for the created link Thought.</param>
	/// <returns>The primary link which was created (others may be created for given attributes).</returns>
	public Link AddStatement(string sSource, string sLinkType, string sTarget, string label = "")
	{
		Thought source = ThoughtFromObject(sSource);
		Thought linkType = ThoughtFromObject(sLinkType, "LinkType", source);
		Thought target = ThoughtFromObject(sTarget);

		Link theLink = AddStatement(source, linkType, target, label);
		return theLink;
	}

	/// <summary>
	/// Adds a statement relating the specified source, link type, and target. No new Thoughts are created.
	/// </summary>
	public Link AddStatement(Thought source, Thought linkType, Thought target, string label = "")
	{
		if (source is null || linkType is null) return null;

		Thought t = Labeled(label);
		Link existing = t as Link;
		Link lnk = null;
		if (existing == null) existing = GetLink(source, linkType, target);

		if (existing is null)
		{
			//create the link but don't add it to the UKS
			lnk = CreateTheLink(source, linkType, target);
			existing = GetLink(lnk);
		}
		else
		{
			existing.LinkType = linkType;
			existing.To = target;
			existing.From = source;
            existing.From.Fire();
            existing.LinkType.Fire();
            existing.To.Fire();
        }

        //does this link already exist (without conditions)?
        if (existing is not null)
		{
			WeakenConflictingLinks(source, existing);
			existing.Fire();
			return existing;
		}
		if (lnk?.From?.Label == "") lnk.From.AddDefaultLabel();
		if (lnk?.To?.Label == "") lnk.To.AddDefaultLabel();
		if (!string.IsNullOrEmpty(label))
			lnk.Label = label.Trim();

		WeakenConflictingLinks(source, lnk);

		WriteTheLink(lnk);
		if (lnk.LinkType is not null && HasProperty(lnk.LinkType, "isCommutative"))
		{
			Link rReverse = new Link(lnk.To, lnk.LinkType, lnk.From);
			WriteTheLink(rReverse);
		}

		//if this is adding a child link, remove any Unknown parent
		ClearExtraneousParents(lnk.From);
		ClearExtraneousParents(lnk.To);
		ClearExtraneousParents(lnk.LinkType);

		return lnk;
	}

	private Link CreateTheLink(Thought source, Thought linkType, Thought target)
	{
		Thought inverseType1 = CheckForInverse(linkType);
		//if this link has an inverse, switcheroo so we are storing consistently in one direction
		if (inverseType1 is not null)
		{
			(source, target) = (target, source);
			linkType = inverseType1;
		}

		Link r = new()
		{ From = source, LinkType = linkType, To = target };
		return r;
	}

	private void WeakenConflictingLinks(Thought newSource, Link newLink)
	{
		if (newSource is null || newLink is null) return;

		for (int i = 0; i < newSource.LinksTo.Count; i++)
		{
			Link existingLink = newSource.LinksTo[i];
			if (existingLink == newLink)
			{
				newLink.Weight += (1 - newLink.Weight) / 2.0f;
				newLink.Fire();
			}
			else if (LinksAreExclusive(newLink, existingLink))
			{
				if (newLink.LinkType?.Children.Contains(existingLink.LinkType) == true && HasAttribute(existingLink.LinkType, "not"))
				{
					existingLink.From?.RemoveLink(existingLink);
				}
				else if (existingLink.LinkType?.Children.Contains(newLink.LinkType) == true && HasAttribute(newLink.LinkType, "not"))
				{
					existingLink.From?.RemoveLink(existingLink);
				}
				else
				{
					if (newLink.Weight == 1 && existingLink.Weight == 1)
						existingLink.Weight = .5f;
					else
						existingLink.Weight = Math.Clamp(existingLink.Weight - .2f, -1, 1);
					if (existingLink.Weight <= 0)
					{
						newSource.RemoveLink(existingLink);
						i--;
					}
				}
			}
		}
	}

	void ClearExtraneousParents(Thought t)
	{
		if (t is null) return;

		bool reconnectNeeded = t.HasAncestor("Thought");
		if (t.Parents.Count > 1)
			t.RemoveParent(ThoughtLabels.GetThought("Unknown"));
		if (reconnectNeeded && !t.HasAncestor("Thought"))
			t.AddParent(ThoughtLabels.GetThought("Unknown"));
	}

	public Thought SubclassExists(Thought t, List<Thought> thoughtAttributes, ref Thought bestMatch, ref List<Thought> missingAttributes)
	{
		if (t is null) return null;

		bestMatch = t;
		missingAttributes = thoughtAttributes;
		if (thoughtAttributes.Count == 0) return t;

		List<Thought> attrs = new(thoughtAttributes);

		List<Thought> existingLinks = t.Descendants.ToList();
		existingLinks.Insert(0, t);
		foreach (Thought r in existingLinks)
		{
			foreach (var attr in attrs)
			{
				if (r.LinksTo.FindFirst(x => x.To == attr) is null) goto NotFound;
			}
			return r;
		NotFound:
			continue;
		}
		return null;
	}

	public Thought CreateInstanceOf(Thought t)
	{
		return CreateSubclass(t, new List<Thought>());
	}

	private Thought CreateSubclass(Thought t, List<Thought> attributes)
	{
		if (t is null) return null;

		string newLabel = t.Label;
		foreach (Thought t1 in attributes)
		{
			newLabel += ((t1.Label.StartsWith(".")) ? "" : ".") + t1.Label;
		}
		Thought retVal = AddThought(newLabel, t);
		foreach (Thought t1 in attributes)
		{
			Link r1 = new() { From = retVal, LinkType = ThoughtLabels.GetThought("is"), To = t1 };
			WriteTheLink(r1);
		}
		return retVal;
	}

	private Thought CheckForInverse(Thought linkType)
	{
		if (linkType is null) return null;
		Thought inverse = linkType.LinksTo.FindFirst(x => x.LinkType?.Label == "inverseOf")?.To;
		return inverse;
	}

	private static List<Thought> FindCommonParents(Thought t, Thought t1)
	{
		List<Thought> commonParents = new();
		foreach (Thought p in t.Parents)
			if (t1.Parents.Contains(p))
				commonParents.Add(p);
		return commonParents;
	}

	public static void WriteTheLink(Link lnk)
	{
		if (lnk.From is null) return;
		if (lnk.LinkType is null) return;
		if (lnk.To is null)
		{
			lock (lnk.From.LinksToWriteable)
			{
				if (!lnk.From.LinksToWriteable.Contains(lnk))
					lnk.From.LinksToWriteable.Add(lnk);
			}
		}
		else
		{
			lock (lnk.From.LinksToWriteable)
			lock (lnk.To.LinksFromWriteable)
			{
				if (!lnk.From.LinksToWriteable.Contains(lnk))
					lnk.From.LinksToWriteable.Add(lnk);
				if (!lnk.To.LinksFromWriteable.Contains(lnk))
					lnk.To.LinksFromWriteable.Add(lnk);
			}
		}
	}
}
