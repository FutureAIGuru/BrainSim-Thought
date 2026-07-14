#!/usr/bin/env python3
"""Generate BrainSim-Thought UKS project XML from frozen NewDocument bootstrap.

Phases (see Work-Log/2026-07-14-research-loop-nature-animals-generator-phased-plan.md):

  0–1  --domain none          Golden bootstrap + dog under Unknown + Fido is-a dog
  2–3  --domain animals_core  Hierarchy + Ch.5 Fido/Tripper only
  4    --domain animals       Full 5yo multi-species Nature & Animals (default)

Always append-only after bootstrap (never mid-list delete — UKSTemp list-index safety).
Git policy: never commit/push BrainSim-Thought from the Grok tower.
"""

from __future__ import annotations

import argparse
import re
import uuid
import xml.etree.ElementTree as ET
from pathlib import Path

TOOLS_DIR = Path(__file__).resolve().parent
REPO_ROOT = TOOLS_DIR.parent
DOMAINS_DIR = TOOLS_DIR / "domains"
DEFAULT_BASE = TOOLS_DIR / "fixtures" / "NewDocument-golden.xml"
DEFAULT_OUTPUT = REPO_ROOT / "BrainSimulator" / "UKSContent" / "NatureAnimals.xml"
DEFAULT_WORLD_OUTPUT = REPO_ROOT / "BrainSimulator" / "UKSContent" / "NatureWorld.xml"

# Built-in domain tokens (code); file domains live under tools/domains/*.yaml
BUILTIN_DOMAINS = frozenset({"none", "animals_core", "animals"})

XML_HEADER = (
    '<?xml version="1.0" encoding="utf-8"?>\n'
    '<ArrayOfSThought xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" '
    'xmlns:xsd="http://www.w3.org/2001/XMLSchema">\n'
)

# Multi-species markers — must stay absent in Phase 2–3 only.
PHASE23_FORBIDDEN = (
    "cat",
    "horse",
    "whale",
    "bird",
    "eagle",
    "snake",
    "predatorOf",
    "livesIn",
    "Whiskers",
    "nature",
    "plant",
)

SPECIES_CLASS: list[tuple[str, str]] = [
    ("dog", "mammal"),
    ("cat", "mammal"),
    ("horse", "mammal"),
    ("whale", "mammal"),
    ("bat", "mammal"),
    ("mouse", "mammal"),
    ("wolf", "mammal"),
    ("deer", "mammal"),
    ("dolphin", "mammal"),
    ("eagle", "bird"),
    ("sparrow", "bird"),
    ("penguin", "bird"),
    ("owl", "bird"),
    ("snake", "reptile"),
    ("turtle", "reptile"),
    ("crocodile", "reptile"),
    ("salmon", "fish"),
    ("shark", "fish"),
    ("trout", "fish"),
    ("frog", "amphibian"),
    ("bee", "insect"),
    ("ant", "insect"),
]


def unl() -> str:
    return f"unl_{uuid.uuid4().hex[:8]}"


def _local(tag: str) -> str:
    if tag.startswith("{"):
        return tag.rsplit("}", 1)[-1]
    return tag


def _child(el: ET.Element, name: str) -> ET.Element | None:
    for c in el:
        if _local(c.tag) == name:
            return c
    return None


def _children(el: ET.Element, name: str) -> list[ET.Element]:
    return [c for c in el if _local(c.tag) == name]


class SThought:
    """One ArrayOfSThought entry (pure node or link)."""

    __slots__ = ("index", "label", "source", "link_type", "target", "weight", "v_values")

    def __init__(
        self,
        index: int,
        label: str,
        source: int | None = None,
        link_type: int | None = None,
        target: int | None = None,
        weight: str | None = None,
        v_values: list[tuple[str | None, str]] | None = None,
    ):
        self.index = index
        self.label = label
        self.source = source
        self.link_type = link_type
        self.target = target
        self.weight = weight
        self.v_values = v_values or []

    @property
    def is_link(self) -> bool:
        return self.source is not None


def parse_array(path: Path) -> list[SThought]:
    tree = ET.parse(path)
    root = tree.getroot()
    if _local(root.tag) != "ArrayOfSThought":
        raise ValueError(f"Expected ArrayOfSThought root, got {root.tag}")

    items: list[SThought] = []
    for el in root:
        if _local(el.tag) != "sThought":
            continue
        idx_el = _child(el, "index")
        lab_el = _child(el, "label")
        if idx_el is None or lab_el is None or idx_el.text is None or lab_el.text is None:
            raise ValueError("sThought missing index/label")
        index = int(idx_el.text)
        label = lab_el.text
        src_el = _child(el, "source")
        if src_el is not None and src_el.text is not None:
            lt_el = _child(el, "linkType")
            tg_el = _child(el, "target")
            if lt_el is None or tg_el is None or lt_el.text is None or tg_el.text is None:
                raise ValueError(f"link sThought {label!r} missing link fields")
            w_el = _child(el, "weight")
            weight = w_el.text if w_el is not None else None
            items.append(
                SThought(
                    index=index,
                    label=label,
                    source=int(src_el.text),
                    link_type=int(lt_el.text),
                    target=int(tg_el.text),
                    weight=weight,
                )
            )
        else:
            v_values: list[tuple[str | None, str]] = []
            for v in _children(el, "V"):
                xsi_type = None
                for k, val in v.attrib.items():
                    if k.endswith("type") or _local(k) == "type":
                        xsi_type = val
                        break
                v_values.append((xsi_type, v.text if v.text is not None else ""))
            items.append(SThought(index=index, label=label, v_values=v_values))
    return items


