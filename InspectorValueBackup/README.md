# Inspector Value Backup

Generated 2026-07-12. A human-readable dump of **every serialized Inspector value**
of the project's own scripts (`Assets/Scripts`), taken from every scene and prefab.

- One `.txt` file per scene/prefab (`__` in the filename = folder separator).
- Each `### ScriptName on GameObject "..."` block lists that component's serialized
  fields exactly as stored on disk. `{fileID: ...}` entries are object references
  (wiring), plain numbers are your tuned values.
- The `PREFAB-INSTANCE OVERRIDES` section at the bottom of scene dumps lists values
  that were changed on a prefab *instance* inside that scene (they live in the scene,
  not in the prefab asset).

## How to restore a lost value

Look up the script + GameObject in the matching `.txt` file and retype the value in
the Inspector. Note that git is the real safety net: scenes and prefabs are text
files under version control, so `git diff` / `git checkout <commit> -- <file>` can
always recover them too.

## Regenerate

```
bash regenerate.sh
```

(from this folder, in Git Bash)
