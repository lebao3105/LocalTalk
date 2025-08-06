#!/usr/bin/env python3
"""
Setup script for LocalTalk code quality automation
Configures pre-commit hooks and IDE integration
"""

import os
import sys
import shutil
import stat
from pathlib import Path

def setup_pre_commit_hook():
    """Setup git pre-commit hook"""
    git_hooks_dir = Path('.git/hooks')
    
    if not git_hooks_dir.exists():
        print("❌ Not in a git repository or hooks directory missing")
        return False
    
    # Create pre-commit hook
    hook_path = git_hooks_dir / 'pre-commit'
    hook_content = '''#!/bin/bash
# LocalTalk Code Quality Pre-commit Hook

echo "🔍 Running LocalTalk code quality checks..."

# Run Python quality checker
python3 scripts/pre-commit-quality-check.py
exit_code=$?

if [ $exit_code -ne 0 ]; then
    echo ""
    echo "💡 To bypass this check (not recommended), use:"
    echo "   git commit --no-verify"
    echo ""
    echo "📚 For detailed quality analysis, run:"
    echo "   python3 tests/code-standards-validator.py"
    exit $exit_code
fi

echo "✅ Pre-commit quality checks passed!"
exit 0
'''
    
    try:
        with open(hook_path, 'w') as f:
            f.write(hook_content)
        
        # Make executable
        st = os.stat(hook_path)
        os.chmod(hook_path, st.st_mode | stat.S_IEXEC)
        
        print(f"✅ Pre-commit hook installed: {hook_path}")
        return True
    except Exception as e:
        print(f"❌ Failed to install pre-commit hook: {e}")
        return False

def create_vscode_settings():
    """Create VS Code settings for code quality"""
    vscode_dir = Path('.vscode')
    vscode_dir.mkdir(exist_ok=True)
    
    settings_path = vscode_dir / 'settings.json'
    settings_content = '''{
    "editor.rulers": [120],
    "editor.formatOnSave": true,
    "editor.codeActionsOnSave": {
        "source.organizeImports": true,
        "source.fixAll": true
    },
    "files.trimTrailingWhitespace": true,
    "files.insertFinalNewline": true,
    "csharp.format.enable": true,
    "omnisharp.enableRoslynAnalyzers": true,
    "dotnet.completion.showCompletionItemsFromUnimportedNamespaces": true,
    "csharp.semanticHighlighting.enabled": true
}'''
    
    try:
        with open(settings_path, 'w') as f:
            f.write(settings_content)
        print(f"✅ VS Code settings created: {settings_path}")
        return True
    except Exception as e:
        print(f"❌ Failed to create VS Code settings: {e}")
        return False

def create_editorconfig():
    """Create .editorconfig for consistent formatting"""
    editorconfig_path = Path('.editorconfig')
    editorconfig_content = '''# EditorConfig for LocalTalk
root = true

[*]
charset = utf-8
end_of_line = lf
insert_final_newline = true
trim_trailing_whitespace = true

[*.cs]
indent_style = space
indent_size = 4
max_line_length = 120

[*.{json,yml,yaml}]
indent_style = space
indent_size = 2

[*.{xml,xaml}]
indent_style = space
indent_size = 4

[*.md]
trim_trailing_whitespace = false
'''
    
    try:
        with open(editorconfig_path, 'w') as f:
            f.write(editorconfig_content)
        print(f"✅ EditorConfig created: {editorconfig_path}")
        return True
    except Exception as e:
        print(f"❌ Failed to create EditorConfig: {e}")
        return False

def main():
    """Main setup function"""
    print("🚀 Setting up LocalTalk code quality automation...")
    print()
    
    success_count = 0
    total_count = 3
    
    # Setup pre-commit hook
    if setup_pre_commit_hook():
        success_count += 1
    
    # Create VS Code settings
    if create_vscode_settings():
        success_count += 1
    
    # Create EditorConfig
    if create_editorconfig():
        success_count += 1
    
    print()
    print(f"📊 Setup completed: {success_count}/{total_count} items configured")
    
    if success_count == total_count:
        print("✅ All automation tools configured successfully!")
        print()
        print("📋 What's been set up:")
        print("  • Pre-commit hook for quality checks")
        print("  • VS Code settings for consistent formatting")
        print("  • EditorConfig for cross-editor consistency")
        print()
        print("💡 Next steps:")
        print("  • Restart VS Code to apply new settings")
        print("  • Run 'python3 tests/code-standards-validator.py' for full analysis")
        print("  • Commit changes to test the pre-commit hook")
        return 0
    else:
        print("⚠️  Some automation tools could not be configured")
        print("   Check the error messages above for details")
        return 1

if __name__ == "__main__":
    sys.exit(main())
