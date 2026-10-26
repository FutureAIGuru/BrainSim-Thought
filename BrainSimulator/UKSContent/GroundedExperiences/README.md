# Grounded experiences

This optional content root extends the original GroundedDogs demo without
renaming it. Add any of these subdirectories as needed:

- `Observations`: one external observation per `.txt` file, including a
  `# subject:` header.
- `Images`: image files referenced by `hasImage` relationships.
- `Lessons`: event scripts containing `show` and `hear` commands.

Example lesson:

```text
show Whiskers.txt distance 1.5
hear This is Whiskers.
hear Whiskers is a cat.
```
