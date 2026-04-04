#!/usr/bin/env python3
"""Extract structured YAML rules from a Pinder design markdown file.

Blocks (paragraphs, tables, code blocks, blockquotes, flavor text, horizontal
rules) are stored as an ordered list so that generate.py can reproduce them in
the original document order.
"""

import sys
import re
import yaml


def slugify(text):
    """Convert heading text to a URL-friendly slug."""
    text = text.lower().strip()
    text = re.sub(r'[^\w\s-]', '', text)
    text = re.sub(r'[\s_]+', '-', text)
    text = re.sub(r'-+', '-', text)
    return text.strip('-')


def guess_type(title, blocks):
    """Guess rule type from content."""
    title_lower = title.lower()
    desc_lower = ''
    has_table = False
    has_code = False
    for b in blocks:
        if b['kind'] == 'paragraph':
            desc_lower += ' ' + b['text'].lower()
        elif b['kind'] == 'table':
            has_table = True
        elif b['kind'] == 'code':
            has_code = True

    if has_table:
        return 'table'
    if has_code:
        return 'template'

    keywords = {
        'interest_change': ['interest', 'xp', 'gain', 'lose', 'reward'],
        'shadow_growth': ['shadow', 'madness', 'horniness', 'denial', 'fixation', 'dread', 'overthinking'],
        'roll_modifier': ['roll', 'modifier', 'bonus', 'dc', 'check', 'dice', 'd20'],
        'trap_activation': ['trap', 'cringe', 'creep', 'overshare', 'spiral', 'dry spell'],
        'state_change': ['state', 'phase', 'transition', 'ghost', 'unmatch', 'date'],
        'definition': ['how', 'what', 'structure', 'overview', 'parameter'],
        'narrative': ['story', 'flavor', 'theme', 'design', 'philosophy'],
    }

    combined = title_lower + ' ' + desc_lower
    for rule_type, kws in keywords.items():
        if any(kw in combined for kw in kws):
            return rule_type

    return 'definition'


def parse_table(lines):
    """Parse a markdown table into list of dicts, separator cells, and header padding."""
    if len(lines) < 2:
        return [], [], '\n'.join(lines)

    header_line = lines[0]
    if '|' not in header_line:
        return [], [], '\n'.join(lines)

    headers = [h.strip() for h in header_line.strip('|').split('|')]
    headers = [h for h in headers if h or True]  # keep potentially empty headers

    if not headers:
        return [], [], '\n'.join(lines)

    # Store raw separator cells to preserve alignment markers and widths
    sep_line = lines[1] if len(lines) > 1 else ''
    sep_cells = []
    if '|' in sep_line:
        sep_cells = [c for c in sep_line.strip('|').split('|')]

    rows = []
    for line in lines[2:]:
        if '|' not in line:
            continue
        cells = [c.strip() for c in line.strip('|').split('|')]
        while len(cells) < len(headers):
            cells.append('')
        row = {}
        for i, h in enumerate(headers):
            if i < len(cells):
                row[h] = cells[i]
        rows.append(row)

    return rows, sep_cells, None