def pure_label_map(items: list[SThought]) -> dict[str, int]:
    out: dict[str, int] = {}
    for it in items:
        if not it.is_link and it.label not in out:
            out[it.label] = it.index
    return out


def index_to_label(items: list[SThought]) -> dict[int, str]:
    out: dict[int, str] = {}
    for it in items:
        if not it.is_link:
            out[it.index] = it.label
    return out


def max_index(items: list[SThought]) -> int:
    return max((it.index for it in items), default=-1)


def has_triple(
    items: list[SThought],
    src_label: str,
    link_label: str,
    tgt_label: str,
) -> bool:
    labels = index_to_label(items)
    pure = pure_label_map(items)
    if src_label not in pure or link_label not in pure or tgt_label not in pure:
        return False
    src_i, lt_i, tgt_i = pure[src_label], pure[link_label], pure[tgt_label]
    for it in items:
        if not it.is_link:
            continue
        if it.source == src_i and it.link_type == lt_i and it.target == tgt_i:
            return True
        if (
            labels.get(it.source) == src_label
            and labels.get(it.link_type) == link_label
            and labels.get(it.target) == tgt_label
        ):
            return True
    return False


def has_is_a(items: list[SThought], src_label: str, tgt_label: str) -> bool:
    return has_triple(items, src_label, "is-a", tgt_label)


def ensure_pure_node(items: list[SThought], label: str) -> int:
    pure = pure_label_map(items)
    if label in pure:
        return pure[label]
    # Append-only: pure index field MUST equal list position (UKS load contract).
    idx = len(items)
    items.append(SThought(index=idx, label=label))
    return idx


def ensure_domain_link_type(
    items: list[SThought],
    label: str,
    *,
    also_comparison: bool = False,
) -> None:
    ensure_pure_node(items, label)
    ensure_is_a(items, label, "LinkType")
    if also_comparison and "Comparison" in pure_label_map(items):
        ensure_is_a(items, label, "Comparison")


def ensure_has_variant(items: list[SThought], has_label: str, number_label: str) -> None:
    ensure_pure_node(items, has_label)
    ensure_is_a(items, has_label, "has")
    if number_label not in pure_label_map(items):
        ensure_pure_node(items, number_label)
        if "number" in pure_label_map(items):
            ensure_is_a(items, number_label, "number")
    ensure_link(items, has_label, "is", number_label)


def ensure_link(
    items: list[SThought],
    src_label: str,
    link_label: str,
    tgt_label: str,
    weight: str | None = None,
) -> None:
    if has_triple(items, src_label, link_label, tgt_label):
        return
    pure = pure_label_map(items)
    if link_label not in pure:
        ensure_domain_link_type(items, link_label)
        pure = pure_label_map(items)
    src_i = ensure_pure_node(items, src_label)
    tgt_i = ensure_pure_node(items, tgt_label)
    pure = pure_label_map(items)
    lt_i = pure[link_label]
    items.append(
        SThought(
            index=src_i,
            label=unl(),
            source=src_i,
            link_type=lt_i,
            target=tgt_i,
            weight=weight,
        )
    )


def ensure_is_a(
    items: list[SThought],
    src_label: str,
    tgt_label: str,
    weight: str | None = None,
) -> None:
    if tgt_label == "Unknown":
        pure = pure_label_map(items)
        if "Unknown" not in pure:
            raise RuntimeError("bootstrap missing Unknown (not a golden Initialize export?)")
    ensure_link(items, src_label, "is-a", tgt_label, weight=weight)


def validate_list_index_integrity(items: list[SThought]) -> list[str]:
    """UKS load resolves source/linkType/target as *list positions* into UKSTemp."""
    errs: list[str] = []
    for i, it in enumerate(items):
        if not it.is_link and it.index != i:
            errs.append(
                f"pure label {it.label!r} at list pos {i} has index field {it.index}"
            )
            if len(errs) >= 8:
                break
    for i, it in enumerate(items):
        if not it.is_link:
            continue
        for name, ref in (
            ("source", it.source),
            ("linkType", it.link_type),
            ("target", it.target),
        ):
            if ref is None or ref < 0 or ref >= len(items):
                errs.append(f"link at pos {i}: {name}={ref} out of range")
                continue
            if items[ref].is_link:
                errs.append(
                    f"link at pos {i}: {name}={ref} points at link ({items[ref].label!r})"
                )
        if len(errs) >= 20:
            errs.append("… further integrity errors suppressed")
            break
    return errs


def ensure_fido_phase1(items: list[SThought]) -> None:
    pure = pure_label_map(items)
    for required in ("Thought", "Unknown", "is-a", "BrainSim"):
        if required not in pure:
            raise RuntimeError(
                f"bootstrap missing required label {required!r}; "
                f"use tools/fixtures/NewDocument-golden.xml"
            )
    ensure_pure_node(items, "dog")
    ensure_pure_node(items, "Fido")
    ensure_is_a(items, "dog", "Unknown")
    ensure_is_a(items, "Fido", "dog", weight="0.9")


