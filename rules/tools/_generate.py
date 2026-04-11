#!/usr/bin/env python3
"""Generate markdown from structured YAML rules.

Reads the ordered ``blocks`` list produced by extract.py so that paragraphs,
tables, code blocks, blockquotes, flavor text, and horizontal rules appear in
their original document order.  Table column widths stored by extract.py are
used to reproduce the original separator row.
"""

import sys
import yaml


def generate_table(table_block):
    """Generate a markdown table from a block dict with rows and optional sep_cells.
    
    Reproduces the separator row faithfully (preserving alignment markers and
    column widths).  When the original separator had space-padded cells (e.g.
    ``| ------------- |``), header and data cells are right-padded to match.
    """
    rows = table_block.get('rows', [])
    if not rows:
        return ''

    headers = list(rows[0].keys())
    if not headers:
        return ''

    sep_cells = table_block.get('sep_cells', [])

    # Detect whether the original table used padded cells.
    # A separator cell starting with a space indicates padded formatting.
    padded = False
    col_widths = []
    if sep_cells and len(sep_cells) == len(headers):
        padded = any(c.startswith(' ') for c in sep_cells)
        col_widths = [len(c) for c in sep_cells]

    def pad_cell(text, width):
        """Pad cell content to fill *width* characters between pipes."""
        if text == '':
            # Empty cell — use single space to match common markdown convention
            content = ' '
        else:
            content = ' {} '.format(text)
        if len(content) < width:
            content = content + ' ' * (width - len(content))
        return content

    lines = []

    if padded and col_widths:
        # Padded format — pad header + data cells to match separator widths
        hparts = []
        for i, h in enumerate(headers):
            hparts.append(pad_cell(h, col_widths[i]) if i < len(col_widths) else ' {} '.format(h))
        lines.append('|' + '|'.join(hparts) + '|')
    else:
        # Compact format — empty headers get single space
        hparts = []
        for h in headers:
            if h == '':
                hparts.append(' ')
            else:
                hparts.append(' {} '.format(h))
        lines.append('|' + '|'.join(hparts) + '|')

    # Separator — always use stored raw cells when available
    if sep_cells and len(sep_cells) == len(headers):
        lines.append('|' + '|'.join(sep_cells) + '|')
    else:
        lines.append('|' + '|'.join(['---' for _ in headers]) + '|')

    # Data rows
    for row in rows:
        cells = [str(row.get(h, '')) for h in headers]
        if padded and col_widths:
            dparts = []
            for i, c in enumerate(cells):
                dparts.append(pad_cell(c, col_widths[i]) if i < len(col_widths) else ' {} '.format(c))
            lines.append('|' + '|'.join(dparts) + '|')
        else:
            # Format each cell: empty cells get single space, others get ' text '
            cparts = []
            for c in cells:
                if c == '':
                    cparts.append(' ')
                else:
                    cparts.append(' {} '.format(c))
            lines.append('|' + '|'.join(cparts) + '|')

    return '\n'.join(lines)


def _generate_table_legacy(table_rows):
    """Fallback for rules that still use the flat table_rows field."""
    return generate_table({'rows': table_rows})


def render_blocks(blocks):
    """Render an ordered list of blocks to markdown fragments."""
    parts = []
    for block in blocks:
        kind = block.get('kind', 'paragraph')
        if kind == 'paragraph':
            parts.append(block['text'])
            parts.append('')
        elif kind == 'table':
            parts.append(generate_table(block))
            parts.append('')
        elif kind == 'code':
            parts.append(block['text'])
            parts.append('')
        elif kind == 'blockquote':
            for line in block['text'].split('\n'):
                if line.rstrip():
                    # Preserve trailing whitespace (e.g. markdown line breaks)
                    parts.append('> {}'.format(line))
                else:
                    parts.append('>')
            parts.append('')
        elif kind == 'flavor':
            for line in block['text'].split('\n'):
                parts.append('*{}*'.format(line))
            parts.append('')
        elif kind == 'hr':
            parts.append('---')
            parts.append('')
        elif kind == 'blank_lines':
            # Emit extra blank lines (the normal paragraph separator already
            # emits one blank line, so we only need count-1 additional blanks)
            extra = block.get('count', 2) - 1
            for _ in range(extra):
                parts.append('')
    return parts



