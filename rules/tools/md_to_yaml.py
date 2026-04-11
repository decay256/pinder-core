#!/usr/bin/env python3
"""Convert rules Markdown back to structured YAML.

Parses the Markdown format produced by generate.py / yaml_to_md.py and
reconstructs the YAML rule list.  Enrichment-only fields (type, formula,
condition, outcome, etc.) cannot be recovered from Markdown and are omitted.

Usage:
    python3 rules/tools/md_to_yaml.py [input.md] [output.yaml]

Defaults:
    input  = rules/regenerated/rules-v3.md
    output = stdout
"""
import re
import sys
import yaml
from pathlib import Path


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def _slugify(text: str) -> str:
    """Convert a heading title to a slug for the id field."""
    s = text.lower().strip()
    s = re.sub(r'[^a-z0-9\s-]', '', s)
    s = re.sub(r'[\s]+', '-', s)
    return s.strip('-')


def _infer_section(title: str, heading_level: int, prev_section: str) -> str:
    """Infer the §N section tag from the heading."""
    if heading_level == 1:
        return '§0'
    m = re.match(r'^(\d+)\.', title)
    if m:
        return f'§{m.group(1)}'
    # Sub-headings inherit parent section
    return prev_section


def _parse_table(lines: list[str]) -> dict:
    """Parse markdown table lines into a block dict with rows and sep_cells."""
    if len(lines) < 2:
        return {}

    def split_row(line: str) -> list[str]:
        """Split a pipe-delimited row, stripping outer pipes."""
        line = line.strip()
        if line.startswith('|'):
            line = line[1:]
        if line.endswith('|'):
            line = line[:-1]
        return [c.strip() for c in line.split('|')]

    headers = split_row(lines[0])

    # Separator row — preserve raw cells for faithful round-trip
    sep_line = lines[1].strip()
    if sep_line.startswith('|'):
        sep_line = sep_line[1:]
    if sep_line.endswith('|'):
        sep_line = sep_line[:-1]
    sep_cells = sep_line.split('|')

    rows = []
    for row_line in lines[2:]:
        cells = split_row(row_line)
        row = {}
        for i, h in enumerate(headers):
            row[h] = cells[i] if i < len(cells) else ''
        rows.append(row)

    block: dict = {'kind': 'table', 'rows': rows}
    if sep_cells:
        block['sep_cells'] = sep_cells
    return block


# ---------------------------------------------------------------------------
# Main parser
# ---------------------------------------------------------------------------

