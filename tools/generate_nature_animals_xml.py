#!/usr/bin/env python3
"""Generate NatureAnimals.xml — sThought UKS import for BrainSim-Thought."""

from __future__ import annotations
import uuid
import argparse
from pathlib import Path

OUTPUT = Path(__file__).resolve().parents[1] / "BrainSimulator" / "UKSContent" / "NatureAnimals.xml"

nodes: list[str] = []
links: list[tuple[int, int, int, float | None]] = []
index_by_label: dict[str, int] = {}

# Minimal module sThoughts for a complete BrainSimThought project (from Demo1.xml patterns + Initialize)
# These make the XML loadable as a full project with modules active.
MODULE_NODES = [
    "BrainSim",
    "AvailableModule",
    "ActiveModule",
    "DlgAttrib",
    "ModuleAction",
    "ModuleAddCounts",
    "ModuleAddToMentalModel",
    "ModuleAlgorithm",
    "ModuleAttention",
    "ModuleAttentionS",
    "ModuleAttributeBubble",
    "ModuleBalanceTree",
    "ModuleClassCreate",
    "ModuleEmpty",
    "ModuleGPTInfo",
    "ModuleLearnMelody",
    "ModuleMentalModel",
    "ModuleOnlineInfo",
    "ModuleRemoveRedundancy",
    "ModuleSequenceEditor",
    "ModuleShowGraph",
    "ModuleSoundIn",
    "ModuleSoundOut",
    "ModuleStressTest",
    "ModuleText",
    "ModuleTextIn",
    "ModuleUKS",
    "ModuleUKSQuery",
    "ModuleUKSStatement",
    "ModuleWellBeing",
    "ModuleWord",
]

def add_module_bootstrap(ISA: str):
    """Add the system module scaffolding so NatureAnimals can run with modules."""
    for m in MODULE_NODES:
        add_node(m)
    # Link modules under AvailableModule / Active (simplified)
    if "AvailableModule" in index_by_label and "BrainSim" in index_by_label:
        add_link("AvailableModule", ISA, "BrainSim")
    if "ActiveModule" in index_by_label and "BrainSim" in index_by_label:
        add_link("ActiveModule", ISA, "BrainSim")
    # Link all Module* to AvailableModule (full list from working ExampleFile)
    for m in MODULE_NODES:
        if m.startswith("Module") and m in index_by_label and "AvailableModule" in index_by_label:
            add_link(m, ISA, "AvailableModule")


def unl() -> str:
    return f"unl_{uuid.uuid4().hex[:8]}"


def add_node(label: str) -> int:
    if label in index_by_label:
        return index_by_label[label]
    idx = len(nodes)
    nodes.append(label)
    index_by_label[label] = idx
    return idx


def add_link(source: str, link_type: str, target: str, weight: float | None = None) -> int:
    links.append((index_by_label[source], index_by_label[link_type], index_by_label[target], weight))
    return len(nodes) + len(links) - 1