def generate_archetype_definition(rule: dict, heading_level: int = 4) -> str:
    parts = []
    prefix = '#' * heading_level
    parts.append(f"{prefix} {rule['title']}")
    
    stats = rule.get('stats', {})
    shadows = rule.get('shadows', {})
    
    high_stats = stats.get('high', [])
    low_stats = stats.get('low', [])
    stats_parts = []
    if high_stats:
        stats_parts.append(f"High {', '.join(high_stats)}")
    if low_stats:
        stats_parts.append(f"Low {', '.join(low_stats)}")
    stats_str = " | ".join(stats_parts) if stats_parts else "—"

    high_shadows = shadows.get('high', [])
    if high_shadows:
        shadow_str = f"High {', '.join(high_shadows)}"
    else:
        shadow_str = "None notable"
        
    parts.append(f"**Stats:** {stats_str} | **Shadow:** {shadow_str}  ")
    
    lr = rule.get('level_range', [])
    if lr:
        if lr[1] == 99:
            parts.append(f"**Level range:** {lr[0]}+")
        else:
            parts.append(f"**Level range:** {lr[0]}–{lr[1]}")
    else:
        parts.append(f"**Level range:** Unknown")
    parts.append("")
    
    if rule.get('behavior'):
        parts.append(rule['behavior'])
        parts.append("")
        
    interference = rule.get('interference', {})
    if interference:
        parts.append("**Interference:**")
        for k, v in interference.items():
            parts.append(f"* {k}: {v}")
        parts.append("")
        
    if rule.get('has_hr'):
        parts.append('---')
        parts.append('')
    return '\n'.join(parts)


def rule_to_markdown(rule, heading_level=2):
    """Convert a single rule entry to markdown."""
    if rule.get('type') == 'archetype_definition':
        return generate_archetype_definition(rule, heading_level)

    parts = []

    # Title as heading
    prefix = '#' * heading_level
    parts.append('{} {}'.format(prefix, rule['title']))

    # Add blank line after heading unless original had none (compact_heading)
    if not rule.get('compact_heading', False):
        parts.append('')

    # If 'blocks' list is present, render in order
    if 'blocks' in rule:
        parts.extend(render_blocks(rule['blocks']))
    else:
        # Legacy fallback: use individual fields
        if rule.get('description'):
            parts.append(rule['description'])
            parts.append('')

        if rule.get('flavor'):
            for line in rule['flavor'].split('\n'):
                parts.append('*{}*'.format(line))
            parts.append('')

        if rule.get('table_rows'):
            parts.append(_generate_table_legacy(rule['table_rows']))
            parts.append('')

        if rule.get('code_examples'):
            for block in rule['code_examples']:
                parts.append(block)
                parts.append('')

        if rule.get('designer_notes'):
            for line in rule['designer_notes'].split('\n'):
                if line.strip():
                    parts.append('> {}'.format(line))
                else:
                    parts.append('>')
            parts.append('')

        if rule.get('examples'):
            parts.append('**Examples:**')
            for ex in rule['examples']:
                parts.append('- {}'.format(ex))
            parts.append('')

        if rule.get('unstructured_prose'):
            parts.append(rule['unstructured_prose'])
            parts.append('')

    return '\n'.join(parts)


def generate_markdown(rules):
    """Generate full markdown document from list of rules."""
    parts = []

    for rule in rules:
        if 'heading_level' in rule:
            heading_level = rule['heading_level']
        else:
            section = rule.get('section', '§0')
            rule_id = rule.get('id', '')
            id_after_section = rule_id[len(section):] if rule_id.startswith(section) else rule_id
            dots = id_after_section.count('.')
            if section == '§0' and dots <= 1:
                heading_level = 1
            elif dots <= 1:
                heading_level = 2
            else:
                heading_level = min(2 + dots - 1, 6)

        md = rule_to_markdown(rule, heading_level)
        parts.append(md)

    result = '\n'.join(parts)
    # Strip trailing blank lines to avoid extra newline at end of document
    result = result.rstrip('\n')
    return result


def main():
    if len(sys.argv) < 2:
        print("Usage: generate.py <yaml_file>", file=sys.stderr)
        sys.exit(1)

    filepath = sys.argv[1]
    with open(filepath, 'r', encoding='utf-8') as f:
        rules = yaml.safe_load(f)

    if not rules:
        print("No rules found.", file=sys.stderr)
        sys.exit(1)

    output = generate_markdown(rules)
    print(output)


if __name__ == '__main__':
    main()
