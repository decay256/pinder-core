#!/bin/bash
# Round-trip test: Markdown → YAML → Markdown
# Usage: roundtrip_test.sh [tools_dir]
# Defaults to using tools from same directory as this script.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RULES_DIR="${SCRIPT_DIR}/.."
DESIGN_DIR="/root/.openclaw/agents-extra/pinder/design"
EXTRACT="$SCRIPT_DIR/extract.py"
GENERATE="$SCRIPT_DIR/generate.py"

mkdir -p "$RULES_DIR/extracted" "$RULES_DIR/regenerated" "$RULES_DIR/diffs"

total_diff=0
max_diff=0
max_name=""
all_pass=true

for doc in "$DESIGN_DIR"/systems/*.md "$DESIGN_DIR"/settings/*.md; do
    name=$(basename "$doc" .md)
    echo "Processing $name..."
    python3 "$EXTRACT" "$doc" > "$RULES_DIR/extracted/$name.yaml"
    python3 "$GENERATE" "$RULES_DIR/extracted/$name.yaml" > "$RULES_DIR/regenerated/$name.md"
    diff "$doc" "$RULES_DIR/regenerated/$name.md" > "$RULES_DIR/diffs/$name.diff" || true

    yaml_entries=$(grep -c "^- id:" "$RULES_DIR/extracted/$name.yaml" 2>/dev/null || echo 0)
    diff_lines=$(wc -l < "$RULES_DIR/diffs/$name.diff")
    total_diff=$((total_diff + diff_lines))

    status="OK"
    if [ "$diff_lines" -gt 50 ]; then
        status="FAIL (>50)"
        all_pass=false
    fi
    if [ "$diff_lines" -gt "$max_diff" ]; then
        max_diff=$diff_lines
        max_name=$name
    fi

    echo "  YAML entries: $yaml_entries | Diff lines: $diff_lines | $status"
done

echo ""
echo "Total diff lines: $total_diff"
echo "Max diff: $max_name ($max_diff lines)"
if $all_pass; then
    echo "RESULT: ALL PASS (<= 50 lines per doc)"
else
    echo "RESULT: SOME FAIL (> 50 lines)"
fi
