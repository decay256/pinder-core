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

    prompt_path = Path(__file__).parent / 'prompts' / 'diff_classifier.txt'
    try:
        with open(prompt_path, 'r', encoding='utf-8') as f:
            prompt_template = f.read()
        prompt = prompt_template.format(diff_text=diff_text)
    except FileNotFoundError:
        # Fallback just in case pathing differs, though it should be packaged
        raise FileNotFoundError(f"Diff classification prompt file not found at {prompt_path}")

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