def build() -> None:
    # --- Bootstrap ontology (required for UKS loader) ---
    for label in (
        "Thought", "LinkType", "Object", "Property", "Comparison",
        "bodyPart", "habitat", "sound", "Action",
    ):
        add_node(label)

    for label in (
        "is-a", "has", "has-child", "is", "can", "hasProperty",
        "isSimilarTo", "differsFrom", "predatorOf", "preyOf", "livesIn", "eats",
        "has.4", "has.3", "has.2", "has.6", "has.8", "has.no", "has.many",
        "warmBlooded", "coldBlooded", "givesLiveBirth", "laysEggs", "vertebrate",
        "NOT", "not", "lays", "egg", "owns", "Abstract", "color", "number", "digit", "many", "some", "none", "isExclusive", "isTransitive", "inheritable",
    ):
        add_node(label)

    # --- Nature taxonomy ---
    for label in (
        "nature", "livingThing", "animal", "plant", "ecosystem",
        "mammal", "bird", "reptile", "fish", "amphibian", "insect",
        "dog", "cat", "horse", "whale", "bat", "mouse", "wolf", "deer", "dolphin",
        "eagle", "sparrow", "penguin", "owl",
        "snake", "turtle", "crocodile",
        "salmon", "shark", "trout",
        "frog", "bee", "ant",
        "leg", "tail", "fur", "feather", "scale", "fin", "wing", "gill", "beak",
        "bark(dog)", "meow(cat)", "neigh(horse)", "buzz(bee)",
        "forest", "ocean", "sky", "grassland", "antarctica", "river", "tree",
        "brown", "black", "golden", "green", "gray",
        "pet", "wild",
        # Named instances (Fido-style)
        "Fido", "Tripper", "Whiskers", "Shadow", "Buddy",
        "Talon", "Slither", "Echo", "Nemo", "Penny", "Bramble",
    ):
        add_node(label)

    ISA = "is-a"
    HAS = "has"
    IS = "is"
    CAN = "can"
    HP = "hasProperty"

    # Module bootstrap for complete project (if flagged)
    import builtins
    if getattr(builtins, "_NATURE_COMPLETE", False):
        add_module_bootstrap(ISA)

    # Ontology structure
    add_link("LinkType", ISA, "Thought")
    add_link("Object", ISA, "Thought")
    add_link("Property", ISA, "Thought")
    add_link("Comparison", ISA, "Property")
    add_link("bodyPart", ISA, "Object")
    add_link("habitat", ISA, "Object")
    add_link("sound", ISA, "Object")
    add_link("Action", ISA, "Thought")
    add_link("nature", ISA, "Thought")

    add_link(ISA, ISA, "LinkType")
    add_link(HAS, ISA, "LinkType")
    add_link("has-child", ISA, "LinkType")
    add_link(IS, ISA, "LinkType")
    add_link(CAN, ISA, "LinkType")
    add_link(HP, ISA, "LinkType")
    add_link("isSimilarTo", ISA, "LinkType")
    add_link("isSimilarTo", ISA, "Comparison")
    add_link("differsFrom", ISA, "LinkType")
    add_link("differsFrom", ISA, "Comparison")
    add_link("predatorOf", ISA, "LinkType")
    add_link("preyOf", ISA, "LinkType")
    add_link("livesIn", ISA, "LinkType")
    add_link("eats", ISA, "LinkType")

    for n in ("has.4", "has.3", "has.2", "has.6", "has.8", "has.no", "has.many"):
        add_link(n, ISA, HAS)

    # Numeric grounding per Simon feedback: has.N is N (the number Thought)
    for val in ("0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "many", "no", "some"):
        add_node(val)
        add_link(val, ISA, "number")
    add_link("has.4", "is", "4")
    add_link("has.3", "is", "3")
    add_link("has.2", "is", "2")
    add_link("has.6", "is", "6")
    add_link("has.8", "is", "8")
    add_link("has.no", "is", "no")
    add_link("has.many", "is", "many")  # will add has.many node usage below

    for n in ("warmBlooded", "coldBlooded", "givesLiveBirth", "laysEggs", "vertebrate"):
        add_link(n, ISA, "Property")

    add_link("NOT", ISA, "Property")
    add_link("not", ISA, "NOT")

    # Living things hierarchy
    add_link("livingThing", ISA, "Object")
    add_link("ecosystem", ISA, "Object")
    add_link("animal", ISA, "livingThing")
    add_link("plant", ISA, "livingThing")
    add_link("tree", ISA, "plant")
    # nature hierarchy fix (was rogue with no children)
    add_link("livingThing", ISA, "nature")

    add_link("mammal", ISA, "animal")
    add_link("bird", ISA, "animal")
    add_link("reptile", ISA, "animal")
    add_link("fish", ISA, "animal")
    add_link("amphibian", ISA, "animal")
    add_link("insect", ISA, "animal")

    # Species → class
    for species, cls in (
        ("dog", "mammal"), ("cat", "mammal"), ("horse", "mammal"), ("whale", "mammal"),
        ("bat", "mammal"), ("mouse", "mammal"), ("wolf", "mammal"), ("deer", "mammal"),
        ("dolphin", "mammal"),
        ("eagle", "bird"), ("sparrow", "bird"), ("penguin", "bird"), ("owl", "bird"),
        ("snake", "reptile"), ("turtle", "reptile"), ("crocodile", "reptile"),
        ("salmon", "fish"), ("shark", "fish"), ("trout", "fish"),
        ("frog", "amphibian"), ("bee", "insect"), ("ant", "insect"),
    ):
        add_link(species, ISA, cls)

    # Body parts
    for part in ("leg", "tail", "fur", "feather", "scale", "fin", "wing", "gill", "beak"):
        add_link(part, ISA, "bodyPart")

    # Sounds
    for snd in ("bark(dog)", "meow(cat)", "neigh(horse)", "buzz(bee)"):
        add_link(snd, ISA, "sound")

    # Habitats
    for h in ("forest", "ocean", "sky", "grassland", "antarctica", "river"):
        add_link(h, ISA, "habitat")

    # Colors with parent (per Simon: all thoughts except Thought have parent)
    add_link("color", ISA, "Abstract")
    add_link("color", HP, "isExclusive")  # meta use of hasProperty OK
    for c in ("brown", "black", "golden", "green", "gray"):
        add_link(c, ISA, "color")

    # Class-level facts - use is-a / has per Simon feedback (hasProperty is for traversal meta only)
    # vertebrate as class
    add_link("vertebrate", ISA, "Object")
    add_link("mammal", ISA, "vertebrate")
    add_link("bird", ISA, "vertebrate")
    add_link("reptile", ISA, "vertebrate")
    add_link("fish", ISA, "vertebrate")
    add_link("amphibian", ISA, "vertebrate")
    # blood and birth as properties or has (use has for consistency with contains)
    add_link("mammal", HAS, "warmBlooded")
    add_link("mammal", HAS, "givesLiveBirth")
    add_link("bird", HAS, "warmBlooded")
    add_link("reptile", HAS, "coldBlooded")
    add_link("fish", HAS, "coldBlooded")
    add_link("amphibian", HAS, "coldBlooded")
    add_link("insect", HAS, "coldBlooded")
    # lays -> egg (action style)
    add_link("lays", ISA, "Action")
    add_link("egg", ISA, "bodyPart")
    add_link("bird", "lays", "egg")
    add_link("reptile", "lays", "egg")
    add_link("fish", "lays", "egg")
    add_link("amphibian", "lays", "egg")

    # Class-level anatomy (inherited by instances)
    add_link("dog", HAS, "tail")
    add_link("dog", "has.4", "leg")
    add_link("dog", HAS, "fur")
    add_link("dog", CAN, "bark(dog)")
    add_link("cat", HAS, "tail")
    add_link("cat", "has.4", "leg")
    add_link("cat", HAS, "fur")
    add_link("cat", CAN, "meow(cat)")
    add_link("horse", HAS, "tail")
    add_link("horse", "has.4", "leg")
    add_link("horse", CAN, "neigh(horse)")
    add_link("whale", HAS, "fin")
    add_link("whale", "has.no", "leg")  # exception: no legs
    add_link("bat", HAS, "wing")
    add_link("bat", "has.4", "leg")
    add_link("mouse", "has.4", "leg")
    add_link("mouse", HAS, "tail")
    add_link("wolf", HAS, "tail")
    add_link("wolf", "has.4", "leg")
    add_link("wolf", HAS, "fur")
    add_link("deer", "has.4", "leg")
    add_link("deer", HAS, "tail")
    add_link("dolphin", HAS, "fin")
    add_link("dolphin", "has.no", "leg")

    add_link("bird", HAS, "wing")
    add_link("bird", "has.many", "feather")
    add_link("bird", "has.2", "leg")
    add_link("bird", HAS, "beak")
    add_link("eagle", "has.2", "wing")
    add_link("sparrow", "has.2", "wing")
    add_link("penguin", "has.2", "wing")
    add_link("owl", "has.2", "wing")

    add_link("reptile", HAS, "scale")
    add_link("snake", "has.no", "leg")
    add_link("snake", HAS, "scale")
    add_link("turtle", "has.4", "leg")
    add_link("turtle", HAS, "scale")
    add_link("crocodile", "has.4", "leg")
    add_link("crocodile", HAS, "scale")

    add_link("fish", HAS, "fin")
    add_link("fish", HAS, "gill")
    add_link("fish", HAS, "scale")
    add_link("salmon", HAS, "fin")
    add_link("shark", HAS, "fin")
    add_link("shark", "has.no", "leg")

    add_link("frog", "has.4", "leg")
    add_link("bee", HAS, "wing")
    add_link("bee", "has.6", "leg")
    add_link("bee", CAN, "buzz(bee)")
    add_link("ant", "has.6", "leg")

    # Pet / wild classification
    add_link("dog", ISA, "pet")
    add_link("cat", ISA, "pet")
    add_link("horse", ISA, "pet")
    add_link("wolf", ISA, "wild")
    add_link("eagle", ISA, "wild")
    add_link("deer", ISA, "wild")

    # Cross-species similarities and differences
    add_link("dog", "isSimilarTo", "wolf")
    add_link("dog", "isSimilarTo", "cat")
    add_link("cat", "isSimilarTo", "dog")
    add_link("wolf", "isSimilarTo", "dog")
    add_link("salmon", "isSimilarTo", "trout")
    add_link("eagle", "isSimilarTo", "owl")

    add_link("dog", "differsFrom", "bird")
    add_link("bird", "differsFrom", "fish")
    add_link("mammal", "differsFrom", "reptile")
    add_link("whale", "differsFrom", "fish")
    add_link("bat", "differsFrom", "bird")
    add_link("snake", "differsFrom", "dog")

    # Predator / prey chains
    add_link("eagle", "predatorOf", "mouse")
    add_link("mouse", "preyOf", "eagle")
    add_link("wolf", "predatorOf", "deer")
    add_link("deer", "preyOf", "wolf")
    add_link("shark", "predatorOf", "salmon")
    add_link("salmon", "preyOf", "shark")
    add_link("owl", "predatorOf", "mouse")
    add_link("cat", "predatorOf", "mouse")

    # Habitat relationships
    add_link("deer", "livesIn", "forest")
    add_link("eagle", "livesIn", "sky")
    add_link("salmon", "livesIn", "river")
    add_link("whale", "livesIn", "ocean")
    add_link("shark", "livesIn", "ocean")
    add_link("dolphin", "livesIn", "ocean")
    add_link("penguin", "livesIn", "antarctica")
    add_link("sparrow", "livesIn", "forest")
    add_link("frog", "livesIn", "river")
    add_link("tree", "livesIn", "forest")

    # Diet
    add_link("dog", "eats", "salmon")
    add_link("cat", "eats", "mouse")
    add_link("eagle", "eats", "mouse")
    add_link("wolf", "eats", "deer")
    add_link("shark", "eats", "salmon")

    # --- Named instances (Fido pattern) ---
    # Fido inherits has.4 leg from dog class (clean inheritance)
    add_link("Fido", ISA, "dog")
    add_link("Fido", IS, "brown")
    add_link("Fido", "livesIn", "grassland")

    add_link("Tripper", ISA, "dog")
    add_link("Tripper", "has.3", "leg")  # exception overriding inherited 4 (per Simon feedback)

    add_link("Whiskers", ISA, "cat")
    add_link("Whiskers", IS, "black")
    add_link("Whiskers", "livesIn", "grassland")

    add_link("Shadow", ISA, "horse")
    add_link("Shadow", IS, "brown")
    add_link("Shadow", "livesIn", "grassland")

    add_link("Buddy", ISA, "dog")
    add_link("Buddy", IS, "golden")

    add_link("Talon", ISA, "eagle")
    add_link("Talon", "livesIn", "sky")

    add_link("Slither", ISA, "snake")
    add_link("Slither", IS, "green")
    add_link("Slither", "livesIn", "forest")

    add_link("Echo", ISA, "bat")
    add_link("Echo", "livesIn", "forest")

    add_link("Nemo", ISA, "salmon")
    add_link("Nemo", "livesIn", "river")

    add_link("Penny", ISA, "penguin")
    add_link("Penny", "livesIn", "antarctica")

    add_link("Bramble", ISA, "deer")
    add_link("Bramble", IS, "brown")
    add_link("Bramble", "livesIn", "forest")


def render_xml() -> str:
    lines = [
        '<?xml version="1.0" encoding="utf-8"?>',
        '<ArrayOfSThought xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">',
    ]
    for i, label in enumerate(nodes):
        lines.extend([
            "  <sThought>",
            f"    <index>{i}</index>",
            f"    <label>{label}</label>",
            "  </sThought>",
        ])
    base = len(nodes)
    for j, (src, lt, tgt, weight) in enumerate(links):
        # Adjusted indexing: set link's <index> to the source node's index
        # This matches the pattern in working ExampleFile.xml (duplicates for registration links)
        idx = src
        lines.extend([
            "  <sThought>",
            f"    <index>{idx}</index>",
            f"    <label>{unl()}</label>",
            f"    <source>{src}</source>",
            f"    <linkType>{lt}</linkType>",
            f"    <target>{tgt}</target>",
        ])
        if weight is not None:
            lines.append(f"    <weight>{weight}</weight>")
        lines.append("  </sThought>")
    lines.append("</ArrayOfSThought>")
    return "\n".join(lines) + "\n"


def main() -> None:
    parser = argparse.ArgumentParser(description="Generate NatureAnimals UKS XML for BrainSim-Thought (proper project format)")
    parser.add_argument("--complete", action="store_true", help="Emit full project XML with module sThoughts (BrainSim + AvailableModule etc.)")
    parser.add_argument("--output", type=str, default=None, help="Override output path")
    args = parser.parse_args()

    # Minimal fix: pre-add module nodes so they get indices 0+ when --complete
    # (BrainSim scaffolding must come first in the XML for the loader, like Demo1.xml)
    if args.complete:
        for m in MODULE_NODES:
            add_node(m)

    # Pass complete flag via a simple global for bootstrap timing (after constants)
    import builtins
    builtins._NATURE_COMPLETE = args.complete

    build()
    xml = render_xml()
    out_path = Path(args.output) if args.output else OUTPUT
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(xml, encoding="utf-8")
    print(f"Wrote {out_path}")
    print(f"  nodes: {len(nodes)}")
    print(f"  links: {len(links)}")
    print(f"  total sThought entries: {len(nodes) + len(links)}")
    if args.complete:
        print("  (complete project with modules)")


if __name__ == "__main__":
    main()