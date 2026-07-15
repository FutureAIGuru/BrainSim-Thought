# SevenSphere import manifests

Used by `tools/generate_sevensphere_cloud.py`.

| File | Role |
|------|------|
| `seed.txt` | Phase 1 — 5 high-value spheres |
| `curated.txt` | Phase 2 — cognition / BrainSim-adjacent |
| `skip-list.txt` | Informational auto skip inventory |

Lines: filename (`UNIPHICS.json`) or Grey center label. `#` comments ignored.

```bash
python3 tools/generate_sevensphere_cloud.py --domain seed
python3 tools/generate_sevensphere_cloud.py --domain curated
python3 tools/generate_sevensphere_cloud.py --domain full
```
