#!/usr/bin/env python3
"""
Version Bump Verification Tool

This script verifies if changes made to gameplay-affecting files (such as 
engine/rules code, schemas, and templates) are accompanied by a strictly
greater SemVer version bump in Directory.Build.props compared to origin/main.

It is intended for use in pre-commit hooks or CI/CD pipelines to ensure
that gameplay-affecting changes are properly versioned.
"""

import os
import re
import subprocess
import sys


def is_gameplay_affecting(path: str) -> bool:
    """
    Returns True if the path is classified as gameplay-affecting.
    
    Gameplay-affecting paths include:
    - Engine/rules code under src/Pinder.Core/ or src/Pinder.LlmAdapters/
    - Schema/data under data/anatomy/*.json, data/items/*.json,
      or exactly data/characters/character-schema.json
    - Prompts under Prompts/, prompts/, or data/prompts/ ending with .txt or .yaml
    """
    p = path.replace('\\', '/')
    if p.startswith('./'):
        p = p[2:]
        
    # Engine/rules
    if p.startswith("src/Pinder.Core/") or p.startswith("src/Pinder.LlmAdapters/"):
        return True
        
    # Schema/data
    if (p.startswith("data/anatomy/") and p.endswith(".json")) or \
       (p.startswith("data/items/") and p.endswith(".json")) or \
       (p == "data/characters/character-schema.json"):
        return True
        
    # Prompts
    if (p.startswith("Prompts/") or p.startswith("prompts/") or p.startswith("data/prompts/")) and \
       (p.endswith(".txt") or p.endswith(".yaml")):
        return True
        
    return False


def parse_semver(version_str: str):
    """
    Parses a SemVer string into a comparable tuple:
    (major, minor, patch, precedence_flag, prerelease_tuple)
    
    Handles standard <major>.<minor>.<patch> with optional prerelease suffixes.
    """
    v_str = version_str.strip().lstrip('v')
    
    # Discard build metadata (if any)
    if '+' in v_str:
        v_str = v_str.split('+', 1)[0]
        
    if '-' in v_str:
        release_part, prerelease_part = v_str.split('-', 1)
        precedence_flag = 0
    else:
        release_part = v_str
        prerelease_part = ""
        precedence_flag = 1
        
    num_parts = release_part.split('.')
    major = 0
    minor = 0
    patch = 0
    if len(num_parts) >= 1 and num_parts[0].isdigit():
        major = int(num_parts[0])
    if len(num_parts) >= 2 and num_parts[1].isdigit():
        minor = int(num_parts[1])
    if len(num_parts) >= 3 and num_parts[2].isdigit():
        patch = int(num_parts[2])
        
    prerelease_tuple = ()
    if prerelease_part:
        idents = prerelease_part.split('.')
        parsed_idents = []
        for ident in idents:
            if ident.isdigit():
                parsed_idents.append((0, int(ident)))
            else:
                parsed_idents.append((1, ident))
        prerelease_tuple = tuple(parsed_idents)
        
    return (major, minor, patch, precedence_flag, prerelease_tuple)


def is_semver_greater(v_new: str, v_old: str) -> bool:
    """
    Parses two SemVer strings and returns True if v_new is strictly greater than v_old.
    """
    try:
        return parse_semver(v_new) > parse_semver(v_old)
    except Exception:
        return False


def get_repo_root() -> str:
    """
    Retrieves the absolute path of the repository root.
    """
    try:
        result = subprocess.run(
            ["git", "rev-parse", "--show-toplevel"],
            capture_output=True,
            text=True,
            check=True
        )
        return result.stdout.strip()
    except Exception:
        return os.getcwd()


def get_changed_files() -> list[str]:
    """
    Runs git diff to get the list of files changed between HEAD and origin/main.
    """
    try:
        result = subprocess.run(
            ["git", "diff", "--name-only", "origin/main"],
            capture_output=True,
            text=True,
            check=True
        )
        return [line.strip() for line in result.stdout.splitlines() if line.strip()]
    except subprocess.CalledProcessError as e:
        print(f"Error running git diff: {e}", file=sys.stderr)
        raise e


def extract_version(content: str) -> str:
    """
    Extracts the version string from Directory.Build.props content.
    """
    match = re.search(r"<Version>(.*?)</Version>", content)
    if match:
        return match.group(1).strip()
    return "0.0.0"


def main():
    try:
        changed_files = get_changed_files()
    except Exception as e:
        print(f"Failed to query git diff: {e}", file=sys.stderr)
        sys.exit(1)
        
    gameplay_files = [f for f in changed_files if is_gameplay_affecting(f)]
    
    if gameplay_files:
        print(f"Detected {len(gameplay_files)} gameplay-affecting file change(s):")
        for gf in gameplay_files[:10]:
            print(f"  - {gf}")
        if len(gameplay_files) > 10:
            print(f"  - ... and {len(gameplay_files) - 10} more")
            
        # Get local Directory.Build.props version
        repo_root = get_repo_root()
        props_path = os.path.join(repo_root, "Directory.Build.props")
        
        try:
            with open(props_path, "r", encoding="utf-8") as f:
                local_content = f.read()
            local_version = extract_version(local_content)
        except Exception as e:
            print(f"Error: Failed to read local Directory.Build.props at '{props_path}': {e}", file=sys.stderr)
            sys.exit(1)
            
        # Get origin/main Directory.Build.props version
        try:
            result = subprocess.run(
                ["git", "show", "origin/main:Directory.Build.props"],
                capture_output=True,
                text=True,
                check=True
            )
            origin_version = extract_version(result.stdout)
        except Exception as e:
            print(f"Warning: Failed to retrieve Directory.Build.props from origin/main: {e}. Defaulting to 0.0.0", file=sys.stderr)
            origin_version = "0.0.0"
            
        print(f"Comparing versions: local={local_version} vs origin/main={origin_version}")
        
        if is_semver_greater(local_version, origin_version):
            print("Success: Version bump verification passed.")
            sys.exit(0)
        else:
            print(
                f"Error: Gameplay-affecting changes were made, but Directory.Build.props "
                f"version was not bumped. Local version '{local_version}' is not greater "
                f"than origin/main version '{origin_version}'.",
                file=sys.stderr
            )
            sys.exit(1)
    else:
        print("No gameplay-affecting files changed. Version bump check bypassed.")
        sys.exit(0)


if __name__ == "__main__":
    main()
