#!/usr/bin/env python3
"""Find unbalanced braces in PowerShell script"""

import sys

def find_unbalanced_braces(filename):
    with open(filename, 'r', encoding='utf-8') as f:
        content = f.read()

    # Remove hashtable literals @{ ... } and inline if statements
    import re

    # Remove here-strings that contain CSS/HTML (common in PowerShell reports)
    content_no_herestrings = re.sub(r'@"[\s\S]*?"@', '', content, flags=re.MULTILINE)

    # Remove hashtable literals @{ ... }
    content_no_hashtables = re.sub(r'@\s*{[^}]*}', '', content_no_herestrings)

    # Remove inline if statements like: if (condition) { value } else { value }
    content_no_inline_if = re.sub(r'if\s*\([^)]*\)\s*{\s*[^}]*\s*}\s*else\s*{\s*[^}]*\s*}', '', content_no_hashtables)

    # Remove simple inline expressions like: $(if (condition) { value } else { value })
    content_clean = re.sub(r'\$\(\s*if\s*\([^)]*\)\s*{\s*[^}]*\s*}\s*else\s*{\s*[^}]*\s*}\s*\)', '', content_no_inline_if)

    lines = content_clean.split('\n')

    open_braces = []
    close_braces = []

    for i, line in enumerate(lines, 1):
        for j, char in enumerate(line):
            if char == '{':
                open_braces.append((i, j, line.strip()))
            elif char == '}':
                close_braces.append((i, j, line.strip()))

    print(f"File: {filename}")
    print(f"Open braces (excluding here-strings, hashtables, and inline ifs): {len(open_braces)}")
    print(f"Close braces (excluding here-strings, hashtables, and inline ifs): {len(close_braces)}")
    print(f"Difference: {len(open_braces) - len(close_braces)}")

    if len(open_braces) != len(close_braces):
        print("\nLast few open braces:")
        for line_num, col, content in open_braces[-10:]:
            print(f"  Line {line_num}: {content}")

        print("\nLast few close braces:")
        for line_num, col, content in close_braces[-10:]:
            print(f"  Line {line_num}: {content}")

    return len(open_braces) == len(close_braces)

if __name__ == "__main__":
    if len(sys.argv) > 1:
        filename = sys.argv[1]
    else:
        filename = "tests/code-standards/CodeStandardsValidator.ps1"
    find_unbalanced_braces(filename)
