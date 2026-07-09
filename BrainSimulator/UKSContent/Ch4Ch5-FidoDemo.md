# Ch.4 + Ch.5 Fido demo — human test script

Load `Ch4Ch5-FidoDemo.txt` (File → Open network using text import, or run UKS import in tests).

## 1. Inheritance (Ch.5)

1. Query **From:** `Fido`, **Type:** `has`, **To:** `fur` → inherited via `dog`.
2. Query **From:** `Fido`, **Type:** `has`, **To:** `owner` → inherited via `pet` (multi-parent).
3. Query **From:** `Tripper`, **Type:** `has.3`, **To:** `legs` → local exception; `has.4` must not appear.

## 2. Explainability (Ch.5)

1. Query **From:** `Fido`, **To:** `fur`.
2. Click **Why?** → trace `Fido → dog → fur`.

## 3. Gated traversal (Ch.4)

1. In UKS Query or debugger: activate only `Fido` → gated `has` empty.
2. Activate `Fido` + `has` → `fur` reachable.

## 4. Bubbling (Ch.5)

1. Enable **ModuleAttributeBubble**.
2. Add matching `has` links on sibling instances under a category.
3. Confirm parent gains bubbled link; check `UKS.BubbleLog` for `bubble` entries.