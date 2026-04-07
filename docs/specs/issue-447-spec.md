**Module**: docs/modules/rules-dsl-pipeline.md

## Overview
This feature integrates the Rules DSL accuracy checker (`rules/tools/accuracy_check.py`) into the continuous integration (CI) pipeline. It automatically triggers when design documents or extracted YAML files change, running the extractor to catch regressions where prose edits create a DSL mismatch. To manage execution costs (as the check utilizes LLM validation requiring `ANTHROPIC_API_KEY`), the check is strictly scoped to evaluate only the specific files or entries that were modified in the commit.

## Function Signatures

No new C# methods are introduced. The work involves a new GitHub Actions workflow (or pre-commit hook wrapper) and modifications to the Python accuracy checker.

**Expected modifications to `accuracy_check.py` CLI:**
```python
# New arguments to support targeted checking:
# --files: List of specific YAML files to check (instead of all in the directory)
# --entries: List of specific entry IDs to check (filters within the files)
def main(files: List[str] = None, entries: List[str] = None) -> int:
    ...
```

**New CI Workflow:**
`.github/workflows/rules-accuracy-check.yml` (or equivalent CI script/pre-commit hook)

## Input/Output Examples

**Scenario 1: Modifying a markdown design document**
- **Input Action**: A push to a branch modifies `design/systems/rules-v3.md`.
- **Pipeline Execution**:
  1. The workflow identifies `rules-v3.md` as changed.
  2. Runs `python rules/tools/extract.py design/systems/rules-v3.md`.
  3. Compares the newly generated `rules-v3-enriched.yaml` with the previous commit.
  4. Extracts the IDs of the modified YAML entries.
  5. Runs `accuracy_check.py --files rules-v3-enriched.yaml --entries <changed_ids>`.
- **Output**: The CI job succeeds if there are 0 `INACCURATE` findings.

**Scenario 2: Directly modifying an enriched YAML file**
- **Input Action**: A push to a branch modifies `rules/extracted/risk-reward-enriched.yaml`.
- **Pipeline Execution**:
  1. The workflow identifies the changed YAML file.
  2. Extracts the IDs of the modified entries using `git diff` or similar tools.
  3. Runs `accuracy_check.py --files risk-reward-enriched.yaml --entries <changed_ids>`.
- **Output**: If the accuracy checker detects an `INACCURATE` finding, the workflow exits with a non-zero code, failing the CI check.

## Acceptance Criteria

### AC1: Conditional Trigger on Markdown or YAML
The accuracy check CI workflow must trigger automatically on `push` or `pull_request` when changes are detected in `design/systems/*.md`, `design/settings/*.md`, or `rules/extracted/*-enriched.yaml`.

### AC2: Re-extraction of Markdown Changes
When markdown files are changed, the CI workflow must re-run `extract.py` (and subsequently `enrich.py` if applicable) on the changed documents to regenerate the corresponding YAML before running the accuracy checker.

### AC3: Failure on INACCURATE Findings
If `accuracy_check.py` returns any `INACCURATE` severity findings, the CI workflow must fail (exit code 1) and output the findings clearly in the workflow logs. Findings with `WARNING` severity should be printed but should not fail the build.

### AC4: Cost Control via Targeted Checking
The accuracy check must be scoped strictly to the changed files and the specific entries within those files. It must not evaluate all 63+ entries across the entire repository on every run. This controls the cost of the LLM-based verification by minimizing API calls.

### AC5: API Key Injection
The CI workflow must inject the `ANTHROPIC_API_KEY` repository secret into the environment of the accuracy checker.

### AC6: Local Execution Documentation
The `README.md` (or equivalent documentation in the `rules/` directory) must be updated to document how developers can run the targeted accuracy check locally, including environment variable setup (`ANTHROPIC_API_KEY`) and command-line usage for specifying files or entries.

## Edge Cases
- **No Rules Modified**: If a PR contains only C# code changes without modifying design docs or YAML, the accuracy check workflow should skip gracefully.
- **Missing API Key**: If `ANTHROPIC_API_KEY` is unavailable (e.g., a PR from a fork), the workflow should cleanly fail with an explicit "Missing API Key" message or skip the LLM evaluation steps, rather than producing a generic HTTP or crash error.
- **Deleted Entries**: If a commit deletes an entry from a design doc, the accuracy check should gracefully handle the removal and not attempt to evaluate a missing ID.

## Error Conditions
- `YAML Parse Error`: If manual edits break the YAML syntax, `accuracy_check.py` outputs an `INACCURATE` finding and fails CI.
- `Missing Environment Variable`: Failure to provide `ANTHROPIC_API_KEY` when required by the tool results in a controlled failure.
- `Subprocess Failure`: Any failure in `extract.py` or parsing the `git diff` halts the pipeline.

## Dependencies
- **GitHub Actions**: Execution environment for the CI workflow (or standard pre-commit framework if chosen).
- **Anthropic API**: The LLM provider required by the accuracy check. The CI environment must have `ANTHROPIC_API_KEY` configured as a repository secret.