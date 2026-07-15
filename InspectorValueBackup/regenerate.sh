#!/bin/bash
# Dumps all serialized Inspector values of project scripts (Assets/Scripts) from
# every scene and prefab into readable text files under InspectorValueBackup/.
set -e
ROOT="/c/Repositories/FishOWisp2"
OUT="$ROOT/InspectorValueBackup"
mkdir -p "$OUT"

# 1. Build guid -> script-name map from Assets/Scripts .meta files
GUIDMAP="$OUT/.guidmap.txt"
> "$GUIDMAP"
find "$ROOT/Assets/Scripts" -name '*.cs.meta' | while read -r meta; do
  guid=$(grep -m1 '^guid:' "$meta" | awk '{print $2}')
  name=$(basename "$meta" .cs.meta)
  echo "$guid $name" >> "$GUIDMAP"
done

# 2. Process each scene and prefab
find "$ROOT/Assets" -path "$ROOT/Assets/Plugins" -prune -o \( -name '*.unity' -o -name '*.prefab' \) -print | while read -r f; do
  rel="${f#$ROOT/Assets/}"
  outfile="$OUT/$(echo "$rel" | sed 's/[\/]/__/g').txt"
  awk -v GUIDMAP="$GUIDMAP" -v SRC="$rel" '
    BEGIN {
      while ((getline line < GUIDMAP) > 0) {
        split(line, a, " "); gmap[a[1]] = a[2];
      }
      close(GUIDMAP);
    }
    # ---------- pass 1: GameObject names ----------
    NR==FNR {
      if ($0 ~ /^--- !u!/) {
        blocktype = ""; curid = "";
        if (match($0, /^--- !u!1 &[0-9]+/)) {
          blocktype = "go";
          curid = $0; sub(/^--- !u!1 &/, "", curid); sub(/ .*/, "", curid);
        }
      } else if (blocktype == "go" && $0 ~ /^  m_Name: /) {
        n = $0; sub(/^  m_Name: /, "", n);
        goname[curid] = n;
      }
      next;
    }
    # ---------- pass 2: MonoBehaviours + prefab overrides ----------
    function flushmb() {
      if (mb_guid != "" && (mb_guid in gmap) && mb_fields != "") {
        gname = (mb_go in goname) ? goname[mb_go] : "(nested/prefab root)";
        printf "\n### %s  on GameObject \"%s\"\n%s", gmap[mb_guid], gname, mb_fields;
      }
      mb_guid = ""; mb_go = ""; mb_fields = ""; capturing = 0;
    }
    /^--- / {
      flushmb();
      inmb = 0; inprefab = 0;
      if ($0 ~ /^--- !u!114 &/) inmb = 1;
      if ($0 ~ /^--- !u!1001 &/) inprefab = 1;
      next;
    }
    inmb {
      if ($0 ~ /^  m_GameObject: /) {
        g = $0; sub(/.*fileID: /, "", g); sub(/[^0-9].*/, "", g); mb_go = g;
      } else if ($0 ~ /^  m_Script: /) {
        g = $0;
        if (match(g, /guid: [0-9a-f]+/)) {
          mb_guid = substr(g, RSTART+6, RLENGTH-6);
        }
      } else if ($0 ~ /^  m_EditorClassIdentifier/) {
        capturing = 1;
      } else if (capturing) {
        mb_fields = mb_fields $0 "\n";
      }
      next;
    }
    inprefab {
      if ($0 ~ /^    - target: /) { pp = ""; }
      else if ($0 ~ /^      propertyPath: /) {
        pp = $0; sub(/^      propertyPath: /, "", pp);
        q = sprintf("%c", 39); gsub(q, "", pp);
      }
      else if ($0 ~ /^      value: / && pp != "" && pp !~ /^m_/) {
        v = $0; sub(/^      value: /, "", v);
        overrides = overrides "  " pp ": " v "\n";
      }
      next;
    }
    END {
      flushmb();
      if (overrides != "") {
        printf "\n### PREFAB-INSTANCE OVERRIDES (values changed on prefab instances in this file)\n%s", overrides;
      }
    }
  ' "$f" "$f" > "$outfile.tmp"

  if [ -s "$outfile.tmp" ]; then
    { echo "# Inspector values from: $rel"; echo "# Backup date: $(date +%F)"; cat "$outfile.tmp"; } > "$outfile"
  fi
  rm -f "$outfile.tmp"
done
rm -f "$GUIDMAP"
echo "Done. Files written:"
ls -la "$OUT" | head -40
