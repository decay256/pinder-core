#!/usr/bin/env python3
"""LLM-based diff classification for round-trip checks.

Runs the round-trip, collects diffs, sends them to Claude for classification.
Prints a single verdict line:
  FORMATTING_ONLY
  CONTENT_LOSS: <description>
  SKIP: no API key

Exit code is always 0 — the calling test handles the assertion.

Usage:
    python3 rules/tools/rules_pipeline.py check-diff
"""
import json
import os
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from _round_trip_check import run_check


def classify_diffs(diffs_found: list[tuple]) -> str:
    """Send diffs to Claude and return verdict string."""
    api_key = os.environ.get('ANTHROPIC_API_KEY', '')
    if not api_key:
        return 'SKIP: no API key'

    # Build diff text
    parts = []
    for title, diff_lines, count in diffs_found:
        parts.append(f"### [{title}] ({count} changed lines)")
        parts.append(''.join(diff_lines))

    diff_text = '\n'.join(parts)
    if len(diff_text) > 30000:
        diff_text = diff_text[:30000] + '\n\n... (truncated)'

    prompt = f"""You are reviewing a YAML round-trip diff from a rules pipeline.
The pipeline converts YAML → Markdown → YAML. Many differences are expected
because the round-trip re-derives structural fields from content. You must
distinguish real data loss from harmless structural/formatting artifacts.

EXPECTED (not content loss — classify as FORMATTING_ONLY):
- Whitespace/formatting changes (padding, blank lines, trailing spaces)
- YAML quoting style changes (single vs double quotes, block vs flow)
- Key reordering within a dict
- Line wrapping differences in long strings
- Separator row width normalization in markdown tables (sep_cells changing
  width, e.g. '-------' → '---' — these are column separator dashes)
- The 'description' field changing value — description is auto-generated
  from blocks, so its exact wording may differ after round-trip
- A 'blocks' field appearing in the round-trip that wasn't in the original
  (the parser always creates blocks from markdown content)
- Blockquote boundaries changing (two blockquotes merging, or a blockquote
  merging into adjacent paragraph content) when the same text is preserved
- Block kind changing (e.g. blockquote → paragraph) when text is preserved

REAL CONTENT LOSS (classify as CONTENT_LOSS):
- Actual rule text, values, or data is completely missing (not just moved
  to a different field like description → blocks or vice versa)
- Table rows or cells have different data values (not separator formatting)
- Entire paragraphs dropped with no equivalent text elsewhere in the rule
- Numeric values changed

Key principle: if the same text/data appears in the rule but in a different
field or with different structure, that is NOT content loss.

Classify the ENTIRE diff as one of exactly two categories.

If ALL hunks are formatting/structural/inference changes, respond with exactly:
FORMATTING_ONLY

If ANY hunk shows actual content loss (data gone, not just moved), respond with:
CONTENT_LOSS: <one-line summary of what was lost>

Respond with ONLY the single verdict line, nothing else.

Diffs:
{diff_text}"""

    request_body = {
        'model': 'claude-sonnet-4-20250514',
        'max_tokens': 200,
        'messages': [{'role': 'user', 'content': prompt}],
    }

    try:
        result = subprocess.run(
            ['curl', '-s', '-X', 'POST',
             'https://api.anthropic.com/v1/messages',
             '-H', f'x-api-key: {api_key}',
             '-H', 'anthropic-version: 2023-06-01',
             '-H', 'content-type: application/json',
             '-d', json.dumps(request_body)],
            capture_output=True, text=True, timeout=60
        )

        if result.returncode != 0:
            return f'SKIP: curl failed (exit {result.returncode})'

        response = json.loads(result.stdout)
        if 'content' in response and response['content']:
            text = response['content'][0].get('text', '').strip()
            # Normalize: extract the verdict line
            for line in text.splitlines():
                line = line.strip()
                if line.startswith('FORMATTING_ONLY'):
                    return 'FORMATTING_ONLY'
                if line.startswith('CONTENT_LOSS:'):
                    return line
            # If model responded but not in expected format, treat as skip
            return f'SKIP: unexpected LLM response: {text[:100]}'
        elif 'error' in response:
            msg = response['error'].get('message', str(response['error']))
            return f'SKIP: API error: {msg[:100]}'
        else:
            return f'SKIP: unexpected response format'

    except subprocess.TimeoutExpired:
        return 'SKIP: API call timed out'
    except (json.JSONDecodeError, KeyError) as e:
        return f'SKIP: parse error: {e}'
    except FileNotFoundError:
        return 'SKIP: curl not found'


def main():
    total_diff, diffs_found, missing_count = run_check()

    if missing_count > 0:
        print(f'CONTENT_LOSS: {missing_count} rules missing entirely from round-trip')
        return 0

    if total_diff == 0:
        print('FORMATTING_ONLY')
        return 0

    verdict = classify_diffs(diffs_found)
    print(verdict)
    return 0


if __name__ == '__main__':
    sys.exit(main())