def apply_animals_core(items: list[SThought]) -> None:
    """Phase 2–3: hierarchy + Ch.5 inheritance/exception (append-only)."""
    pure = pure_label_map(items)
    for required in ("Object", "has", "is", "is-a", "Unknown", "brown", "4", "3"):
        if required not in pure:
            raise RuntimeError(
                f"bootstrap missing {required!r} needed for animals_core"
            )

    ensure_pure_node(items, "livingThing")
    ensure_pure_node(items, "animal")
    ensure_pure_node(items, "mammal")
    ensure_pure_node(items, "dog")
    ensure_pure_node(items, "Fido")

    ensure_is_a(items, "livingThing", "Object")
    ensure_is_a(items, "animal", "livingThing")
    ensure_is_a(items, "mammal", "animal")
    ensure_is_a(items, "dog", "mammal")
    # Keep dog→Unknown if present (append-only; multi-parent OK)
    ensure_is_a(items, "Fido", "dog", weight="0.9")

    ensure_pure_node(items, "bodyPart")
    ensure_pure_node(items, "fur")
    ensure_pure_node(items, "leg")
    ensure_pure_node(items, "has.4")
    ensure_pure_node(items, "has.3")
    ensure_pure_node(items, "Tripper")

    ensure_is_a(items, "bodyPart", "Object")
    ensure_is_a(items, "fur", "bodyPart")
    ensure_is_a(items, "leg", "bodyPart")
    ensure_is_a(items, "has.4", "has")
    ensure_is_a(items, "has.3", "has")
    ensure_link(items, "has.4", "is", "4")
    ensure_link(items, "has.3", "is", "3")

    ensure_link(items, "dog", "has", "fur")
    ensure_link(items, "dog", "has.4", "leg")
    ensure_link(items, "Fido", "is", "brown")
    ensure_is_a(items, "Tripper", "dog")
    ensure_link(items, "Tripper", "has.3", "leg")


