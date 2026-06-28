#!/usr/bin/env python3
import os
import sys
import re

FILES_TO_CHECK = [
    "docs/modules/session-runner.md",
    "docs/persona/texting-style-aggregation.md",
    "docs/persona/texting-style-rework-investigation.md",
    "docs/modules/remote-assets.md",
    "README.md",
    "docs/ARCHITECTURE.md",
    "docs/unity-integration.md"
]

def read_file(filepath):
    try:
        with open(filepath, 'r', encoding='utf-8') as f:
            return f.read()
    except FileNotFoundError:
        return ""

def main():
    errors = []
    files_content = {f: read_file(f) for f in FILES_TO_CHECK}

    # Session Runner options and defaults
    for f in ["docs/modules/session-runner.md", "README.md"]:
        content = files_content.get(f, "")
        if not content: continue
        
        if "--max-turns" in content:
            errors.append(f"{f}: Found stale reference to '--max-turns'. Should be '--turns'.")
            
        if re.search(r"default.*20", content, re.IGNORECASE) or re.search(r"20.*default", content, re.IGNORECASE):
            errors.append(f"{f}: Found stale reference to default being 20. Should be 30.")

    # Old texting style slots
    for f in ["docs/persona/texting-style-aggregation.md", "docs/persona/texting-style-rework-investigation.md"]:
        content = files_content.get(f, "")
        if not content: continue
        
        is_historical = "historical" in content.lower() or "archived" in content.lower() or "deprecated" in content.lower()
        
        if ("item_id" in content or "tier" in content or "anatomy" in content.lower()) and not is_historical:
            errors.append(f"{f}: Found old texting-style slots/schema (item_id/tier/anatomy) but file not marked historical/archived.")

        if f == "docs/persona/texting-style-aggregation.md":
            required_slots = ["Special", "Head", "Body", "Hair", "Arms", "Face"]
            for slot in required_slots:
                if slot not in content:
                    errors.append(f"{f}: Missing required slot '{slot}'.")
            if "item schema v2" not in content.lower() and "schema v2" not in content.lower() and "v2" not in content.lower():
                errors.append(f"{f}: Missing reference to item schema v2.")

    # Remote assets
    f = "docs/modules/remote-assets.md"
    remote_assets_content = files_content.get(f, "")
    if remote_assets_content:
        # Looking for signs that https or response size are still in "follow-up tickets" or "todo"
        if re.search(r'follow-up.*?https', remote_assets_content, re.IGNORECASE | re.DOTALL) or \
           re.search(r'todo.*?https', remote_assets_content, re.IGNORECASE | re.DOTALL) or \
           "HTTPS-scheme enforcement" in remote_assets_content or \
           "Response-size cap" in remote_assets_content or \
           re.search(r'follow-up.*?limit', remote_assets_content, re.IGNORECASE | re.DOTALL) or \
           re.search(r'todo.*?limit', remote_assets_content, re.IGNORECASE | re.DOTALL):
            errors.append(f"{f}: HTTPS enforcement or buffer limits are still marked as todo/follow-up.")

    # Dependency tables
    for f in ["docs/ARCHITECTURE.md", "README.md", "docs/unity-integration.md"]:
        content = files_content.get(f, "")
        if not content: continue
        
        if re.search(r'Pinder\.Core.*?(zero|no)\s+(external\s+)?dependencies', content, re.IGNORECASE):
            errors.append(f"{f}: Found false claim that Pinder.Core has zero dependencies.")
            
        if re.search(r'Pinder\.Core.*?without\s+System\.Text\.Json', content, re.IGNORECASE) or \
           re.search(r'no\s+System\.Text\.Json', content, re.IGNORECASE):
            errors.append(f"{f}: Found false claim that Pinder.Core doesnt use System.Text.Json.")
            
        if re.search(r'Pinder\.SessionSetup\s+depends\s+(only|solely)\s+on\s+Pinder\.Core', content, re.IGNORECASE):
            errors.append(f"{f}: Found false claim that Pinder.SessionSetup depends ONLY on Pinder.Core.")

        if f == "docs/ARCHITECTURE.md":
            if "System.Text.Json" not in content:
                errors.append(f"{f}: Missing System.Text.Json in documented dependencies.")
            if "Microsoft.Bcl.AsyncInterfaces" not in content:
                errors.append(f"{f}: Missing Microsoft.Bcl.AsyncInterfaces in documented dependencies.")

    if errors:
        print("Contract Test FAILED:")
        for err in errors:
            print(f" - {err}")
        sys.exit(1)
    else:
        print("Contract Test PASSED: All documentation requirements met.")
        sys.exit(0)

if __name__ == "__main__":
    main()
