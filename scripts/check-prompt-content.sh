#!/bin/bash
# ==============================================================================
# Prompt Content Gate - scripts/check-prompt-content.sh
#
# This script blocks the reintroduction of hardcoded prompt content in C# const
# strings under the src/ directory.
#
# Allowlist mechanism (one relative path per line, comments/empty lines allowed):
# --- ALLOWLIST START ---
# src/Pinder.SessionSetup/LlmStakeGenerator.cs
# src/Pinder.SessionSetup/LlmOutfitDescriber.cs
# src/Pinder.Core/Conversation/PlayerResponseDelayEvaluator.cs
# --- ALLOWLIST END ---
# ==============================================================================

# Ensure we run from the repo root
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$REPO_ROOT"

# Parse allowlist from the script header
allowlist=()
while read -r line; do
    if [[ "$line" =~ ^#[[:space:]]+src/ ]]; then
        clean_line=$(echo "$line" | sed -E 's/^#[[:space:]]*//' | xargs)
        allowlist+=("$clean_line")
    fi
done < "$0"

echo "Parsed allowlist:"
for allowed in "${allowlist[@]}"; do
    echo "  - $allowed"
done
echo ""

# Find all C# files in src/
files=$(find src/ -name "*.cs")

has_violations=0

for file in $files; do
    # Check if file is in allowlist
    is_allowed=0
    for allowed in "${allowlist[@]}"; do
        if [[ "$file" == "$allowed" ]]; then
            is_allowed=1
            break
        fi
    done

    if [[ $is_allowed -eq 1 ]]; then
        continue
    fi

    # Check for prompt content inside const string blocks
    output=$(awk '
    BEGIN {
        in_const = 0;
        block = "";
        start_line = 0;
        violation = 0;
    }
    /const[[:space:]]+string/ {
        in_const = 1;
        block = $0;
        start_line = NR;
        if ($0 ~ /;/) {
            check_block(block, start_line);
            in_const = 0;
        }
        next;
    }
    in_const {
        block = block "\n" $0;
        if ($0 ~ /;/) {
            check_block(block, start_line);
            in_const = 0;
        }
    }
    function check_block(b, line) {
        if (b ~ /Stat:/ || b ~ /OPTION_/ || b ~ /\[SIGNALS\]/ || b ~ /ACTIVE ARCHETYPE/ || b ~ /FUNDAMENTAL RULE/ || b ~ /You are playing the role of/) {
            print "  Line " line ": const string contains prompt content marker"
            print "    " b
            violation = 1;
        }
    }
    END {
        if (violation == 1) {
            exit 1;
        }
    }
    ' "$file")

    if [[ $? -ne 0 ]]; then
        echo "FAIL: $file has hardcoded prompt content in const string:"
        echo "$output"
        has_violations=1
    fi
done

if [[ $has_violations -eq 1 ]]; then
    echo "Error: Prompt content gate failed. Found forbidden prompt content in C# const strings."
    exit 1
else
    echo "PASS: No forbidden const-string prompt content found."
    exit 0
fi