def apply_animals_full(items: list[SThought]) -> None:
    """Phase 4: full 5yo Nature & Animals pack on top of animals_core."""
    apply_animals_core(items)

    pure = pure_label_map(items)
    for required in ("Object", "has", "is", "can", "is-a", "Action", "LinkType"):
        if required not in pure:
            raise RuntimeError(f"bootstrap missing {required!r} for animals full pack")

    for lt, as_cmp in (
        ("differsFrom", True),
        ("predatorOf", False),
        ("preyOf", False),
        ("livesIn", False),
        ("eats", False),
    ):
        ensure_domain_link_type(items, lt, also_comparison=as_cmp)
    if "isSimilarTo" in pure_label_map(items):
        ensure_is_a(items, "isSimilarTo", "LinkType")

    for has_lab, num in (
        ("has.2", "2"),
        ("has.4", "4"),
        ("has.3", "3"),
        ("has.6", "6"),
        ("has.8", "8"),
        ("has.no", "no"),
        ("has.many", "many"),
    ):
        ensure_has_variant(items, has_lab, num)

    ensure_pure_node(items, "nature")
    ensure_is_a(items, "nature", "Thought")
    ensure_is_a(items, "livingThing", "nature")
    ensure_pure_node(items, "plant")
    ensure_pure_node(items, "tree")
    ensure_pure_node(items, "ecosystem")
    ensure_is_a(items, "plant", "livingThing")
    ensure_is_a(items, "tree", "plant")
    ensure_is_a(items, "ecosystem", "Object")

    for cls in ("bird", "reptile", "fish", "amphibian", "insect"):
        ensure_pure_node(items, cls)
        ensure_is_a(items, cls, "animal")

    for species, cls in SPECIES_CLASS:
        ensure_pure_node(items, species)
        ensure_is_a(items, species, cls)

    ensure_is_a(items, "bodyPart", "Object")
    for part in (
        "leg", "tail", "fur", "feather", "scale", "fin", "wing", "gill", "beak",
    ):
        ensure_pure_node(items, part)
        ensure_is_a(items, part, "bodyPart")

    ensure_pure_node(items, "sound")
    ensure_is_a(items, "sound", "Object")
    for snd in ("bark(dog)", "meow(cat)", "neigh(horse)", "buzz(bee)"):
        ensure_pure_node(items, snd)
        ensure_is_a(items, snd, "sound")

    ensure_pure_node(items, "habitat")
    ensure_is_a(items, "habitat", "Object")
    for h in ("forest", "ocean", "sky", "grassland", "antarctica", "river"):
        ensure_pure_node(items, h)
        ensure_is_a(items, h, "habitat")

    for c in ("brown", "black", "golden", "green", "gray"):
        ensure_pure_node(items, c)
        if "color" in pure_label_map(items):
            ensure_is_a(items, c, "color")

    ensure_pure_node(items, "vertebrate")
    ensure_is_a(items, "vertebrate", "Object")
    for cls in ("mammal", "bird", "reptile", "fish", "amphibian"):
        ensure_is_a(items, cls, "vertebrate")

    for prop in ("warmBlooded", "coldBlooded", "givesLiveBirth"):
        ensure_pure_node(items, prop)
        ensure_is_a(items, prop, "Object")

    ensure_link(items, "mammal", "has", "warmBlooded")
    ensure_link(items, "mammal", "has", "givesLiveBirth")
    ensure_link(items, "bird", "has", "warmBlooded")
    ensure_link(items, "reptile", "has", "coldBlooded")
    ensure_link(items, "fish", "has", "coldBlooded")
    ensure_link(items, "amphibian", "has", "coldBlooded")
    ensure_link(items, "insect", "has", "coldBlooded")

    ensure_pure_node(items, "lays")
    ensure_pure_node(items, "egg")
    ensure_is_a(items, "lays", "Action")
    ensure_domain_link_type(items, "lays")
    ensure_is_a(items, "egg", "bodyPart")
    for cls in ("bird", "reptile", "fish", "amphibian"):
        ensure_link(items, cls, "lays", "egg")

    ensure_link(items, "dog", "has", "tail")
    ensure_link(items, "dog", "has.4", "leg")
    ensure_link(items, "dog", "has", "fur")
    ensure_link(items, "dog", "can", "bark(dog)")

    ensure_link(items, "cat", "has", "tail")
    ensure_link(items, "cat", "has.4", "leg")
    ensure_link(items, "cat", "has", "fur")
    ensure_link(items, "cat", "can", "meow(cat)")

    ensure_link(items, "horse", "has", "tail")
    ensure_link(items, "horse", "has.4", "leg")
    ensure_link(items, "horse", "can", "neigh(horse)")

    ensure_link(items, "whale", "has", "fin")
    ensure_link(items, "whale", "has.no", "leg")
    ensure_link(items, "bat", "has", "wing")
    ensure_link(items, "bat", "has.4", "leg")
    ensure_link(items, "mouse", "has.4", "leg")
    ensure_link(items, "mouse", "has", "tail")
    ensure_link(items, "wolf", "has", "tail")
    ensure_link(items, "wolf", "has.4", "leg")
    ensure_link(items, "wolf", "has", "fur")
    ensure_link(items, "deer", "has.4", "leg")
    ensure_link(items, "deer", "has", "tail")
    ensure_link(items, "dolphin", "has", "fin")
    ensure_link(items, "dolphin", "has.no", "leg")

    ensure_link(items, "bird", "has", "wing")
    ensure_link(items, "bird", "has.many", "feather")
    ensure_link(items, "bird", "has.2", "leg")
    ensure_link(items, "bird", "has", "beak")
    for b in ("eagle", "sparrow", "penguin", "owl"):
        ensure_link(items, b, "has.2", "wing")

    ensure_link(items, "reptile", "has", "scale")
    ensure_link(items, "snake", "has.no", "leg")
    ensure_link(items, "snake", "has", "scale")
    ensure_link(items, "turtle", "has.4", "leg")
    ensure_link(items, "turtle", "has", "scale")
    ensure_link(items, "crocodile", "has.4", "leg")
    ensure_link(items, "crocodile", "has", "scale")

    ensure_link(items, "fish", "has", "fin")
    ensure_link(items, "fish", "has", "gill")
    ensure_link(items, "fish", "has", "scale")
    ensure_link(items, "salmon", "has", "fin")
    ensure_link(items, "shark", "has", "fin")
    ensure_link(items, "shark", "has.no", "leg")

    ensure_link(items, "frog", "has.4", "leg")
    ensure_link(items, "bee", "has", "wing")
    ensure_link(items, "bee", "has.6", "leg")
    ensure_link(items, "bee", "can", "buzz(bee)")
    ensure_link(items, "ant", "has.6", "leg")

    for lab in ("pet", "wild"):
        ensure_pure_node(items, lab)
        ensure_is_a(items, lab, "Object")
    ensure_is_a(items, "dog", "pet")
    ensure_is_a(items, "cat", "pet")
    ensure_is_a(items, "horse", "pet")
    ensure_is_a(items, "wolf", "wild")
    ensure_is_a(items, "eagle", "wild")
    ensure_is_a(items, "deer", "wild")

    ensure_link(items, "dog", "isSimilarTo", "wolf")
    ensure_link(items, "dog", "isSimilarTo", "cat")
    ensure_link(items, "cat", "isSimilarTo", "dog")
    ensure_link(items, "wolf", "isSimilarTo", "dog")
    ensure_link(items, "salmon", "isSimilarTo", "trout")
    ensure_link(items, "eagle", "isSimilarTo", "owl")
    ensure_link(items, "dog", "differsFrom", "bird")
    ensure_link(items, "bird", "differsFrom", "fish")
    ensure_link(items, "mammal", "differsFrom", "reptile")
    ensure_link(items, "whale", "differsFrom", "fish")
    ensure_link(items, "bat", "differsFrom", "bird")
    ensure_link(items, "snake", "differsFrom", "dog")

    ensure_link(items, "eagle", "predatorOf", "mouse")
    ensure_link(items, "mouse", "preyOf", "eagle")
    ensure_link(items, "wolf", "predatorOf", "deer")
    ensure_link(items, "deer", "preyOf", "wolf")
    ensure_link(items, "shark", "predatorOf", "salmon")
    ensure_link(items, "salmon", "preyOf", "shark")
    ensure_link(items, "owl", "predatorOf", "mouse")
    ensure_link(items, "cat", "predatorOf", "mouse")

    ensure_link(items, "deer", "livesIn", "forest")
    ensure_link(items, "eagle", "livesIn", "sky")
    ensure_link(items, "salmon", "livesIn", "river")
    ensure_link(items, "whale", "livesIn", "ocean")
    ensure_link(items, "shark", "livesIn", "ocean")
    ensure_link(items, "dolphin", "livesIn", "ocean")
    ensure_link(items, "penguin", "livesIn", "antarctica")
    ensure_link(items, "sparrow", "livesIn", "forest")
    ensure_link(items, "frog", "livesIn", "river")
    ensure_link(items, "tree", "livesIn", "forest")

    ensure_link(items, "dog", "eats", "salmon")
    ensure_link(items, "cat", "eats", "mouse")
    ensure_link(items, "eagle", "eats", "mouse")
    ensure_link(items, "wolf", "eats", "deer")
    ensure_link(items, "shark", "eats", "salmon")

    ensure_link(items, "Fido", "livesIn", "grassland")

    for label, species, extra in (
        ("Whiskers", "cat", (("is", "black"), ("livesIn", "grassland"))),
        ("Shadow", "horse", (("is", "brown"), ("livesIn", "grassland"))),
        ("Buddy", "dog", (("is", "golden"),)),
        ("Talon", "eagle", (("livesIn", "sky"),)),
        ("Slither", "snake", (("is", "green"), ("livesIn", "forest"))),
        ("Echo", "bat", (("livesIn", "forest"),)),
        ("Nemo", "salmon", (("livesIn", "river"),)),
        ("Penny", "penguin", (("livesIn", "antarctica"),)),
        ("Bramble", "deer", (("is", "brown"), ("livesIn", "forest"))),
    ):
        ensure_pure_node(items, label)
        ensure_is_a(items, label, species)
        for lt, tgt in extra:
            ensure_link(items, label, lt, tgt)