def md_to_rules(md_text: str) -> list[dict]:
    """Parse Markdown text into a list of rule dicts."""
    lines = md_text.split('\n')
    rules: list[dict] = []
    current_rule: dict | None = None
    current_section = '§0'

    i = 0
    while i < len(lines):
        line = lines[i]

        # --- Heading ---
        heading_match = re.match(r'^(#{1,6})\s+(.*)', line)
        if heading_match:
            level = len(heading_match.group(1))
            title = heading_match.group(2).strip()

            # Finalise previous rule
            if current_rule is not None:
                _finalise_rule(current_rule)
                rules.append(current_rule)

            current_section = _infer_section(title, level, current_section)
            slug = _slugify(title)
            rule_id = f'{current_section}.{slug}'

            current_rule = {
                'id': rule_id,
                'section': current_section,
                'title': title,
                'heading_level': level,
                'blocks': [],
            }

            # Check compact heading: if next line is not blank
            if i + 1 < len(lines) and lines[i + 1].strip() != '':
                current_rule['compact_heading'] = True

            i += 1
            continue

        # Everything below requires an active rule
        if current_rule is None:
            i += 1
            continue

        blocks = current_rule['blocks']

        # --- Horizontal rule ---
        if re.match(r'^-{3,}\s*$', line):
            blocks.append({'kind': 'hr'})
            i += 1
            continue

        # --- Code block ---
        if line.startswith('```'):
            code_lines = [line]
            i += 1
            while i < len(lines):
                code_lines.append(lines[i])
                if lines[i].startswith('```') and len(code_lines) > 1:
                    i += 1
                    break
                i += 1
            blocks.append({'kind': 'code', 'text': '\n'.join(code_lines)})
            continue

        # --- Table ---
        if '|' in line and i + 1 < len(lines) and re.match(r'^\|[-:\s|]+\|$', lines[i + 1].strip()):
            table_lines = []
            while i < len(lines) and '|' in lines[i] and lines[i].strip():
                table_lines.append(lines[i])
                i += 1
            tbl = _parse_table(table_lines)
            if tbl:
                blocks.append(tbl)
            continue

        # --- Blockquote ---
        if line.startswith('>'):
            bq_lines = []
            while i < len(lines) and (lines[i].startswith('>') or (lines[i].strip() == '' and i + 1 < len(lines) and lines[i + 1].startswith('>'))):
                raw = lines[i]
                if raw.startswith('> '):
                    bq_lines.append(raw[2:])
                elif raw == '>':
                    bq_lines.append('')
                else:
                    bq_lines.append(raw)
                i += 1
            blocks.append({'kind': 'blockquote', 'text': '\n'.join(bq_lines)})
            continue

        # --- Blank line ---
        if line.strip() == '':
            # Count consecutive blank lines
            count = 0
            while i < len(lines) and lines[i].strip() == '':
                count += 1
                i += 1
            # Extra blank lines beyond the normal paragraph separator (1)
            if count > 1:
                blocks.append({'kind': 'blank_lines', 'count': count})
            continue

        # --- Flavor text (italic-wrapped full line) ---
        # Flavor: line starts with * and ends with * (not bold **)
        if re.match(r'^\*[^*].*[^*]\*$', line):
            flavor_lines = []
            while i < len(lines) and re.match(r'^\*[^*].*[^*]\*$', lines[i]):
                # Strip surrounding * 
                flavor_lines.append(lines[i][1:-1])
                i += 1
            blocks.append({'kind': 'flavor', 'text': '\n'.join(flavor_lines)})
            continue

        # --- Paragraph (default) ---
        para_lines = []
        while i < len(lines) and lines[i].strip() != '' and not lines[i].startswith('#') and not lines[i].startswith('```') and not re.match(r'^-{3,}\s*$', lines[i]) and not lines[i].startswith('>'):
            # Check if this starts a table
            if '|' in lines[i] and i + 1 < len(lines) and re.match(r'^\|[-:\s|]+\|$', lines[i + 1].strip()):
                break
            para_lines.append(lines[i])
            i += 1
        if para_lines:
            blocks.append({'kind': 'paragraph', 'text': '\n'.join(para_lines)})

    # Finalise last rule
    if current_rule is not None:
        _finalise_rule(current_rule)
        rules.append(current_rule)

    return rules


def _finalise_rule(rule: dict):
    """Clean up a rule before appending: strip trailing hr, set description."""
    blocks = rule.get('blocks', [])

    # Strip trailing empty blocks
    while blocks and blocks[-1].get('kind') == 'blank_lines':
        blocks.pop()

    # Build description from first paragraph
    for b in blocks:
        if b.get('kind') == 'paragraph':
            rule['description'] = b['text']
            break
    else:
        rule['description'] = ''


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------

def main():
    root = Path(__file__).parent.parent.parent
    md_path = sys.argv[1] if len(sys.argv) > 1 else str(
        root / 'rules' / 'regenerated' / 'rules-v3.md')
    out_path = sys.argv[2] if len(sys.argv) > 2 else None

    md_text = Path(md_path).read_text(encoding='utf-8')
    rules = md_to_rules(md_text)

    output = yaml.dump(rules, default_flow_style=False, allow_unicode=True,
                       sort_keys=False, width=200)

    if out_path and out_path != '-':
        Path(out_path).write_text(output, encoding='utf-8')
        print(f"Written: {out_path} ({len(rules)} rules)", file=sys.stderr)
    else:
        print(output)


if __name__ == '__main__':
    main()