def extract_rules(filepath):
    """Parse markdown file into structured rule entries with ordered blocks."""
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    lines = content.split('\n')
    rules = []

    current_h1 = ''
    section_counter = 0
    current_rule = None

    in_code_block = False
    code_block_lines = []
    code_block_lang = ''
    in_table = False
    table_lines = []

    def _blocks(rule):
        """Get or create the blocks list for a rule."""
        if 'blocks' not in rule:
            rule['blocks'] = []
        return rule['blocks']

    def flush_table():
        nonlocal in_table, table_lines, current_rule
        if table_lines and current_rule:
            rows, sep_cells, raw = parse_table(table_lines)
            if rows:
                block = {'kind': 'table', 'rows': rows}
                if sep_cells:
                    block['sep_cells'] = sep_cells
                _blocks(current_rule).append(block)
            elif raw:
                _blocks(current_rule).append({'kind': 'paragraph', 'text': raw})
        table_lines = []
        in_table = False

    def flush_code():
        nonlocal in_code_block, code_block_lines, code_block_lang, current_rule
        if code_block_lines and current_rule:
            block_text = '\n'.join(code_block_lines)
            lang_prefix = "```{}\n".format(code_block_lang) if code_block_lang else "```\n"
            full_block = lang_prefix + block_text + "\n```"
            _blocks(current_rule).append({'kind': 'code', 'text': full_block})
        code_block_lines = []
        code_block_lang = ''
        in_code_block = False

    def finalize_rule(rule):
        if rule is None:
            return
        # Guess type from blocks
        blocks = rule.get('blocks', [])
        rule['type'] = guess_type(rule['title'], blocks)
        # Set description from first paragraph block (for backward compat / search)
        first_para = next((b for b in blocks if b['kind'] == 'paragraph'), None)
        if first_para:
            rule['description'] = first_para['text']
        else:
            rule['description'] = ''
        # Clean up empty fields
        for key in list(rule.keys()):
            if rule[key] is None or rule[key] == '' or rule[key] == []:
                del rule[key]
        # Ensure required fields
        if 'description' not in rule:
            rule['description'] = ''
        rules.append(rule)

    i = 0
    while i < len(lines):
        line = lines[i]

        # Code block toggle
        if line.strip().startswith('```'):
            if in_code_block:
                flush_code()
                i += 1
                continue
            else:
                flush_table()
                in_code_block = True
                code_block_lang = line.strip()[3:].strip()
                code_block_lines = []
                i += 1
                continue

        if in_code_block:
            code_block_lines.append(line)
            i += 1
            continue

        # Table detection — only lines that start with '|' are table rows
        if line.strip().startswith('|') and not line.strip().startswith('>'):
            if not in_table:
                flush_table()
                in_table = True
            table_lines.append(line)
            i += 1
            continue
        else:
            if in_table:
                flush_table()

        # Heading detection
        heading_match = re.match(r'^(#{1,6})\s+(.*)', line)
        if heading_match:
            flush_table()
            level = len(heading_match.group(1))
            title = heading_match.group(2).strip()

            # Count blank lines between heading and first content
            blank_count = 0
            j = i + 1
            while j < len(lines) and not lines[j].strip():
                blank_count += 1
                j += 1

            if level == 1:
                finalize_rule(current_rule)
                current_h1 = title
                section_counter = 0
                slug = slugify(title)
                current_rule = {
                    'id': '§0.{}'.format(slug),
                    'section': '§0',
                    'title': title,
                    'type': 'definition',
                    '_heading_level': level,
                    '_blank_after': blank_count,
                }
            elif level == 2:
                finalize_rule(current_rule)
                section_counter += 1
                slug = slugify(title)
                num_match = re.match(r'^(\d+)[\.\s]', title)
                if num_match:
                    sec = num_match.group(1)
                else:
                    sec = str(section_counter)
                current_rule = {
                    'id': '§{}.{}'.format(sec, slug),
                    'section': '§{}'.format(sec),
                    'title': title,
                    'type': 'definition',
                    '_heading_level': level,
                    '_blank_after': blank_count,
                }
            elif level >= 3:
                finalize_rule(current_rule)
                slug = slugify(title)
                parent_sec = '§{}'.format(section_counter) if section_counter else '§0'
                current_rule = {
                    'id': '{}.{}'.format(parent_sec, slug),
                    'section': parent_sec,
                    'title': title,
                    'type': 'definition',
                    '_heading_level': level,
                    '_blank_after': blank_count,
                }

            i += 1
            continue

        # Horizontal rule
        if re.match(r'^-{3,}$', line.strip()) or re.match(r'^\*{3,}$', line.strip()):
            if current_rule is not None:
                _blocks(current_rule).append({'kind': 'hr'})
            i += 1
            continue

        # Empty line — track consecutive blanks to preserve multi-blank gaps
        if not line.strip():
            blank_count = 1
            j = i + 1
            while j < len(lines) and not lines[j].strip():
                blank_count += 1
                j += 1
            if blank_count > 1 and current_rule is not None:
                _blocks(current_rule).append({'kind': 'blank_lines', 'count': blank_count})
            i += blank_count
            continue

        # No current rule yet — create a preamble entry
        if current_rule is None:
            current_rule = {
                'id': '§0.preamble',
                'section': '§0',
                'title': 'Preamble',
                'type': 'definition',
                '_heading_level': 0,
            }

        # Blockquote
        if line.strip().startswith('>'):
            quote_lines = []
            while i < len(lines) and lines[i].strip().startswith('>'):
                raw = lines[i]
                # Strip leading whitespace, then the '>' marker, then at most one space
                content = raw.lstrip()
                if content.startswith('> '):
                    content = content[2:]
                elif content.startswith('>'):
                    content = content[1:]
                quote_lines.append(content)
                i += 1
            quote_text = '\n'.join(quote_lines)
            _blocks(current_rule).append({'kind': 'blockquote', 'text': quote_text})
            continue

        # Check for italic-only line (flavor text)
        stripped = line.strip()
        if (stripped.startswith('*') and stripped.endswith('*') and not stripped.startswith('**')) or \
           (stripped.startswith('_') and stripped.endswith('_')):
            flavor = stripped.strip('*_').strip()
            _blocks(current_rule).append({'kind': 'flavor', 'text': flavor})
            i += 1
            continue

        # Regular paragraph / list item — accumulate consecutive non-empty, non-special lines
        para_lines = []
        while i < len(lines):
            l = lines[i]
            if not l.strip():
                break
            if l.strip().startswith('#'):
                break
            if l.strip().startswith('```'):
                break
            if l.strip().startswith('>'):
                break
            if '|' in l and l.strip().startswith('|'):
                break
            # Check for horizontal rule
            if re.match(r'^-{3,}$', l.strip()) or re.match(r'^\*{3,}$', l.strip()):
                break
            para_lines.append(l)
            i += 1

        para_text = '\n'.join(para_lines)
        if para_text.strip():
            _blocks(current_rule).append({'kind': 'paragraph', 'text': para_text})

        continue

    # Finalize last rule
    flush_table()
    flush_code()
    finalize_rule(current_rule)

    # Expose heading level and blank_after as public fields
    for rule in rules:
        hl = rule.pop('_heading_level', None)
        if hl and hl > 0:
            rule['heading_level'] = hl
        ba = rule.pop('_blank_after', None)
        if ba is not None and ba == 0:
            rule['compact_heading'] = True

    return rules


def main():
    if len(sys.argv) < 2:
        print("Usage: extract.py <markdown_file>", file=sys.stderr)
        sys.exit(1)

    filepath = sys.argv[1]
    rules = extract_rules(filepath)

    yaml.dump(rules, sys.stdout, default_flow_style=False, allow_unicode=True, sort_keys=False, width=200)


if __name__ == '__main__':
    main()
