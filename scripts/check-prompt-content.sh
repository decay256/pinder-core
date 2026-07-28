#!/bin/bash
# ==============================================================================
# Prompt Content Gate - scripts/check-prompt-content.sh
#
# This script blocks the reintroduction of hardcoded model-facing prompt content
# in C# strings under src/. Prompt prose belongs in data/prompts/*.yaml or other
# runtime configuration that remains admin-editable and source-attributed.
#
# Allowlist mechanism (one relative path per line, comments/empty lines allowed):
# --- ALLOWLIST START ---
# src/Pinder.SessionSetup/LlmStakeGenerator.cs
# src/Pinder.SessionSetup/LlmOutfitDescriber.cs
# src/Pinder.Core/Conversation/PlayerResponseDelayEvaluator.cs
# src/Pinder.LlmAdapters/GameDefinition.Defaults.cs
# src/Pinder.NarrativeHarness/HarnessRunner.cs
# src/Pinder.NarrativeHarness/PursuerActor.cs
# --- ALLOWLIST END ---
#
# The allowlist is intentionally narrow: it covers historical prompt-prose debt
# outside the scope of #1336. New director/performance prompt prose must be
# catalog-backed; code may still contain prompt keys, diagnostics, logging labels,
# provider phase labels, and player-visible UI copy.
#
# Existing protocol sentinels that are parsed/rendered by engine contracts
# ([SIGNALS], OPTION_N, ACTIVE ARCHETYPE) are exempted below at exact file/text
# granularity. They are structural labels, not reusable natural-language prompt
# prose, and #1336 explicitly avoids bulk-migrating historical strings.
# ==============================================================================

set -u

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$REPO_ROOT"

python_cmd=()
probe_python() {
    "$@" <<'PY' >/dev/null 2>&1
print("ok")
PY
}

for candidate in python3 python; do
    if command -v "$candidate" >/dev/null 2>&1 && probe_python "$candidate"; then
        python_cmd=("$candidate")
        break
    fi
done

