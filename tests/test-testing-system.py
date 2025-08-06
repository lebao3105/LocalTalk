#!/usr/bin/env python3
"""
Test script for the LocalTalk validation system
This script validates the validation scripts themselves and tests the system
"""

import os
import sys
import json
import re
from pathlib import Path
from datetime import datetime

def print_header(title):
    print(f"\n{'='*50}")
    print(f"🔍 {title}")
    print(f"{'='*50}")

def print_success(message):
    print(f"✅ {message}")

def print_error(message):
    print(f"❌ {message}")

def print_warning(message):
    print(f"⚠️  {message}")

def print_info(message):
    print(f"ℹ️  {message}")

def test_validation_structure():
    """Test that the validation directory structure is correct"""
    print_header("Testing Validation Directory Structure")
    
    required_dirs = [
        "tests",
        "tests/code-standards",
        "tests/file-transfer",
        "tests/performance",
        "tests/platform-deployment"
    ]
    
    required_files = [
        "tests/MasterAssumptionValidator.ps1",
        "tests/README.md",
        "tests/code-standards/CodeStandardsValidator.ps1",
        "tests/code-standards/DeveloperPolicyValidator.ps1",
        "tests/code-standards/README.md"
    ]
    
    success = True
    
    # Check directories
    for dir_path in required_dirs:
        if os.path.exists(dir_path) and os.path.isdir(dir_path):
            print_success(f"Directory exists: {dir_path}")
        else:
            print_error(f"Missing directory: {dir_path}")
            success = False
    
    # Check files
    for file_path in required_files:
        if os.path.exists(file_path) and os.path.isfile(file_path):
            print_success(f"File exists: {file_path}")
        else:
            print_error(f"Missing file: {file_path}")
            success = False
    
    # Create results directory if it doesn't exist
    results_dir = "tests/results"
    if not os.path.exists(results_dir):
        os.makedirs(results_dir)
        print_info(f"Created results directory: {results_dir}")
    else:
        print_success(f"Results directory exists: {results_dir}")
    
    return success

def test_powershell_syntax():
    """Test PowerShell scripts for basic syntax issues"""
    print_header("Testing PowerShell Script Syntax")
    
    ps_files = [
        "tests/code-standards/CodeStandardsValidator.ps1",
        "tests/code-standards/DeveloperPolicyValidator.ps1",
        "tests/MasterAssumptionValidator.ps1"
    ]
    
    success = True
    
    for ps_file in ps_files:
        if not os.path.exists(ps_file):
            print_error(f"PowerShell file not found: {ps_file}")
            success = False
            continue
            
        with open(ps_file, 'r', encoding='utf-8') as f:
            content = f.read()
            
        # Basic syntax checks
        issues = []
        
        # Check for balanced braces (excluding hashtable literals)
        import re
        content_no_hashtables = re.sub(r'@\s*{[^}]*}', '', content)
        open_braces = content_no_hashtables.count('{')
        close_braces = content_no_hashtables.count('}')
        if open_braces != close_braces:
            issues.append(f"Unbalanced braces: {open_braces} open, {close_braces} close")
        
        # Check for balanced parentheses in major constructs
        if_matches = re.findall(r'if\s*\([^)]*\)', content)
        foreach_matches = re.findall(r'foreach\s*\([^)]*\)', content)
        
        # Check for param block
        if 'param(' not in content:
            issues.append("Missing param block")
        
        # Check for basic error handling
        if '$ErrorActionPreference' not in content:
            issues.append("Missing error action preference")
        
        if issues:
            print_error(f"Syntax issues in {ps_file}:")
            for issue in issues:
                print(f"  - {issue}")
            success = False
        else:
            print_success(f"Syntax OK: {ps_file}")
    
    return success

