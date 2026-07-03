# Check Version Bump

This tool verifies if changes made to gameplay-affecting files (such as engine/rules code, schemas, and templates) are accompanied by a strictly greater SemVer version bump in `Directory.Build.props` compared to `origin/main`.

It is intended for use in pre-commit hooks or CI/CD pipelines to ensure that gameplay-affecting changes are properly versioned.

## Usage

To run the verification script manually, execute:

```bash
python rules/tools/check_version_bump.py
```

## Running Tests

To run the unit and integration tests for this tool, execute:

```bash
uv run pytest rules/tools/test_check_version_bump.py
```