def escape_xml(text: str) -> str:
    return (
        text.replace("&", "&amp;")
        .replace("<", "&lt;")
        .replace(">", "&gt;")
        .replace('"', "&quot;")
    )


def render_xml(items: list[SThought]) -> str:
    lines = [XML_HEADER.rstrip("\n")]
    for it in items:
        lines.append("  <sThought>")
        lines.append(f"    <index>{it.index}</index>")
        lines.append(f"    <label>{escape_xml(it.label)}</label>")
        if it.v_values:
            for xsi_type, text in it.v_values:
                if xsi_type:
                    lines.append(
                        f'    <V xsi:type="{escape_xml(xsi_type)}">{escape_xml(text)}</V>'
                    )
                else:
                    lines.append(f"    <V>{escape_xml(text)}</V>")
        if it.is_link:
            assert it.source is not None and it.link_type is not None and it.target is not None
            lines.append(f"    <source>{it.source}</source>")
            lines.append(f"    <linkType>{it.link_type}</linkType>")
            lines.append(f"    <target>{it.target}</target>")
            if it.weight is not None:
                lines.append(f"    <weight>{escape_xml(it.weight)}</weight>")
        lines.append("  </sThought>")
    lines.append("</ArrayOfSThought>")
    return "\r\n".join(lines) + "\r\n"


def validate_phase1(items: list[SThought]) -> list[str]:
    errs: list[str] = []
    pure = pure_label_map(items)
    for lab in ("BrainSim", "Unknown", "Thought", "is-a", "dog", "Fido"):
        if lab not in pure:
            errs.append(f"missing pure label: {lab}")
    if not has_is_a(items, "dog", "Unknown"):
        errs.append("missing link dog --is-a--> Unknown")
    if not has_is_a(items, "Fido", "dog"):
        errs.append("missing link Fido --is-a--> dog")
    for m in ("mammal", "livingThing", "Tripper", "has.4"):
        if m in pure:
            errs.append(f"unexpected Phase-1 nature label present: {m}")
    return errs


def validate_phase23(items: list[SThought]) -> list[str]:
    errs: list[str] = []
    pure = pure_label_map(items)
    for lab in (
        "BrainSim", "Unknown", "Thought", "Object", "livingThing", "animal",
        "mammal", "dog", "Fido", "Tripper", "fur", "leg", "bodyPart",
        "has.4", "has.3", "brown", "has", "is",
    ):
        if lab not in pure:
            errs.append(f"missing pure label: {lab}")
    for src, tgt in (
        ("livingThing", "Object"),
        ("animal", "livingThing"),
        ("mammal", "animal"),
        ("dog", "mammal"),
        ("Fido", "dog"),
        ("Tripper", "dog"),
        ("bodyPart", "Object"),
        ("fur", "bodyPart"),
        ("leg", "bodyPart"),
        ("has.4", "has"),
        ("has.3", "has"),
    ):
        if not has_is_a(items, src, tgt):
            errs.append(f"missing is-a: {src} → {tgt}")
    if not has_triple(items, "dog", "has", "fur"):
        errs.append("missing dog --has--> fur")
    if not has_triple(items, "dog", "has.4", "leg"):
        errs.append("missing dog --has.4--> leg")
    if not has_triple(items, "Fido", "is", "brown"):
        errs.append("missing Fido --is--> brown")
    if not has_triple(items, "Tripper", "has.3", "leg"):
        errs.append("missing Tripper --has.3--> leg")
    if has_triple(items, "Fido", "has", "fur"):
        errs.append("Fido has local has→fur (should inherit)")
    if has_triple(items, "Fido", "has.4", "leg"):
        errs.append("Fido has local has.4→leg (should inherit)")
    if has_triple(items, "Tripper", "has.4", "leg"):
        errs.append("Tripper has has.4→leg")
    for m in PHASE23_FORBIDDEN:
        if m in pure:
            errs.append(f"unexpected multi-species label in Phase 2–3: {m}")
    if "Unknown" not in pure:
        errs.append("Unknown missing")
    return errs


def validate_phase4(items: list[SThought]) -> list[str]:
    """Full multi-species pack + Gate 4 smoke triples."""
    errs = validate_phase23(items)
    # Strip phase23 "forbidden multi-species" errors — those are expected in phase 4
    errs = [e for e in errs if "unexpected multi-species" not in e]

    pure = pure_label_map(items)
    for lab in (
        "cat", "horse", "whale", "bird", "eagle", "snake", "salmon", "shark",
        "mouse", "wolf", "deer", "penguin", "nature", "plant", "tree",
        "has.no", "has.many", "has.2", "has.6", "feather", "predatorOf",
        "livesIn", "eats", "Whiskers", "Talon", "Nemo", "Bramble",
    ):
        if lab not in pure:
            errs.append(f"missing Phase-4 label: {lab}")

    checks = [
        ("eagle", "is-a", "bird"),
        ("salmon", "is-a", "fish"),
        ("dog", "isSimilarTo", "wolf"),
        ("eagle", "predatorOf", "mouse"),
        ("whale", "differsFrom", "fish"),
        ("penguin", "livesIn", "antarctica"),
        ("bird", "has.many", "feather"),
        ("bird", "lays", "egg"),
        ("whale", "has.no", "leg"),
        ("snake", "has.no", "leg"),
        ("eagle", "livesIn", "sky"),
        ("shark", "eats", "salmon"),
        ("shark", "predatorOf", "salmon"),
        ("Fido", "is-a", "dog"),
        ("Fido", "is", "brown"),
        ("Tripper", "has.3", "leg"),
        ("dog", "has.4", "leg"),
        ("dog", "has", "fur"),
        ("nature", "is-a", "Thought"),
        ("livingThing", "is-a", "nature"),
        ("tree", "is-a", "plant"),
    ]
    for s, lt, t in checks:
        if not has_triple(items, s, lt, t):
            errs.append(f"missing Phase-4 triple: {s} --{lt}--> {t}")

    # Ch.5 still: no local inherited anatomy on Fido
    if has_triple(items, "Fido", "has", "fur"):
        errs.append("Fido has local has→fur")
    if has_triple(items, "Fido", "has.4", "leg"):
        errs.append("Fido has local has.4→leg")

    return errs


