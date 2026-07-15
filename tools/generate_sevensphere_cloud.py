#!/usr/bin/env python3
"""Generate BrainSim-Thought UKS project XML from SevenSphere JSON spheres.

Maps each sphere (Grey center + six color satellites) into UKS Thoughts with
typed aspect links — NOT false is-a taxonomy between center and aspects.

  UNIPHICS is-a sphereCenter
  Energy Density is-a sphereAspect
  UNIPHICS --hasBlueAspect--> Energy Density

**Center-only spheres are never imported.** A sphere must have Grey plus at least
one non-empty color satellite that is not a self-label of the center. Files with
only Grey (empty Cyan/Blue/Magenta/Red/Yellow/Green) are skipped and do not
appear as sphereCenter nodes in the XML.

Domains:
  seed     — tools/sevensphere_manifests/seed.txt
  curated  — tools/sevensphere_manifests/curated.txt
  full     — all parseable JSON in --input-dir
  sense    — curated + tools/domains/sevensphere_sense.yaml (optional hand facts)

Reuses ensure_* / parse / render helpers from generate_nature_animals_xml.py.
Git policy: never commit/push BrainSim-Thought from the Grok tower.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

TOOLS_DIR = Path(__file__).resolve().parent
REPO_ROOT = TOOLS_DIR.parent
sys.path.insert(0, str(TOOLS_DIR))

from generate_nature_animals_xml import (  # noqa: E402
    DEFAULT_BASE,
    apply_yaml_domain,
    ensure_domain_link_type,
    ensure_fido_phase1,
    ensure_is_a,
    ensure_link,
    ensure_pure_node,
    has_is_a,
    has_triple,
    index_to_label,
    parse_array,
    pure_label_map,
    render_xml,
    summarize,
    validate_list_index_integrity,
    validate_parent_audit,
)

MANIFESTS_DIR = TOOLS_DIR / "sevensphere_manifests"
DOMAINS_DIR = TOOLS_DIR / "domains"
DEFAULT_INPUT = Path.home() / "Desktop" / "Areas" / "Personal" / "sevenspheres" / "data"
DEFAULT_OUTPUT = REPO_ROOT / "BrainSimulator" / "UKSContent" / "SevenSphereCloud.xml"

COLORS = ("Cyan", "Blue", "Magenta", "Red", "Yellow", "Green")
COLOR_LINK = {
    "Green": "hasGreenAspect",
    "Blue": "hasBlueAspect",
    "Red": "hasRedAspect",
    "Cyan": "hasCyanAspect",
    "Magenta": "hasMagentaAspect",
    "Yellow": "hasYellowAspect",
}

SEED_TRIPLES = [
    ("UNIPHICS", "hasBlueAspect", "Energy Density"),
    ("UNIPHICS", "hasCyanAspect", "Amorphics"),
    ("UNIPHICS", "hasGreenAspect", "Spin"),
    ("BrainSim3", "hasGreenAspect", "UKS"),
    ("BrainSim3", "hasBlueAspect", "Modules"),
    ("Seven Sphere", "hasGreenAspect", "Structure"),
    ("Seven Sphere", "hasCyanAspect", "Observation"),
    ("Algorithm", "hasBlueAspect", "Input"),
    ("Algorithm", "hasGreenAspect", "Output"),
]


def normalize_key(label: str) -> str:
    s = label.replace("_", " ").strip()
    s = re.sub(r"\s+", " ", s)
    return s.upper()


def raw_color_aspects(data: dict) -> list[tuple[str, str]]:
    """Non-empty color satellites (may still be self-labels of Grey)."""
    out: list[tuple[str, str]] = []
    for color in COLORS:
        raw = str(data.get(color, "")).strip()
        if raw:
            out.append((color, raw))
    return out


def usable_aspects(grey_raw: str, data: dict) -> list[tuple[str, str]]:
    """Aspects that can form a real center→satellite link (not empty, not self).

    Spheres with only a center (Grey filled, no usable satellites) return [].
    """
    grey_key = normalize_key(grey_raw)
    if not grey_key:
        return []
    usable: list[tuple[str, str]] = []
    for color, raw in raw_color_aspects(data):
        if normalize_key(raw) == grey_key:
            continue
        usable.append((color, raw))
    return usable


class LabelRegistry:
    """Map normalized keys → first-seen display labels (including bootstrap)."""

    def __init__(self, items) -> None:
        self.norm_to_display: dict[str, str] = {}
        for lab in pure_label_map(items):
            self.norm_to_display[normalize_key(lab)] = lab

    def resolve(self, raw: str) -> str | None:
        text = (raw or "").strip()
        if not text:
            return None
        key = normalize_key(text)
        if key in self.norm_to_display:
            return self.norm_to_display[key]
        self.norm_to_display[key] = text
        return text


def load_manifest(path: Path) -> list[str]:
    if not path.is_file():
        raise SystemExit(f"Manifest not found: {path}")
    lines: list[str] = []
    for line in path.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        lines.append(line)
    return lines


def iter_sphere_files(input_dir: Path) -> list[Path]:
    return sorted(input_dir.glob("*.json"))


def load_sphere(path: Path) -> dict | None:
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except Exception as exc:
        return {"__error__": f"parse_error: {exc}", "__path__": path.name}
    if not isinstance(data, dict):
        return {"__error__": "not_object", "__path__": path.name}
    return data


def sphere_matches_manifest(data: dict, path: Path, tokens: set[str]) -> bool:
    if not tokens:
        return True
    name = path.name
    stem = path.stem
    grey = str(data.get("Grey", "")).strip()
    fname = str(data.get("Filename", "")).strip()
    candidates = {
        name,
        stem,
        grey,
        fname,
        normalize_key(grey) if grey else "",
        normalize_key(stem),
    }
    # also allow basename match without hash suffix
    candidates.add(re.sub(r"_[0-9a-f]{6,}$", "", stem, flags=re.I))
    for t in tokens:
        if t in candidates or normalize_key(t) in candidates:
            return True
        if t == name or t == fname:
            return True
    return False


def install_ontology(items) -> None:
    """sevenSphereDomain + classes + six aspect link types."""
    pure = pure_label_map(items)
    for required in ("Thought", "Unknown", "is-a", "LinkType"):
        if required not in pure:
            raise RuntimeError(
                f"bootstrap missing {required!r}; use tools/fixtures/NewDocument-golden.xml"
            )

    ensure_pure_node(items, "sevenSphereDomain")
    ensure_pure_node(items, "sphereCenter")
    ensure_pure_node(items, "sphereAspect")
    ensure_is_a(items, "sevenSphereDomain", "Thought")
    ensure_is_a(items, "sphereCenter", "sevenSphereDomain")
    ensure_is_a(items, "sphereAspect", "sevenSphereDomain")

    for lt in COLOR_LINK.values():
        ensure_domain_link_type(items, lt)


def apply_sphere(
    items,
    registry: LabelRegistry,
    data: dict,
    *,
    stats: dict,
) -> bool:
    """Import one sphere. Returns True if imported.

    Center-only spheres (Grey only, or only self-referential satellites) are
    skipped entirely — no sphereCenter node is written for them.
    """
    grey_raw = str(data.get("Grey", "")).strip()
    if not grey_raw:
        stats["skipped_empty_grey"] += 1
        return False

    aspects = usable_aspects(grey_raw, data)
    if not aspects:
        # Distinguish pure center-only vs all satellites equal center
        if raw_color_aspects(data):
            stats["skipped_self_aspects"] += 1
            stats["skipped_center_only"] += 1
        else:
            stats["skipped_empty_aspects"] += 1
            stats["skipped_center_only"] += 1
        return False

    center = registry.resolve(grey_raw)
    assert center is not None
    # Only materialize center after we know ≥1 real aspect exists
    ensure_pure_node(items, center)
    ensure_is_a(items, center, "sphereCenter")

    links_made = 0
    for color, raw in aspects:
        aspect = registry.resolve(raw)
        assert aspect is not None
        # Defensive: usable_aspects already filtered self-labels
        if normalize_key(aspect) == normalize_key(center):
            stats["skipped_self_aspects"] += 1
            continue
        ensure_pure_node(items, aspect)
        # Dual role OK: if already a center, keep sphereCenter; else sphereAspect
        if not has_is_a(items, aspect, "sphereCenter"):
            ensure_is_a(items, aspect, "sphereAspect")
        link_type = COLOR_LINK[color]
        ensure_link(items, center, link_type, aspect)
        stats["aspect_links"] += 1
        links_made += 1

    if links_made == 0:
        # Should not happen after usable_aspects; do not count as imported.
        # Note: center may already exist from bootstrap collision (e.g. Thought).
        stats["skipped_center_only"] += 1
        return False

    stats["spheres_imported"] += 1
    return True


def select_spheres(
    input_dir: Path,
    domain: str,
    manifest_path: Path | None,
    max_spheres: int | None,
) -> tuple[list[tuple[Path, dict]], list[str]]:
    """Return (path, data) list and skip warnings."""
    warnings: list[str] = []
    tokens: set[str] | None = None
    if domain in ("seed", "curated"):
        mpath = manifest_path or (MANIFESTS_DIR / f"{domain}.txt")
        raw_tokens = load_manifest(mpath)
        tokens = set(raw_tokens) | {normalize_key(t) for t in raw_tokens}
    elif domain in ("full", "sense"):
        tokens = None
        if domain == "sense":
            # sense = curated geometric + yaml
            mpath = manifest_path or (MANIFESTS_DIR / "curated.txt")
            raw_tokens = load_manifest(mpath)
            tokens = set(raw_tokens) | {normalize_key(t) for t in raw_tokens}
    else:
        raise SystemExit(f"Unknown domain {domain!r}; use seed|curated|full|sense")

    selected: list[tuple[Path, dict]] = []
    seen_center: set[str] = set()

    for path in iter_sphere_files(input_dir):
        data = load_sphere(path)
        if data is None:
            continue
        if "__error__" in data:
            warnings.append(f"{path.name}: {data['__error__']}")
            continue
        if tokens is not None and not sphere_matches_manifest(data, path, tokens):
            continue
        grey = str(data.get("Grey", "")).strip()
        if not grey:
            warnings.append(f"{path.name}: empty_grey")
            continue
        key = normalize_key(grey)
        if key in seen_center:
            # Prefer first file; note duplicate centers
            warnings.append(f"{path.name}: duplicate_center_skipped ({grey})")
            continue
        # Require ≥1 usable satellite (not center-only / not all self-labels)
        if not usable_aspects(grey, data):
            if raw_color_aspects(data):
                warnings.append(f"{path.name}: center_only_self_aspects")
            else:
                warnings.append(f"{path.name}: center_only_no_aspects")
            continue
        seen_center.add(key)
        selected.append((path, data))
        if max_spheres is not None and len(selected) >= max_spheres:
            break

    return selected, warnings


def validate_bootstrap(items) -> list[str]:
    errs: list[str] = []
    pure = pure_label_map(items)
    for lab in ("BrainSim", "Unknown", "Thought", "is-a", "dog", "Fido"):
        if lab not in pure:
            errs.append(f"missing pure label: {lab}")
    if not has_is_a(items, "dog", "Unknown"):
        errs.append("missing dog --is-a--> Unknown")
    if not has_is_a(items, "Fido", "dog"):
        errs.append("missing Fido --is-a--> dog")
    return errs


def validate_ontology(items) -> list[str]:
    errs: list[str] = []
    pure = pure_label_map(items)
    for lab in (
        "sevenSphereDomain",
        "sphereCenter",
        "sphereAspect",
        *COLOR_LINK.values(),
    ):
        if lab not in pure:
            errs.append(f"missing ontology label: {lab}")
    if not has_is_a(items, "sevenSphereDomain", "Thought"):
        errs.append("sevenSphereDomain not under Thought")
    if not has_is_a(items, "sphereCenter", "sevenSphereDomain"):
        errs.append("sphereCenter not under sevenSphereDomain")
    for lt in COLOR_LINK.values():
        if not has_is_a(items, lt, "LinkType"):
            errs.append(f"{lt} not is-a LinkType")
    return errs


def validate_seed_content(items) -> list[str]:
    errs: list[str] = []
    pure = pure_label_map(items)
    # Resolve expected labels via pure map fuzzy (bootstrap Thought)
    for src, lt, tgt in SEED_TRIPLES:
        # allow registry display forms
        src_ok = src if src in pure else None
        tgt_ok = tgt if tgt in pure else None
        if src_ok is None:
            # try case-insensitive
            for lab in pure:
                if normalize_key(lab) == normalize_key(src):
                    src_ok = lab
                    break
        if tgt_ok is None:
            for lab in pure:
                if normalize_key(lab) == normalize_key(tgt):
                    tgt_ok = lab
                    break
        if src_ok is None:
            errs.append(f"missing seed center: {src}")
            continue
        if tgt_ok is None:
            errs.append(f"missing seed aspect: {tgt}")
            continue
        if not has_triple(items, src_ok, lt, tgt_ok):
            errs.append(f"missing seed triple: {src_ok} --{lt}--> {tgt_ok}")
    return errs


def validate_no_false_taxonomy(items, centers: set[str], aspects_by_center: dict[str, set[str]]) -> list[str]:
    """Centers must not be is-a their own color aspects (false taxonomy guard)."""
    errs: list[str] = []
    for center, aspects in aspects_by_center.items():
        for asp in aspects:
            if has_is_a(items, center, asp):
                errs.append(f"false taxonomy: {center} is-a {asp}")
            if has_is_a(items, asp, center) and normalize_key(asp) != normalize_key(center):
                # aspect is-a center is also wrong for geometric import
                errs.append(f"false taxonomy: {asp} is-a {center}")
    return errs


def validate_no_lonely_centers(items) -> list[str]:
    """Every pure label is-a sphereCenter must have ≥1 has*Aspect outbound link.

    Guards against center-only imports and sense-YAML-only centers without geometry.
    """
    errs: list[str] = []
    pure = pure_label_map(items)
    labels = index_to_label(items)
    isa = pure.get("is-a")
    sc = pure.get("sphereCenter")
    if isa is None or sc is None:
        return errs

    aspect_lt = {pure[name] for name in COLOR_LINK.values() if name in pure}
    centers: set[str] = set()
    with_aspect: set[str] = set()
    for it in items:
        if not it.is_link:
            continue
        if it.link_type == isa and it.target == sc:
            src = labels.get(it.source)
            if src:
                centers.add(src)
        if it.link_type in aspect_lt:
            src = labels.get(it.source)
            if src:
                with_aspect.add(src)

    lonely = sorted(centers - with_aspect)
    if lonely:
        sample = ", ".join(lonely[:15])
        more = f" (+{len(lonely) - 15} more)" if len(lonely) > 15 else ""
        errs.append(
            f"{len(lonely)} sphereCenter(s) with no aspect links (center-only): "
            f"{sample}{more}"
        )
    return errs


def build(
    *,
    base_path: Path,
    input_dir: Path,
    domain: str,
    manifest_path: Path | None,
    max_spheres: int | None,
    with_sense: bool,
) -> tuple[list, list[str], dict, list[str]]:
    items = parse_array(base_path)
    ensure_fido_phase1(items)
    install_ontology(items)
    registry = LabelRegistry(items)

    selected, warnings = select_spheres(input_dir, domain, manifest_path, max_spheres)
    stats = {
        "spheres_selected": len(selected),
        "spheres_imported": 0,
        "aspect_links": 0,
        "skipped_empty_grey": 0,
        "skipped_empty_aspects": 0,
        "skipped_self_aspects": 0,
        "skipped_center_only": 0,
        "warnings": len(warnings),
    }

    centers: set[str] = set()
    aspects_by_center: dict[str, set[str]] = {}

    for path, data in selected:
        grey_raw = str(data.get("Grey", "")).strip()
        usable = usable_aspects(grey_raw, data)
        if not usable:
            # Double-check: never import center-only even if selection drifted
            stats["skipped_center_only"] += 1
            warnings.append(f"{path.name}: center_only_blocked_at_apply")
            continue
        imported = apply_sphere(items, registry, data, stats=stats)
        if not imported:
            continue
        grey = registry.resolve(grey_raw)
        if grey:
            centers.add(grey)
            aspects_by_center.setdefault(grey, set())
            for color, raw in usable:
                asp = registry.resolve(raw)
                if asp:
                    aspects_by_center[grey].add(asp)

    if with_sense or domain == "sense":
        sense_path = DOMAINS_DIR / "sevensphere_sense.yaml"
        if sense_path.is_file():
            apply_yaml_domain(items, sense_path)
            stats["sense_yaml"] = 1
        else:
            warnings.append(f"sense yaml missing: {sense_path}")
            stats["sense_yaml"] = 0

    errs: list[str] = []
    errs.extend(validate_list_index_integrity(items))
    errs.extend(validate_bootstrap(items))
    errs.extend(validate_ontology(items))
    errs.extend(validate_parent_audit(items, strict=False))
    # Seed sample triples only when seed/curated/sense manifests include them
    if domain in ("seed", "curated", "sense"):
        errs.extend(validate_seed_content(items))
    errs.extend(validate_no_false_taxonomy(items, centers, aspects_by_center))
    errs.extend(validate_no_lonely_centers(items))

    # dedup
    seen: set[str] = set()
    uniq: list[str] = []
    for e in errs:
        if e not in seen:
            seen.add(e)
            uniq.append(e)
    return items, uniq, stats, warnings


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Generate UKS thought cloud from SevenSphere JSON files."
    )
    parser.add_argument("--base", type=str, default=str(DEFAULT_BASE))
    parser.add_argument(
        "--input-dir",
        type=str,
        default=str(DEFAULT_INPUT),
        help="Directory of SevenSphere *.json spheres",
    )
    parser.add_argument(
        "--domain",
        type=str,
        default="seed",
        choices=("seed", "curated", "full", "sense"),
        help="seed | curated | full | sense (curated + sense YAML)",
    )
    parser.add_argument(
        "--manifest",
        type=str,
        default=None,
        help="Override manifest path (seed/curated lists)",
    )
    parser.add_argument("--output", type=str, default=None)
    parser.add_argument("--validate-only", action="store_true")
    parser.add_argument("--max-spheres", type=int, default=None)
    parser.add_argument(
        "--with-sense",
        action="store_true",
        help="Also apply tools/domains/sevensphere_sense.yaml",
    )
    args = parser.parse_args()

    base_path = Path(args.base)
    input_dir = Path(args.input_dir).expanduser()
    if not base_path.is_file():
        raise SystemExit(f"Bootstrap not found: {base_path}")
    if not input_dir.is_dir():
        raise SystemExit(f"Input dir not found: {input_dir}")

    manifest = Path(args.manifest) if args.manifest else None
    items, errs, stats, warnings = build(
        base_path=base_path,
        input_dir=input_dir,
        domain=args.domain,
        manifest_path=manifest,
        max_spheres=args.max_spheres,
        with_sense=args.with_sense,
    )

    if errs:
        for e in errs:
            print(f"VALIDATE FAIL: {e}")
        raise SystemExit(1)

    s = summarize(items)
    print("VALIDATE PASS" if args.validate_only else "BUILD OK")
    print(f"  base: {base_path}")
    print(f"  input: {input_dir}")
    print(f"  domain: {args.domain}")
    print(f"  spheres_selected: {stats['spheres_selected']}")
    print(f"  spheres_imported: {stats['spheres_imported']}")
    print(f"  aspect_links: {stats['aspect_links']}")
    print(f"  skipped_center_only: {stats.get('skipped_center_only', 0)}")
    print(f"  pure_nodes: {s['pure_nodes']}")
    print(f"  links: {s['links']}")
    print(f"  total sThought: {s['total_sThought']}")
    if warnings:
        print(f"  warnings: {len(warnings)}")
        for w in warnings[:12]:
            print(f"    - {w}")
        if len(warnings) > 12:
            print(f"    … +{len(warnings) - 12} more")

    if args.validate_only:
        return

    if args.output:
        out_path = Path(args.output)
    elif args.domain == "seed":
        out_path = (
            REPO_ROOT
            / "BrainSimulator"
            / "UKSContent"
            / "Generated-SS-Phase1-Seed.xml"
        )
    elif args.domain == "curated":
        out_path = (
            REPO_ROOT
            / "BrainSimulator"
            / "UKSContent"
            / "Generated-SS-Phase2-Curated.xml"
        )
    elif args.domain == "sense":
        out_path = (
            REPO_ROOT
            / "BrainSimulator"
            / "UKSContent"
            / "Generated-SS-Phase3-Sense.xml"
        )
    else:
        out_path = DEFAULT_OUTPUT

    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(render_xml(items), encoding="utf-8")
    print(f"Wrote {out_path}")


if __name__ == "__main__":
    main()
