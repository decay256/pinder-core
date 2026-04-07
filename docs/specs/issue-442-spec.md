**Module**: docs/modules/rules-dsl.md

## Overview
The Rules DSL pipeline establishes a bidirectional Markdown↔YAML conversion system for game design documents. It extracts authoritative Markdown rules into structured YAML format, enriches them with machine-readable conditions and outcomes, generates xUnit C# test stubs, and verifies the fidelity of the conversion by round-tripping YAML back to Markdown and comparing diffs. This enables game designers to author in Markdown while providing the game engine with validated, data-driven parameters.

## Function Signatures

**`rules/tools/extract.py`**
* `def slugify(text: str) -> str:`
* `def guess_type(title: str, blocks: list) -> str:`
* `def parse_table(lines: list) -> dict:`
* `def parse_archetype_blocks(title: str, blocks: list, current_tier: int) -> dict:`
* `def extract_rules(filepath: str) -> list:`

**`rules/tools/generate.py`**
* `def generate_table(table_block: dict) -> str:`
* `def render_blocks(blocks: list) -> str:`
* `def generate_archetype_definition(rule: dict, heading_level: int = 4) -> str:`
* `def rule_to_markdown(rule: dict, heading_level: int = 2) -> str:`
* `def generate_markdown(rules: list) -> str:`

**`rules/tools/enrich.py`**
* `def load_yaml(path: str) -> list:`
* `def save_yaml(path: str, data: list) -> None:`
* `def enrich_rules_v3(entries: list) -> list:`
* `def enrich_risk_reward(entries: list) -> list:`
* `def enrich_async_time(entries: list) -> list:`
* `def enrich_traps(entries: list) -> list:`
* `def enrich_archetypes(entries: list) -> list:`
* `def count_enriched(entries: list) -> tuple:`
* `def validate_vocabulary(entries: list, filename: str) -> list:`

**`rules/tools/accuracy_check.py`**
* `def check_file(filepath: str) -> list:`

*(Note: `coverage_check.py` and `generate_tests.py` contain standard CLI main entry points for pipeline execution as documented in the issue)*

## Input/Output Examples

**Extraction (Markdown to YAML)**
*Input (`rules-v3.md`)*:
```markdown
## Fumble (Miss by 1-2)
-1 Interest. You stumbled, but it's recoverable.
```
*Output (`extracted/rules-v3.yaml`)*:
```yaml
- id: fumble-miss-by-1-2
  section: Fumble (Miss by 1-2)
  type: interest_change
  description: "-1 Interest. You stumbled, but it's recoverable."
  blocks:
    - kind: paragraph
      text: "-1 Interest. You stumbled, but it's recoverable."
```

**Enrichment**
*Input (`extracted/rules-v3.yaml`)*:
(Same as above)
*Output (`extracted/rules-v3-enriched.yaml`)*:
```yaml
- id: fumble-miss-by-1-2
  section: Fumble (Miss by 1-2)
  type: interest_change
  description: "-1 Interest. You stumbled, but it's recoverable."
  condition:
    miss_margin_min: 1
    miss_margin_max: 2
  outcome:
    interest_delta: -1
```

**Test Generation**
*Input (`extracted/rules-v3-enriched.yaml`)*:
(Same as above)
*Output (`RulesSpecTests_Enriched.cs`)*:
```csharp
[Fact]
public void FumbleMissBy12_AppliesEffect()
{
    // Arrange & Act
    var delta = FailureScale.GetInterestDelta(FailureTier.Fumble);
    
    // Assert
    Assert.Equal(-1, delta);
}
```

## Acceptance Criteria

1. **Bidirectional Extraction & Generation**
   - The system must extract blocks (paragraphs, tables, code blocks, blockquotes) from Markdown files into an ordered list in YAML.
   - The system must generate Markdown from YAML that maintains block ordering, table alignments, and column widths.
2. **Round-Trip Fidelity**
   - When extracting a document to YAML and regenerating it back to Markdown, the textual diff must not exceed 50 lines per document.
   - No information loss (prose, structural depth) is permitted during the round-trip.
3. **Coverage Enforcement**
   - Every section heading in the source Markdown must have a corresponding extracted YAML entry (100% coverage, resulting in 0 orphaned entries).
4. **Data Enrichment**
   - The system must parse unstructured text into structured `condition` and `outcome` vocabularies for mechanical rules (e.g., `interest_change`, `roll_modifier`, `shadow_growth`).
5. **Test Stub Generation**
   - The system must generate C# xUnit test stubs (`RulesSpecTests.cs`) based on the YAML definitions.
   - Qualitative/LLM rules must be mapped to `[Fact(Skip = "...")]` with `NotImplementedException`.
6. **Accuracy Validation**
   - Machine-readable enriched data must be validated against the source prose to ensure semantic equivalence without deviations.

## Edge Cases

* **Empty Cells / Padded Tables**: Tables with padded cells (e.g., `| ------------- |`) must be accurately represented and regenerated with identical padding.
* **Heading Depth Flattening**: Headings `###` and `####` map to `##` upon regeneration. The hierarchical depth is safely maintained in the YAML `id` slug and `heading_level` properties.
* **Unstructured Prose**: Rule blocks that are purely thematic or qualitative fallback to unstructured formatting or skipped test stubs if they represent cosmetic instructions.
* **Open-ended Ranges**: Ranges like "Miss by 10+" map to `min: 10` without a `max` bound in the condition schema.

## Error Conditions

* **Extraction Failure**: Raised if a table in Markdown is malformed or lacks a separator row.
* **CoverageMismatch**: Fails if a Markdown section has no corresponding YAML block.
* **VocabularyValidationError**: Raised by `validate_vocabulary` if unrecognized keys are used in `condition` or `outcome`.
* **Diff Threshold Exceeded**: The `roundtrip_test.sh` script fails with a non-zero exit code if any regenerated file exceeds the 50-line diff limit.

## Dependencies

* **Python 3.x**
* **PyYAML**: Used for parsing and dumping YAML structures safely.
* **Bash**: Required for executing `roundtrip_test.sh`.
* **xUnit / C#**: Target framework for the output of `generate_tests.py`.