def summarize(items: list[SThought]) -> dict[str, int]:
    pure_n = sum(1 for it in items if not it.is_link)
    link_n = sum(1 for it in items if it.is_link)
    return {
        "pure_nodes": pure_n,
        "links": link_n,
        "total_sThought": pure_n + link_n,
    }


# Bootstrap sequence nodes use NXT/FRST/VLU, not is-a (Initialize export).
_SEQ_LABEL = re.compile(r"^(alphabet|digit|pi)-seq\d+$")

# Labels allowed without is-a parent (Simon root + app module root + Initialize seqs).
PARENT_AUDIT_ALLOW = frozenset({"Thought", "BrainSim"})

# Domain labels that must never use hasProperty (meta-only).
DOMAIN_NO_HASPROPERTY = frozenset(
    {
        "dog", "cat", "Fido", "Tripper", "mammal", "animal", "bird", "eagle",
        "whale", "snake", "fur", "leg", "feather", "Whiskers", "Talon", "Nemo",
        "Bramble", "shark", "salmon", "wolf", "mouse", "deer", "penguin",
    }
)


def validate_parent_audit(items: list[SThought], *, strict: bool = False) -> list[str]:
    """Every pure label (except allowlist / Initialize seqs) has ≥1 is-a parent."""
    errs: list[str] = []
    pure = pure_label_map(items)
    labels = index_to_label(items)
    isa = pure.get("is-a")
    if isa is None:
        return ["missing is-a for parent audit"]

    has_parent: set[str] = set()
    for it in items:
        if not it.is_link or it.link_type != isa:
            continue
        src = labels.get(it.source)
        if src:
            has_parent.add(src)

    orphans: list[str] = []
    for lab in pure:
        if lab in PARENT_AUDIT_ALLOW:
            continue
        if not strict and _SEQ_LABEL.match(lab):
            continue
        if lab not in has_parent:
            orphans.append(lab)

    if orphans:
        sample = ", ".join(sorted(orphans)[:25])
        more = f" (+{len(orphans) - 25} more)" if len(orphans) > 25 else ""
        errs.append(
            f"{len(orphans)} pure label(s) without is-a parent: {sample}{more}"
        )
    return errs


def validate_no_domain_hasproperty(items: list[SThought]) -> list[str]:
    """hasProperty is for link-rule meta only — not domain anatomy/species facts."""
    errs: list[str] = []
    pure = pure_label_map(items)
    labels = index_to_label(items)
    hp = pure.get("hasProperty")
    if hp is None:
        return errs
    for it in items:
        if not it.is_link or it.link_type != hp:
            continue
        src = labels.get(it.source, "")
        if src in DOMAIN_NO_HASPROPERTY:
            tgt = labels.get(it.target, "?")
            errs.append(f"domain hasProperty misuse: {src} --hasProperty--> {tgt}")
    return errs


def validate_phase5(items: list[SThought], *, strict_parents: bool = False) -> list[str]:
    """Phase 5 hardening: integrity + parents + hasProperty + full animals content."""
    errs: list[str] = []
    errs.extend(validate_list_index_integrity(items))
    errs.extend(validate_parent_audit(items, strict=strict_parents))
    errs.extend(validate_no_domain_hasproperty(items))
    # Full pack content (Phase 4)
    errs.extend(validate_phase4(items))
    return errs


def validate_existing_file(path: Path, *, domain: str = "animals") -> list[str]:
    """Validate a written XML without regenerating."""
    items = parse_array(path)
    errs = validate_list_index_integrity(items)
    if domain == "none":
        errs.extend(validate_phase1(items))
    elif domain == "animals_core":
        errs.extend(validate_phase23(items))
    else:
        errs.extend(validate_phase5(items))
    return errs


def parse_domain_list(spec: str) -> list[str]:
    """Split --domain animals,plants,inanimates into ordered tokens."""
    parts = [p.strip().lower() for p in (spec or "").split(",") if p.strip()]
    if not parts:
        return ["animals"]
    return parts


def list_yaml_domains() -> list[str]:
    if not DOMAINS_DIR.is_dir():
        return []
    return sorted(p.stem for p in DOMAINS_DIR.glob("*.yaml"))


def load_yaml_domain(path: Path) -> dict:
    import yaml  # PyYAML

    data = yaml.safe_load(path.read_text(encoding="utf-8"))
    if not isinstance(data, dict):
        raise ValueError(f"Domain file must be a mapping: {path}")
    return data


