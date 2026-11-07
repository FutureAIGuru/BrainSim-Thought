# External JavaScript snapshot

These checked-in files are served by UKSWeb itself. Graph pages do not need a
JavaScript CDN at runtime. Update these deliberately rather than replacing them
with unversioned downloads.

Snapshot date: 2026-11-06.

| Local file | Version | Download source |
| --- | --- | --- |
| `3d-force-graph-1.80.1.min.js` | 1.80.1 | https://cdn.jsdelivr.net/npm/3d-force-graph@1.80.1/dist/3d-force-graph.min.js |
| `three-spritetext-1.10.0.mjs` | 1.10.0 | https://esm.sh/three-spritetext@1.10.0/es2022/three-spritetext.mjs |
| `three.module.min.js` | 0.187.0 | https://cdn.jsdelivr.net/npm/three@0.187.0/build/three.module.min.js |
| `three.core.min.js` | 0.187.0 | https://cdn.jsdelivr.net/npm/three@0.187.0/build/three.core.min.js |

The force-graph distribution bundles its own rendering/layout dependencies.
SpriteText's separate Three.js dependency is also included here. Its version
matches the version resolved by the former esm.sh import at snapshot time.

The only changes to the SpriteText download are replacing its remote Three.js
import with `./three.module.min.js` and removing the reference to an absent source
map. Three.js's module import is also rewritten from `./three.core.js` to the adjacent
`./three.core.min.js`.
Preserve both Three.js files together. The adjacent LICENSE files accompany the
three MIT-licensed packages; bundled license notices are retained in the scripts.

All guided experiments and the standalone graph use this same snapshot through
`uks-graph.js`. ASP.NET Core includes `wwwroot` in published output, so no package
installation or frontend build is required. After changing a library, update its
filename/imports and the graph component's cache version, then check node labels,
hover, expansion/collapse, and sequences in both page types.

This snapshots JavaScript only. Google Fonts and the WordPress preview's logo
remain external resources.
