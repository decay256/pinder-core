#!/usr/bin/env python3
import os
import sys
import re

def check_docs():
    docs_to_check = [
        "docs/modules/llm-adapters.md",
        "docs/ARCHITECTURE.md"
    ]

    stale_patterns = [
        r"StartConversation",
        r"HasActiveConversation",
        r"AnthropicLlmAdapter",
        r"OpenAiLlmAdapter",
        r"adapter-owned history",
        r"adapter owns history",
        r"adapter state",
        r"session initialization.*?adapter",
        r"IStatefulLlmAdapter",
    ]
    
    required_patterns = [
        r"PinderLlmAdapter",
        r"ILlmTransport",
        r"GameSession",
        r"history-passing"
    ]

    has_errors = False
    
    found_required = {p: False for p in required_patterns}
    
    for doc in docs_to_check:
        if not os.path.exists(doc):
            print(f"ERROR: {doc} not found")
            has_errors = True
            continue
            
        with open(doc, 'r') as f:
            content = f.read()
            lines = content.split('\n')
            
        for i, line in enumerate(lines):
            # Check for stale patterns
            for pattern in stale_patterns:
                if re.search(pattern, line, re.IGNORECASE):
                    # check if marked historical/deprecated
                    if pattern in ["AnthropicLlmAdapter", "OpenAiLlmAdapter", "IStatefulLlmAdapter", "StartConversation", "HasActiveConversation"]:
                        if "historical" in line.lower() or "deprecated" in line.lower() or "archived" in line.lower() or "removed" in line.lower():
                            continue
                    
                    print(f"ERROR: Found stale reference in {doc}:{i+1} matching '{pattern}':\n    {line.strip()}")
                    has_errors = True
                
        for pattern in required_patterns:
            if re.search(pattern, content, re.IGNORECASE):
                found_required[pattern] = True
                
    for pattern, found in found_required.items():
        if not found:
            print(f"ERROR: Required documentation for '{pattern}' not found in any checked doc")
            has_errors = True

    if has_errors:
        print("Documentation verification FAILED.")
        sys.exit(1)
    else:
        print("Documentation verification PASSED.")
        sys.exit(0)

if __name__ == "__main__":
    check_docs()