def apply_yaml_domain(items: list[SThought], path: Path) -> None:
    """Apply a data-only domain pack (append-only). See tools/domains/README.md."""
    data = load_yaml_domain(path)

    for node in data.get("nodes") or []:
        if not isinstance(node, dict) or "label" not in node:
            raise ValueError(f"{path}: node entries need label: {node!r}")
        lab = str(node["label"])
        ensure_pure_node(items, lab)
        parents = node.get("is_a") or node.get("is-a") or []
        if isinstance(parents, str):
            parents = [parents]
        for parent in parents:
            parent = str(parent)
            # Parent should already exist (bootstrap / earlier domain); create only if missing
            # so typos surface as parent-audit issues rather than silent orphans of orphans.
            if parent not in pure_label_map(items):
                ensure_pure_node(items, parent)
            ensure_is_a(items, lab, parent)

    for triple in data.get("links") or []:
        if not isinstance(triple, (list, tuple)) or len(triple) != 3:
            raise ValueError(f"{path}: links must be [src, type, tgt]: {triple!r}")
        src, lt, tgt = (str(triple[0]), str(triple[1]), str(triple[2]))
        if lt == "hasProperty":
            raise ValueError(f"{path}: hasProperty forbidden in domain packs ({src})")
        ensure_link(items, src, lt, tgt)

    for inst in data.get("instances") or []:
        if not isinstance(inst, dict) or "label" not in inst:
            raise ValueError(f"{path}: instances need label: {inst!r}")
        lab = str(inst["label"])
        ensure_pure_node(items, lab)
        parents = inst.get("is_a") or inst.get("is-a") or []
        if isinstance(parents, str):
            parents = [parents]
        for parent in parents:
            parent = str(parent)
            if parent not in pure_label_map(items):
                ensure_pure_node(items, parent)
            ensure_is_a(items, lab, parent)
        for triple in inst.get("links") or []:
            if not isinstance(triple, (list, tuple)) or len(triple) != 3:
                raise ValueError(f"{path}: instance links must be [src, type, tgt]: {triple!r}")
            src, lt, tgt = (str(triple[0]), str(triple[1]), str(triple[2]))
            if lt == "hasProperty":
                raise ValueError(f"{path}: hasProperty forbidden ({src})")
            ensure_link(items, src, lt, tgt)


def validate_yaml_domain_content(items: list[SThought], domain_id: str) -> list[str]:
    """Smoke triples for built-in YAML packs (plants / inanimates)."""
    errs: list[str] = []
    pure = pure_label_map(items)
    if domain_id == "plants":
        for lab in ("plant", "tree", "flower", "oak", "rose", "GrandmaOak", "leaf", "grow"):
            if lab not in pure:
                errs.append(f"plants pack missing label: {lab}")
        for s, lt, t in (
            ("tree", "is-a", "plant"),
            ("oak", "is-a", "tree"),
            ("tree", "has", "leaf"),
            ("flower", "has", "petal"),
            ("GrandmaOak", "is-a", "oak"),
            ("GrandmaOak", "livesIn", "forest"),
            ("plant", "is-a", "livingThing"),
        ):
            if not has_triple(items, s, lt, t):
                errs.append(f"plants pack missing: {s} --{lt}--> {t}")
    elif domain_id == "inanimates":
        for lab in ("thing", "rock", "sun", "car", "house", "MyHouse", "RedCar", "wheel"):
            if lab not in pure:
                errs.append(f"inanimates pack missing label: {lab}")
        for s, lt, t in (
            ("thing", "is-a", "Object"),
            ("rock", "is-a", "thing"),
            ("sun", "is", "hot"),
            ("car", "has", "wheel"),
            ("house", "has", "door"),
            ("MyHouse", "is-a", "house"),
            ("RedCar", "is", "red"),
        ):
            if not has_triple(items, s, lt, t):
                errs.append(f"inanimates pack missing: {s} --{lt}--> {t}")
    return errs


def build_domain_stack(domain_spec: str, base_path: Path) -> tuple[list[SThought], list[str], str]:
    """Build items for one or more domains (comma-separated). Append-only merge."""
    tokens = parse_domain_list(domain_spec)
    yaml_ids = set(list_yaml_domains())
    for t in tokens:
        if t not in BUILTIN_DOMAINS and t not in yaml_ids:
            known = ", ".join(sorted(BUILTIN_DOMAINS | yaml_ids))
            raise SystemExit(f"Unknown domain {t!r}. Known: {known}")

    # none only alone
    if "none" in tokens and tokens != ["none"]:
        raise SystemExit("--domain none cannot be combined with other domains")

    items = parse_array(base_path)
    errs: list[str] = []
    applied: list[str] = []

    for t in tokens:
        if t == "none":
            ensure_fido_phase1(items)
            errs.extend(validate_phase1(items))
            applied.append("none")
        elif t == "animals_core":
            ensure_fido_phase1(items)
            apply_animals_core(items)
            applied.append("animals_core")
        elif t == "animals":
            ensure_fido_phase1(items)
            apply_animals_full(items)
            applied.append("animals")
        else:
            # YAML layer — needs spine from earlier builtins when possible
            if t == "plants" and "livingThing" not in pure_label_map(items):
                errs.append(
                    "plants requires livingThing (include animals or animals_core before plants)"
                )
            ypath = DOMAINS_DIR / f"{t}.yaml"
            apply_yaml_domain(items, ypath)
            applied.append(t)
            errs.extend(validate_yaml_domain_content(items, t))

    # Hardening validators depending on stack
    if applied == ["none"]:
        phase_label = "0-1 golden-parity"
    elif "animals" in applied and len(applied) == 1:
        errs.extend(validate_phase5(items))
        # validate_phase5 already includes integrity + phase4; avoid dup integrity later
        phase_label = "4+5 animals"
    elif applied == ["animals_core"]:
        errs.extend(validate_phase23(items))
        phase_label = "2-3 animals_core"
    else:
        # multi-domain or yaml-only stack
        if "animals" in applied:
            # full animals content still required
            p5 = validate_phase5(items)
            # phase23-forbidden filter already in phase4
            errs.extend(p5)
        elif "animals_core" in applied:
            errs.extend(validate_phase23(items))
        errs.extend(validate_list_index_integrity(items))
        errs.extend(validate_parent_audit(items, strict=False))
        errs.extend(validate_no_domain_hasproperty(items))
        phase_label = "6 multi-domain [" + ",".join(applied) + "]"

    # Always integrity once more if not already covered densely
    if "animals" not in applied or len(applied) > 1:
        pass  # already added above for multi; for single animals, phase5 has integrity
    else:
        pass

    # Deduplicate error strings while preserving order
    seen: set[str] = set()
    uniq: list[str] = []
    for e in errs:
        if e not in seen:
            seen.add(e)
            uniq.append(e)
    return items, uniq, phase_label