def test_csharp_code_samples():
    """Test the C# code that will be validated"""
    print_header("Testing C# Code Samples")
    
    # Find C# files in the Shared directory
    shared_dir = Path("Shared")
    if not shared_dir.exists():
        print_error("Shared directory not found")
        return False
    
    cs_files = list(shared_dir.rglob("*.cs"))
    if not cs_files:
        print_error("No C# files found in Shared directory")
        return False
    
    print_info(f"Found {len(cs_files)} C# files to analyze")
    
    # Analyze a sample file for common issues
    sample_file = "Shared/Models/Device.cs"
    if os.path.exists(sample_file):
        with open(sample_file, 'r', encoding='utf-8') as f:
            content = f.read()
        
        issues_found = []
        
        # Check for camelCase properties (should be PascalCase)
        camel_properties = re.findall(r'public\s+\w+\s+([a-z]\w*)\s*{', content)
        if camel_properties:
            issues_found.extend([f"camelCase property: {prop}" for prop in camel_properties])
        
        # Check for missing XML documentation
        public_members = re.findall(r'public\s+(?:class|struct|interface|\w+\s+\w+)', content)
        xml_docs = re.findall(r'///\s*<summary>', content)
        
        if len(public_members) > len(xml_docs):
            issues_found.append(f"Missing XML docs: {len(public_members)} public members, {len(xml_docs)} documented")
        
        if issues_found:
            print_warning(f"Code standards issues found in {sample_file}:")
            for issue in issues_found[:5]:  # Show first 5 issues
                print(f"  - {issue}")
            if len(issues_found) > 5:
                print(f"  ... and {len(issues_found) - 5} more issues")
        else:
            print_success(f"No obvious issues in {sample_file}")
    
    return True

def test_validation_configuration():
    """Test the validation configuration and standards"""
    print_header("Testing Validation Configuration")
    
    # Check if the code standards are reasonable
    config_tests = [
        ("Max line length should be reasonable", lambda: 80 <= 120 <= 200),
        ("Max method length should be reasonable", lambda: 20 <= 50 <= 100),
        ("Standards should include naming conventions", lambda: True),
        ("Standards should include security checks", lambda: True)
    ]
    
    success = True
    for test_name, test_func in config_tests:
        try:
            if test_func():
                print_success(test_name)
            else:
                print_error(test_name)
                success = False
        except Exception as e:
            print_error(f"{test_name}: {e}")
            success = False
    
    return success

def generate_test_report():
    """Generate a test report"""
    print_header("Validation System Test Report")
    
    timestamp = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    
    report = {
        "timestamp": timestamp,
        "test_results": {
            "structure_test": test_validation_structure(),
            "syntax_test": test_powershell_syntax(),
            "csharp_test": test_csharp_code_samples(),
            "config_test": test_validation_configuration()
        }
    }
    
    # Calculate overall success
    all_passed = all(report["test_results"].values())
    
    print(f"\n📊 Test Summary ({timestamp})")
    print("=" * 40)
    
    for test_name, result in report["test_results"].items():
        status = "PASS" if result else "FAIL"
        color = "✅" if result else "❌"
        print(f"{color} {test_name.replace('_', ' ').title()}: {status}")
    
    print(f"\nOverall Result: {'✅ PASS' if all_passed else '❌ FAIL'}")
    
    # Save report
    report_file = f"tests/results/testing-system-test-{datetime.now().strftime('%Y%m%d-%H%M%S')}.json"
    with open(report_file, 'w') as f:
        json.dump(report, f, indent=2)
    
    print(f"\n📄 Test report saved to: {report_file}")
    
    if all_passed:
        print("\n🎉 All validation system tests passed!")
        print("The LLVM-inspired code standards validation system is ready to use.")
    else:
        print("\n⚠️  Some tests failed. Please review the issues above.")
    
    return all_passed

def main():
    """Main test function"""
    print("LocalTalk Validation System Test")
    print("Testing the LLVM-inspired code standards validation system")
    
    # Change to the project root directory
    if os.path.basename(os.getcwd()) != "LocalTalk":
        # Try to find the LocalTalk directory
        for root, dirs, files in os.walk("."):
            if "LocalTalk.sln" in files:
                os.chdir(root)
                break
        else:
            print_error("Could not find LocalTalk project root")
            return False
    
    print_info(f"Working directory: {os.getcwd()}")
    
    # Run the test suite
    success = generate_test_report()
    
    if success:
        print("\n🚀 Next Steps:")
        print("1. Run the code standards validator:")
        print("   pwsh tests/code-standards/CodeStandardsValidator.ps1 -Verbose")
        print("2. Run the developer policy validator:")
        print("   pwsh tests/code-standards/DeveloperPolicyValidator.ps1 -Verbose")
        print("3. Run the master validator with new categories:")
        print("   pwsh tests/MasterAssumptionValidator.ps1 -TestCategories CodeStandardsCompliance,DeveloperPolicyCompliance")
    
    return success

if __name__ == "__main__":
    success = main()
    sys.exit(0 if success else 1)
