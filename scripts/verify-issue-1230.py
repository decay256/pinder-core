import os
import re
import sys

def verify_docs():
    files_to_check = [
        'docs/unity-integration.md',
        'docs/modules/conversation.md',
        'docs/modules/game-session.md',
        'docs/modules/conversation-game-session.md'
    ]
    
    stale_patterns = {
        'Zero-arg GameSessionConfig': r'new\s+GameSessionConfig\s*\(\s*\)',
        'Old Snapshot API': r'\.Snapshot\s*\(',
        'Old Restore API': r'GameSession\.Restore\s*\(',
        'Removed action ReadAsync': r'\bReadAsync\b',
        'Removed action RecoverAsync': r'\bRecoverAsync\b',
        'Removed type ReadResult': r'\bReadResult\b',
        'Removed type RecoverResult': r'\bRecoverResult\b',
    }

    project_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    
    errors = []

    for rel_path in files_to_check:
        filepath = os.path.join(project_root, rel_path)
        if not os.path.exists(filepath):
            print(f"Warning: File not found: {rel_path}")
            continue
            
        with open(filepath, 'r', encoding='utf-8') as f:
            content = f.read()
            lines = content.splitlines()

            # Check for stale patterns
            for i, line in enumerate(lines):
                # Skip lines explicitly marked as historical or archived
                if re.search(r'\b(historical|archived)\b', line, re.IGNORECASE):
                    continue

                for desc, pattern in stale_patterns.items():
                    if re.search(pattern, line):
                        errors.append(f"{rel_path}:{i+1} - Found stale reference ({desc}): {line.strip()}")
            
            # Check for ResolveTurnAsync without StartTurnAsync
            # A simple heuristic: if ResolveTurnAsync is found in a code block without StartTurnAsync
            code_blocks = re.findall(r'```(?:csharp|cs)?\n(.*?)\n```', content, re.DOTALL)
            for block in code_blocks:
                if 'ResolveTurnAsync' in block and 'StartTurnAsync' not in block:
                    errors.append(f"{rel_path} - Code snippet has ResolveTurnAsync without StartTurnAsync:\n{block.strip()}")
                    
    if errors:
        print("Documentation verification failed! Stale references found:")
        for err in errors:
            print(f" - {err}")
        sys.exit(1)
    else:
        print("Documentation verification passed! No stale references found.")
        sys.exit(0)

if __name__ == '__main__':
    verify_docs()