def build_domain(domain: str, base_path: Path) -> tuple[list[SThought], list[str], str]:
    """Backward-compatible single-domain builder."""
    return build_domain_stack(domain, base_path)


def main() -> None:
    parser = argparse.ArgumentParser(
        description=(
            "Generate BrainSim-Thought UKS project XML from NewDocument golden bootstrap. "
            "Default: Phase 4 full animals pack with Phase 5 validators."
        )
    )
    parser.add_argument("--base", type=str, default=str(DEFAULT_BASE))
    parser.add_argument("--output", type=str, default=None)
    parser.add_argument(
        "--domain",
        type=str,
        default="animals",
        help=(
            "Comma-separated domains: none | animals_core | animals | "
            "plants | inanimates | … (YAML under tools/domains/). "
            "Example: animals,plants,inanimates"
        ),
    )
    parser.add_argument(
        "--golden-parity",
        action="store_true",
        help="Shortcut for --domain none",
    )
    parser.add_argument(
        "--validate",
        action="store_true",
        help="Print validate: PASS after successful write (always validates before write)",
    )
    parser.add_argument(
        "--validate-only",
        action="store_true",
        help="Build + validate domain; do not write output",
    )
    parser.add_argument(
        "--check-file",
        type=str,
        default=None,
        help="Validate an existing ArrayOfSThought XML (no regenerate)",
    )
    parser.add_argument(
        "--strict-parents",
        action="store_true",
        help="Fail parent audit even on Initialize alphabet/digit/pi-seq* nodes",
    )
    parser.add_argument("--complete", action="store_true", help="Deprecated no-op")
    args = parser.parse_args()

    domain_spec = (args.domain or "animals").strip().lower()
    if args.golden_parity:
        domain_spec = "none"
    if domain_spec == "":
        domain_spec = "animals"
    tokens = parse_domain_list(domain_spec)
    domain_spec = ",".join(tokens)

    # Check-only mode for a file already on disk
    if args.check_file:
        path = Path(args.check_file)
        if not path.is_file():
            raise SystemExit(f"File not found: {path}")
        # Primary domain token for content checks
        primary = tokens[0] if len(tokens) == 1 else "animals"
        errs = validate_existing_file(path, domain=primary)
        items = parse_array(path)
        for t in tokens:
            if t not in BUILTIN_DOMAINS:
                errs.extend(validate_yaml_domain_content(items, t))
        if args.strict_parents:
            errs.extend(validate_parent_audit(items, strict=True))
        # Dedup
        seen: set[str] = set()
        uniq = []
        for e in errs:
            if e not in seen:
                seen.add(e)
                uniq.append(e)
        errs = uniq
        if errs:
            for e in errs:
                print(f"VALIDATE FAIL: {e}")
            raise SystemExit(1)
        stats = summarize(items)
        print(f"CHECK PASS: {path}")
        print(f"  domain: {domain_spec}")
        print(f"  pure_nodes: {stats['pure_nodes']}")
        print(f"  links: {stats['links']}")
        print(f"  total sThought entries: {stats['total_sThought']}")
        return

    base_path = Path(args.base)
    if not base_path.is_file():
        raise SystemExit(f"Bootstrap not found: {base_path}")

    items, errs, phase_label = build_domain_stack(domain_spec, base_path)
    if args.strict_parents:
        errs.extend(validate_parent_audit(items, strict=True))

    if errs:
        for e in errs:
            print(f"VALIDATE FAIL: {e}")
        raise SystemExit(1)

    stats = summarize(items)

    if args.validate_only:
        print("VALIDATE-ONLY PASS")
        print(f"  base: {base_path}")
        print(f"  domain: {domain_spec}")
        print(f"  pure_nodes: {stats['pure_nodes']}")
        print(f"  links: {stats['links']}")
        print(f"  total sThought entries: {stats['total_sThought']}")
        print(f"  phase: {phase_label}")
        return

    xml = render_xml(items)
    if args.output:
        out_path = Path(args.output)
    elif len(tokens) > 1 or any(t not in BUILTIN_DOMAINS for t in tokens):
        out_path = DEFAULT_WORLD_OUTPUT
    else:
        out_path = DEFAULT_OUTPUT
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(xml, encoding="utf-8")

    print(f"Wrote {out_path}")
    print(f"  base: {base_path}")
    print(f"  domain: {domain_spec}")
    print(f"  pure_nodes: {stats['pure_nodes']}")
    print(f"  links: {stats['links']}")
    print(f"  total sThought entries: {stats['total_sThought']}")
    print(f"  phase: {phase_label}")
    if args.validate:
        print("  validate: PASS")


if __name__ == "__main__":
    main()
