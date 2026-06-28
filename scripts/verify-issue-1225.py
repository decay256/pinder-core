#!/usr/bin/env python3
import os
import sys

FILES_TO_CHECK = [
    "docs/modules/unity-core-sync-architecture.md",
    "docs/specs/unity-core-item-anatomy-contract.md",
    "docs/unity-integration.md"
]

def check_stale_content(filepath):
    if not os.path.exists(filepath):
        print(f"Error: {filepath} does not exist.")
        return True

    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    errors = []

    # 1. Stale pipeline description
    if "CharacterData" in content and ("CharacterDefinitionLoader" in content or "CharacterAssembler" in content):
        if "pending" not in content.lower() and "pinned" not in content.lower():
            errors.append("Stale pipeline description: CharacterData feed to CharacterDefinitionLoader / CharacterAssembler without stating it's pending/pinned.")

    # 2. Item schema v2 tier assertions
    if "tier" in content.lower() and ("omit" in content.lower() or "omission" in content.lower()):
        if "starter-items.json" not in content:
            errors.append("Item schema v2 tier assertions: documents tier omission without distinguishing current Unity starter-items.json using tier.")

    # 3. Opponent -> Datee
    if "Opponent" in content and "Datee" not in content:
        errors.append("Stale terminology: 'Opponent' found instead of 'Datee'.")
    if "GetOpponentResponseAsync" in content:
        if "PlaceholderLlmAdapter" not in content:
            errors.append("Stale terminology: 'GetOpponentResponseAsync' described as current (must reference Unity's PlaceholderLlmAdapter / DeliverMessageAsync).")

    # 4. Stale fields
    if "equipedAccessories" in content:
        errors.append("Stale field found: equipedAccessories[]")
    if "hairStyleIndex" in content:
        errors.append("Stale field found: hairStyleIndex")

    # 5. Stale LookCatalog counts/duplicate-id notes
    if "unity-core-item-anatomy-contract.md" in filepath:
        if "LookCatalog" in content and ("duplicate" in content.lower() or "count" in content.lower() or "14 items" in content.lower()):
            errors.append("Stale LookCatalog counts/duplicate-id notes found.")

    # 6. apiVersion assigned to Unity
    if "apiVersion" in content and "Unity" in content:
        if "browser proxy" not in content.lower():
            errors.append("apiVersion assigned to Unity without browser proxy notes.")

    # 7. Stale admin endpoints
    if "PUT /api/admin/items" in content:
        errors.append("Stale admin endpoint PUT /api/admin/items/{id} instead of /api/admin/content/items|anatomy.")
    if "staging-edits" in content:
        errors.append("Stale staging-edits branch reference instead of commit+push-to-main flow.")

    # 8. Jira handoff instructions
    if "Jira" in content or "JIRA" in content:
        errors.append("Stale Jira handoff instructions found.")

    if errors:
        print(f"\nFailures in {filepath}:")
        for err in errors:
            print(f" - {err}")
        return True
    
    return False

def main():
    has_errors = False
    for filepath in FILES_TO_CHECK:
        if check_stale_content(filepath):
            has_errors = True
            
    if has_errors:
        print("\nContract test FAILED: Stale documentation references found.")
        sys.exit(1)
    else:
        print("\nContract test PASSED: Documentation meets issue 1225 requirements.")
        sys.exit(0)

if __name__ == "__main__":
    main()