if [[ ${#python_cmd[@]} -eq 0 ]] && command -v py >/dev/null 2>&1 && probe_python py -3; then
    python_cmd=(py -3)
fi

if [[ ${#python_cmd[@]} -eq 0 ]]; then
    echo "Error: Prompt content gate requires python3 or python."
    exit 1
fi

echo "Parsed allowlist:"
while read -r line; do
    if [[ "$line" =~ ^#[[:space:]]+src/ ]]; then
        clean_line=$(echo "$line" | sed -E 's/^#[[:space:]]*//' | xargs)
        echo "  - $clean_line"
    fi
done < "$0"
echo ""

PROMPT_CONTENT_GATE_SCRIPT="$0" "${python_cmd[@]}" <<'PY'
from __future__ import annotations

import os
import re
import sys
from dataclasses import dataclass
from pathlib import Path


REPO_ROOT = Path.cwd()
SCRIPT_PATH = Path(os.environ["PROMPT_CONTENT_GATE_SCRIPT"])


LEGACY_PROMPT_MARKERS = (
    "Stat:",
    "OPTION_",
    "[SIGNALS]",
    "ACTIVE ARCHETYPE",
    "FUNDAMENTAL RULE",
    "You are playing the role of",
)

STRUCTURAL_SENTINEL_EXEMPTIONS = {
    "src/Pinder.Core/Characters/ActiveArchetype.cs": (
        "ACTIVE ARCHETYPE:",
    ),
    "src/Pinder.Core/Conversation/SuccessImprovementValidator.cs": (
        "OPTION_",
    ),
    "src/Pinder.LlmAdapters/Anthropic/DateeResponseParsers.cs": (
        "[SIGNALS]",
    ),
    "src/Pinder.LlmAdapters/GmOutputContract.cs": (
        "[SIGNALS]",
    ),
    "src/Pinder.LlmAdapters/SessionDocumentBuilder.Trace.cs": (
        "OPTION_{i}",
        "OPTION_{i + 1}: [message]",
    ),
}

# Existing prompt prose outside #1336 is exempted only at exact file/text
# granularity so diagnostic-looking neighbors cannot hide new directives.
LEGACY_EXACT_PROMPT_EXEMPTIONS = {
    "src/Pinder.LlmAdapters/SessionDocumentBuilder.Trace.cs": (
        "Do not exceed {ceiling} characters regardless of your texting style.",
    ),
}

DIRECTIVE_PATTERNS = (
    r"\bYou are\b",
    r"\bProduce\b",
    r"\bWrite a reply\b",
    r"\bGiven the diagnosis\b",
    r"\bReturn only\b",
    r"\bUse this\b",
    r"\bDo not\b",
    r"\bNever\b",
    r"\bTreat\b",
    r"\bInterpret\b",
    r"\bLet\b",
    r"\bThe recipient\b",
    r"\bPrivate emotional\b",
    r"\bDATEE EMOTIONAL\b",
    r"\bPrimary emotion\b",
    r"\bResponse posture\b",
)

MODEL_CONTEXT_RE = re.compile(
    r"(system|user|datee|director|performance|compiled|emotional|prompt|instruction|template|wrapper|llm|model)"
    r".{0,120}"
    r"(prompt|message|content|instruction|template|wrapper|direction|response)",
    re.IGNORECASE | re.DOTALL,
)

EXEMPT_ASSIGNMENT_RE = re.compile(
    r"\b(?:Diagnostic|CorrelationHints|Phase|Reason|Provider|Source|Key|Label|"
    r"ParserName|Error|PlayerVisible|VisibleCopy|Display|Ui|Button|Title)\w*\s*=\s*$",
    re.IGNORECASE,
)

EXEMPT_NAMED_ARGUMENT_RE = re.compile(
    r"\b(?:message|diagnostic|phase|reason|provider|source|key|label|parserName|"
    r"error|playerVisible|visibleCopy|display|ui|button|title)\s*:\s*$",
    re.IGNORECASE,
)

EXEMPT_CALL_RE = re.compile(
    r"\b(?:throw\s+new\s+\w*Exception|[\w.]*?(?:Log|Trace|Assert|Display|RenderForUi)\w*)"
    r"\s*\([^;{}]*$",
    re.IGNORECASE | re.DOTALL,
)

MODEL_SINK_CALL_RE = re.compile(
    r"\b(?:SendAsync|SendStructuredAsync|SendWithDiagnosticsAsync)\s*\([^;{}]*$",
    re.IGNORECASE | re.DOTALL,
)

KEY_OR_LABEL_RE = re.compile(r"^[a-z0-9][a-z0-9_.:/-]*$", re.IGNORECASE)
PLACEHOLDER_ONLY_RE = re.compile(r"^[A-Z0-9_{} .:/-]+$")
WORD_RE = re.compile(r"[A-Za-z]{3,}")


@dataclass(frozen=True)
class StringLiteral:
    line: int
    text: str
    prefix: str
    raw_source: str
    start: int


def load_allowlist() -> set[str]:
    allowed: set[str] = set()
    for line in SCRIPT_PATH.read_text(encoding="utf-8").splitlines():
        if re.match(r"^#\s+src/", line):
            allowed.add(re.sub(r"^#\s*", "", line).strip().replace("\\", "/"))
    return allowed


def iter_csharp_files() -> list[Path]:
    src = REPO_ROOT / "src"
    return sorted(src.rglob("*.cs")) if src.exists() else []


def update_line_count(text: str, start: int, end: int, line: int) -> int:
    return line + text.count("\n", start, end)


def parse_string_literals(source: str) -> list[StringLiteral]:
    literals: list[StringLiteral] = []
    i = 0
    line = 1
    length = len(source)

    while i < length:
        ch = source[i]

        if ch == "\n":
            line += 1
            i += 1
            continue

        if source.startswith("//", i):
            end = source.find("\n", i + 2)
            if end < 0:
                break
            i = end
            continue

        if source.startswith("/*", i):
            end = source.find("*/", i + 2)
            if end < 0:
                break
            line = update_line_count(source, i, end + 2, line)
            i = end + 2
            continue

        prefix_start = i
        prefix_chars = ""
        while i < length and source[i] in "$@":
            prefix_chars += source[i]
            i += 1

        if i < length and source[i] == '"':
            start_line = line
            if source.startswith('"""', i):
                quote_count = 0
                while i + quote_count < length and source[i + quote_count] == '"':
                    quote_count += 1
                delimiter = '"' * quote_count
                content_start = i + quote_count
                end = source.find(delimiter, content_start)
                if end < 0:
                    break
                text = source[content_start:end]
                raw = source[prefix_start : end + quote_count]
                literals.append(StringLiteral(start_line, text, prefix_chars, raw, prefix_start))
                line = update_line_count(source, prefix_start, end + quote_count, line)
                i = end + quote_count
                continue

            i += 1
            content: list[str] = []
            if "@" in prefix_chars:
                while i < length:
                    if source[i] == '"':
                        if i + 1 < length and source[i + 1] == '"':
                            content.append('"')
                            i += 2
                            continue
                        i += 1
                        break
                    content.append(source[i])
                    if source[i] == "\n":
                        line += 1
                    i += 1
            else:
                while i < length:
                    if source[i] == "\\" and i + 1 < length:
                        content.append(source[i])
                        content.append(source[i + 1])
                        i += 2
                        continue
                    if source[i] == '"':
                        i += 1
                        break
                    content.append(source[i])
                    if source[i] == "\n":
                        line += 1
                    i += 1

            literals.append(
                StringLiteral(
                    start_line,
                    "".join(content),
                    prefix_chars,
                    source[prefix_start:i],
                    prefix_start,
                )
            )
            continue

        i = prefix_start + 1

    return literals


def line_window(lines: list[str], line: int, before: int = 6, after: int = 2) -> str:
    start = max(0, line - before - 1)
    end = min(len(lines), line + after)
    return "\n".join(lines[start:end])


def candidate_purpose(source: str, literal: StringLiteral) -> str:
    statement_start = max(
        source.rfind(";", 0, literal.start),
        source.rfind("{", 0, literal.start),
        source.rfind("}", 0, literal.start),
    )
    return source[statement_start + 1 : literal.start]


def has_exempt_purpose(purpose: str) -> bool:
    return any(
        pattern.search(purpose)
        for pattern in (EXEMPT_ASSIGNMENT_RE, EXEMPT_NAMED_ARGUMENT_RE, EXEMPT_CALL_RE)
    )


def is_prompt_key_or_label(text: str) -> bool:
    stripped = text.strip()
    if not stripped:
        return True
    if KEY_OR_LABEL_RE.match(stripped):
        return True
    if PLACEHOLDER_ONLY_RE.match(stripped) and len(WORD_RE.findall(stripped)) <= 3:
        return True
    return False


def has_directive_prose(text: str) -> bool:
    stripped = " ".join(text.strip().split())
    if len(stripped) < 42:
        return False
    if len(WORD_RE.findall(stripped)) < 6:
        return False
    return any(re.search(pattern, stripped, re.IGNORECASE) for pattern in DIRECTIVE_PATTERNS)


def is_structural_sentinel_exemption(rel: str, text: str) -> bool:
    allowed = STRUCTURAL_SENTINEL_EXEMPTIONS.get(rel)
    if not allowed:
        return False
    return any(text == marker or text.startswith(marker) for marker in allowed)


def is_legacy_exact_prompt_exemption(rel: str, text: str) -> bool:
    allowed = LEGACY_EXACT_PROMPT_EXEMPTIONS.get(rel)
    return bool(allowed and text in allowed)


def classify_literal(
    rel: str,
    literal: StringLiteral,
    context: str,
    purpose: str,
) -> str | None:
    text = literal.text.strip()
    if not text:
        return None

    if is_structural_sentinel_exemption(rel, text):
        return None

    if is_legacy_exact_prompt_exemption(rel, text):
        return None

    if any(marker in text for marker in LEGACY_PROMPT_MARKERS):
        return "const/legacy prompt marker"

    if is_prompt_key_or_label(text):
        return None

    if has_directive_prose(text) and MODEL_SINK_CALL_RE.search(purpose):
        form = "interpolated" if "$" in literal.prefix else "ordinary"
        return f"{form} model-facing prompt prose passed directly to model transport"

    if has_exempt_purpose(purpose):
        return None

    if not has_directive_prose(text):
        return None

    if MODEL_CONTEXT_RE.search(context):
        form = "interpolated" if "$" in literal.prefix else "ordinary"
        return f"{form} model-facing prompt prose"

    return None


def main() -> int:
    allowlist = load_allowlist()
    violations: list[tuple[str, int, str, str]] = []

    for path in iter_csharp_files():
        rel = path.relative_to(REPO_ROOT).as_posix()
        if rel in allowlist:
            continue

        source = path.read_text(encoding="utf-8-sig")
        lines = source.splitlines()
        for literal in parse_string_literals(source):
            context = line_window(lines, literal.line)
            purpose = candidate_purpose(source, literal)
            reason = classify_literal(rel, literal, context, purpose)
            if reason is None:
                continue
            sample = " ".join(literal.text.strip().split())
            if len(sample) > 180:
                sample = sample[:177] + "..."
            violations.append((rel, literal.line, reason, sample))

    if violations:
        by_file: dict[str, list[tuple[int, str, str]]] = {}
        for rel, line, reason, sample in violations:
            by_file.setdefault(rel, []).append((line, reason, sample))

        for rel, items in by_file.items():
            print(f"FAIL: {rel} has hardcoded model-facing prompt content:")
            for line, reason, sample in items:
                print(f"  Line {line}: {reason}")
                print(f"    {sample}")
        print("Error: Prompt content gate failed. Move reusable model-facing prose to data/prompts/*.yaml or document a narrow exemption.")
        return 1

    print("PASS: No forbidden hardcoded model-facing prompt content found.")
    return 0


raise SystemExit(main())
PY
