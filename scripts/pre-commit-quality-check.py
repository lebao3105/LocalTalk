#!/usr/bin/env python3
"""
Pre-commit hook for LocalTalk code quality validation
Runs basic code quality checks before allowing commits
"""

import os
import sys
import subprocess
import re
from pathlib import Path

def check_csharp_files():
    """Check C# files for basic quality issues"""
    issues = []
    
    # Get list of staged C# files
    try:
        result = subprocess.run(['git', 'diff', '--cached', '--name-only', '--diff-filter=ACM'], 
                              capture_output=True, text=True, check=True)
        staged_files = [f for f in result.stdout.strip().split('\n') if f.endswith('.cs')]
    except subprocess.CalledProcessError:
        return ["Failed to get staged files"]
    
    if not staged_files:
        return []
    
    for file_path in staged_files:
        if not os.path.exists(file_path):
            continue
            
        with open(file_path, 'r', encoding='utf-8') as f:
            content = f.read()
            lines = content.split('\n')
        
        # Check for basic issues
        file_issues = []
        
        # Check line length
        for i, line in enumerate(lines, 1):
            if len(line) > 120:
                file_issues.append(f"Line {i}: Exceeds 120 characters ({len(line)} chars)")
        
        # Check for camelCase properties
        camel_props = re.findall(r'public\s+\w+\s+([a-z]\w*)\s*{', content)
        for prop in camel_props:
            file_issues.append(f"Property '{prop}' should be PascalCase")
        
        # Check for magic numbers
        magic_numbers = re.findall(r'\b(?<![\w.])\d{3,}\b(?![\w.])', content)
        filtered_magic = [n for n in magic_numbers if n not in ['100', '200', '404', '500', '1000', '2000']]
        if len(filtered_magic) > 3:
            file_issues.append(f"Found {len(filtered_magic)} potential magic numbers")
        
        # Check for missing documentation on public members
        public_members = len(re.findall(r'public\s+(?:class|struct|interface|\w+\s+\w+)', content))
        xml_docs = len(re.findall(r'///\s*<summary>', content))
        if public_members > xml_docs and public_members > 0:
            file_issues.append(f"Missing XML docs: {public_members - xml_docs} of {public_members} public members")
        
        if file_issues:
            issues.append(f"\n{file_path}:")
            for issue in file_issues[:5]:  # Limit to first 5 issues per file
                issues.append(f"  - {issue}")
            if len(file_issues) > 5:
                issues.append(f"  - ... and {len(file_issues) - 5} more issues")
    
    return issues

def main():
    """Main pre-commit hook function"""
    print("🔍 Running LocalTalk code quality checks...")
    
    # Check if we're in a git repository
    if not os.path.exists('.git'):
        print("❌ Not in a git repository")
        return 1
    
    # Run C# file checks
    issues = check_csharp_files()
    
    if issues:
        print("❌ Code quality issues found:")
        for issue in issues:
            print(issue)
        print("\n💡 Fix these issues before committing or run:")
        print("   python3 tests/code-standards-validator.py")
        print("   for detailed analysis and suggestions.")
        return 1
    else:
        print("✅ Code quality checks passed!")
        return 0

if __name__ == "__main__":
    sys.exit(main())
