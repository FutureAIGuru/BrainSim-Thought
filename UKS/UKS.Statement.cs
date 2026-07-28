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
	public Link? AddStatement(string sSource, string sLinkType, string sTarget, string label = "")
	{
		Thought? source = ThoughtFromObject(sSource);
		Thought? linkType = ThoughtFromObject(sLinkType, "LinkType", source);
		Thought? target = ThoughtFromObject(sTarget);
		if (source is null || linkType is null) return null;

		return AddStatement(source, linkType, target, label);
	}

	/// <summary>
	/// Adds a statement relating the specified source, link type, and target. No new Thoughts are created.
	/// </summary>
	public Link? AddStatement(Thought source, Thought linkType, Thought? target, string label = "")
	{
		if (source is null || linkType is null) return null;

		Link? wired = GetLink(source, linkType, target);
		Link? labeledLink = string.IsNullOrEmpty(label) ? null : Labeled(label) as Link;
		bool isUnwiredPlaceholder = labeledLink is not null && labeledLink.From is null;

		source.Fire();
		linkType.Fire();
		target?.Fire();

		if (wired is not null && !isUnwiredPlaceholder)
		{
			WeakenConflictingLinks(source, wired);
			wired.Fire();
			return wired;
		}

		Link lnk = isUnwiredPlaceholder ? labeledLink! : CreateTheLink(source, linkType, target);
		if (isUnwiredPlaceholder)
		{
			lnk.From = source;
			lnk.LinkType = linkType;
			lnk.To = target;
		}

		if (lnk.From?.Label == "") lnk.From.AddDefaultLabel();
		if (lnk.To?.Label == "") lnk.To.AddDefaultLabel();
		if (!string.IsNullOrEmpty(label))
			lnk.Label = label.Trim();

		WeakenConflictingLinks(source, lnk);

		WriteTheLink(lnk);
		lnk.Fire();
		ApplyDefaultTimeToLive(lnk);
		if (lnk.LinkType is not null && HasProperty(lnk.LinkType, "isCommutative"))
		{
			Link rReverse = new Link(lnk.To!, lnk.LinkType!, lnk.From!);
			WriteTheLink(rReverse);
		}

		//if this is adding a child link, remove any Unknown parent
		ClearExtraneousParents(lnk.From);
		ClearExtraneousParents(lnk.To);
		ClearExtraneousParents(lnk.LinkType);

		return lnk;
	}

	private Link CreateTheLink(Thought source, Thought linkType, Thought? target)
	{
		Thought? inverseType1 = CheckForInverse(linkType);
		//if this link has an inverse, switcheroo so we are storing consistently in one direction
		if (inverseType1 is not null)
		{
			(source, target) = (target!, source);
			linkType = inverseType1;
		}

		Link r = new()
		{ From = source, LinkType = linkType, To = target };
		return r;
	}

	private void WeakenConflictingLinks(Thought newSource, Link newLink)
	{
		if (newSource is null || newLink is null) return;

		// A link which is itself a result or a condition never conflicts, so the
		// comparison against every existing link can be skipped outright.
		if (newLink.HasProperty("isResult") || newLink.HasProperty("isCondition")) return;

		// Whether the new link's target can take part in an exclusion depends
		// only on that target, so it is settled once here rather than while
		// examining each existing link. Every common parent the comparison can
		// find is a direct parent of this target.
		bool targetMayExclude = false;
		if (newLink.To is not null)
		{
			IReadOnlyList<Thought> targetParents = newLink.To.Parents;
			for (int i = 0; i < targetParents.Count && !targetMayExclude; i++)
				targetMayExclude = HasProperty(targetParents[i], "isexclusive") ||
					HasProperty(targetParents[i], "allowMultiple");
		}

		// Reading LinksTo would copy the whole list and test every link for
		// expiry on each call, which on a Thought holding thousands of links
		// cost far more than the comparison being made. The list is examined in
		// place, and the few links which actually conflict are collected before
		// anything is changed, so that the changes cannot disturb the scan.
		List<Link> sourceLinks = newSource.LinksToWriteable;
		List<Link>? conflicting = null;
		bool alreadyPresent = false;
		for (int i = 0; i < sourceLinks.Count; i++)
		{
			Link existingLink = sourceLinks[i];
			if (existingLink == newLink) alreadyPresent = true;
			else if (LinksAreExclusive(newLink, existingLink, targetMayExclude))
				(conflicting ??= new List<Link>()).Add(existingLink);
		}

		if (alreadyPresent)
		{
			newLink.Weight += (1 - newLink.Weight) / 4.0f;
			newLink.Fire();
		}
		if (conflicting is null) return;

		for (int i = 0; i < conflicting.Count; i++)
		{
			Link existingLink = conflicting[i];
			{
				if (existingLink.LinkType is not null && newLink.LinkType?.Children.Contains(existingLink.LinkType) == true && HasAttribute(existingLink.LinkType, "not"))
				{
					existingLink.From?.RemoveLink(existingLink);
				}
				else if (newLink.LinkType is not null && existingLink.LinkType?.Children.Contains(newLink.LinkType) == true && HasAttribute(newLink.LinkType, "not"))
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
						newSource.RemoveLink(existingLink);
				}
			}
		}
	}

	void ClearExtraneousParents(Thought? t)
	{
		if (t is null) return;

		bool reconnectNeeded = t.HasAncestor("Thought");
		Thought? unknown = ThoughtLabels.GetThought("Unknown");
		if (t.Parents.Count > 1 && unknown is not null)
			t.RemoveParent(unknown);
		if (reconnectNeeded && !t.HasAncestor("Thought") && unknown is not null)
			t.AddParent(unknown);
	}

	public Thought? SubclassExists(Thought t, List<Thought> thoughtAttributes, ref Thought? bestMatch, ref List<Thought> missingAttributes)
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

	public Thought? CreateInstanceOf(Thought t)
	{
		return CreateSubclass(t, new List<Thought>());
	}

	private Thought? CreateSubclass(Thought t, List<Thought> attributes)
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

	private Thought? CheckForInverse(Thought linkType)
	{
		if (linkType is null) return null;
		return linkType.LinksTo.FindFirst(x => x.LinkType?.Label == "inverseOf")?.To;
	}

	private static List<Thought> FindCommonParents(Thought t, Thought t1)
	{
		List<Thought> commonParents = new();
		foreach (Thought p in t.Parents)
			if (t1.Parents.Contains(p))
				commonParents.Add(p);
		return commonParents;
	}

	/// <summary>Ch.5 stable vs ephemeral: short TTL for link types marked isEphemeral.</summary>
	private void ApplyDefaultTimeToLive(Link lnk)
	{
		Thought? ephemeral = Labeled("isEphemeral");
		if (ephemeral is not null && lnk.LinkType?.HasProperty(ephemeral) == true)
			lnk.TimeToLive = TimeSpan.FromSeconds(30);
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
