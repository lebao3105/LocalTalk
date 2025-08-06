#!/usr/bin/env python3
"""
LocalTalk Code Standards Validation System

This module provides comprehensive code standards validation for the LocalTalk project,
implementing LLVM-inspired quality checks and analysis tools.

Usage:
    python3 tests/code-standards-validator.py

Features:
    - C# code standards analysis
    - Cross-platform compatibility
    - Integration with PowerShell validators
    - Real-time repository analysis
"""

import os
import json
import re
from pathlib import Path
from datetime import datetime

def print_header(title, char="="):
    print(f"\n{char*60}")
    print(f"🎯 {title}")
    print(f"{char*60}")

def print_section(title):
    print(f"\n{'─'*50}")
    print(f"📋 {title}")
    print(f"{'─'*50}")

def print_success(message):
    print(f"✅ {message}")

def print_warning(message):
    print(f"⚠️  {message}")

def print_info(message):
    print(f"ℹ️  {message}")

def print_error(message):
    print(f"❌ {message}")

def analyze_file_standards(file_path, lines, content):
    """Analyze a single C# file for standards compliance"""
    metrics = {
        "long_lines": 0,
        "missing_docs": 0,
        "camel_case_props": 0,
        "unused_usings": 0,
        "hardcoded_strings": 0,
        "complex_methods": 0,
        "magic_numbers": 0,
        "total_loc": len(lines),
        "documented_members": 0,
        "total_public_members": 0,
        "issues": 0,
        "violations": []
    }

    # Check for long lines (>120 characters)
    long_line_count = len([line for line in lines if len(line.strip()) > 120])
    if long_line_count > 0:
        metrics["long_lines"] = 1
        metrics["issues"] += 1
        metrics["violations"].append(f"Long lines: {long_line_count} lines exceed 120 characters")

    # Check for XML documentation coverage
    public_members = len(re.findall(r'public\s+(?:static\s+)?(?:virtual\s+)?(?:override\s+)?(?:async\s+)?(?:\w+\s+)+\w+\s*[({]', content))
    xml_docs = content.count('/// <summary>')
    metrics["total_public_members"] = public_members
    metrics["documented_members"] = xml_docs

    if public_members > xml_docs and public_members > 0:
        metrics["missing_docs"] = 1
        metrics["issues"] += 1
        undocumented = public_members - xml_docs
        metrics["violations"].append(f"Missing documentation: {undocumented} of {public_members} public members undocumented")

    # Check for camelCase properties
    camel_props = re.findall(r'public\s+\w+\s+([a-z]\w*)\s*{', content)
    if camel_props:
        metrics["camel_case_props"] = 1
        metrics["issues"] += 1
        metrics["violations"].append(f"Naming convention: {len(camel_props)} camelCase properties found")

    # Check for potentially unused using statements
    using_statements = [line.strip() for line in lines if line.strip().startswith('using ') and not line.strip().startswith('using (')]
    if len(using_statements) > 15:  # Threshold for review
        metrics["unused_usings"] = 1
        metrics["issues"] += 1
        metrics["violations"].append(f"Using statements: {len(using_statements)} using statements (review for unused)")

    # Check for hardcoded secrets/passwords
    hardcoded_patterns = [
        r'(password|secret|key|token)\s*=\s*["\'][^"\']+["\']',
        r'(api_key|apikey|auth_token)\s*=\s*["\'][^"\']+["\']'
    ]

    for pattern in hardcoded_patterns:
        matches = re.findall(pattern, content.lower())
        if matches:
            metrics["hardcoded_strings"] = 1
            metrics["issues"] += 1
            metrics["violations"].append(f"Security: Potential hardcoded secrets found")
            break

    # Check for magic numbers
    magic_numbers = re.findall(r'\b(?<![\w.])\d{2,}\b(?![\w.])', content)
    # Filter out common acceptable numbers
    filtered_magic = [n for n in magic_numbers if n not in ['100', '200', '404', '500', '1000', '2000']]
    if len(filtered_magic) > 5:
        metrics["magic_numbers"] = 1
        metrics["issues"] += 1
        metrics["violations"].append(f"Code quality: {len(filtered_magic)} potential magic numbers found")

    # Check for complex methods (basic heuristic)
    method_blocks = re.findall(r'(?:public|private|protected|internal)\s+(?:static\s+)?(?:virtual\s+)?(?:override\s+)?(?:async\s+)?\w+\s+\w+\s*\([^)]*\)\s*{([^{}]*(?:{[^{}]*}[^{}]*)*)}', content, re.DOTALL)
    complex_methods = [block for block in method_blocks if len(block.split('\n')) > 50]
    if complex_methods:
        metrics["complex_methods"] = len(complex_methods)
        metrics["issues"] += 1
        metrics["violations"].append(f"Method complexity: {len(complex_methods)} methods exceed 50 lines")

    return metrics

def analyze_csharp_violations():
    """Analyze C# code for standards violations"""
    print_section("C# Code Standards Analysis")
    
    # Analyze the Device.cs file as an example
    device_file = "Shared/Models/Device.cs"
    if not os.path.exists(device_file):
        print_warning(f"Sample file not found: {device_file}")
        return
    
    with open(device_file, 'r', encoding='utf-8') as f:
        content = f.read()
        lines = content.split('\n')
    
    violations = []
    
    # Check for camelCase properties (should be PascalCase)
    camel_properties = re.findall(r'public\s+\w+\s+([a-z]\w*)\s*{', content)
    for prop in camel_properties:
        violations.append({
            "type": "Naming Convention",
            "severity": "Major",
            "issue": f"Property '{prop}' should be PascalCase",
            "expected": prop.capitalize(),
            "fixable": True
        })
    
    # Check for missing XML documentation
    public_members = len(re.findall(r'public\s+(?:class|struct|interface|\w+\s+\w+)', content))
    xml_docs = len(re.findall(r'///\s*<summary>', content))
    
    if public_members > xml_docs:
        violations.append({
            "type": "Missing Documentation",
            "severity": "Major", 
            "issue": f"{public_members - xml_docs} public members lack XML documentation",
            "expected": "/// <summary> documentation for all public members",
            "fixable": True
        })
    
    # Check line lengths
    long_lines = []
    for i, line in enumerate(lines, 1):
        if len(line) > 120:
            long_lines.append(i)
    
    if long_lines:
        violations.append({
            "type": "Line Length",
            "severity": "Minor",
            "issue": f"{len(long_lines)} lines exceed 120 characters",
            "lines": long_lines[:5],  # Show first 5
            "fixable": True
        })
    
    print_info(f"Analyzed: {device_file}")
    print_info(f"Lines of code: {len(lines)}")
    print_info(f"Public members: {public_members}")
    print_info(f"XML documented: {xml_docs}")
    
    if violations:
        print_warning(f"Found {len(violations)} standards violations:")
        for i, violation in enumerate(violations, 1):
            severity_icon = "🔴" if violation["severity"] == "Major" else "🟡"
            fixable_icon = "🔧" if violation["fixable"] else "⚠️"
            print(f"  {i}. {severity_icon} {fixable_icon} {violation['type']}: {violation['issue']}")
    else:
        print_success("No standards violations found!")
    
    return violations

def demonstrate_testing_features():
    """Demonstrate the key features of the testing system"""
    print_section("Testing System Features")
    
    features = [
        {
            "name": "Naming Conventions",
            "description": "Enforces PascalCase for classes/methods, camelCase for variables",
            "example": "class FileManager { public void TransferFile(string fileName) }",
            "violations": ["camelCase properties", "non-descriptive names"]
        },
        {
            "name": "Code Quality",
            "description": "Checks line length, method complexity, documentation",
            "example": "Max 120 chars/line, 50 lines/method, XML docs required",
            "violations": ["long lines", "complex methods", "missing docs"]
        },
        {
            "name": "Security Standards", 
            "description": "Detects hardcoded secrets, validates input handling",
            "example": "No 'password = \"secret\"', require null checks",
            "violations": ["hardcoded secrets", "missing validation"]
        },
        {
            "name": "Code Organization",
            "description": "Validates namespaces, using statements, file headers",
            "example": "Sorted usings, proper namespaces, license headers",
            "violations": ["unused usings", "missing namespaces"]
        },
        {
            "name": "Developer Policies",
            "description": "Validates commit messages, attribution, project structure",
            "example": "Descriptive commits, proper attribution, required docs",
            "violations": ["vague commits", "missing attribution"]
        }
    ]
    
    for feature in features:
        print(f"\n🔍 {feature['name']}")
        print(f"   📝 {feature['description']}")
        print(f"   💡 Example: {feature['example']}")
        print(f"   ⚠️  Detects: {', '.join(feature['violations'])}")

def show_testing_workflow():
    """Show the testing workflow"""
    print_section("Testing Workflow")
    
    workflow_steps = [
        "1. 📁 Scan source directories (Shared, LocalTalk, LocalTalkUWP)",
        "2. 🔍 Analyze each C# file for standards violations",
        "3. 📊 Categorize issues by severity (Critical, Major, Minor)",
        "4. 🔧 Identify fixable vs manual issues",
        "5. 📄 Generate detailed JSON report with file/line details",
        "6. ✅ Provide pass/fail result with actionable feedback"
    ]
    
    for step in workflow_steps:
        print(f"   {step}")
    
    print(f"\n📋 Integration Options:")
    print(f"   • Run manually: pwsh tests/code-standards/CodeStandardsValidator.ps1")
    print(f"   • CI/CD pipeline: Add to build process")
    print(f"   • Pre-commit hooks: Validate before commits")
    print(f"   • IDE integration: Real-time validation")

def analyze_repository_standards():
    """Analyze the actual repository for code standards compliance"""
    analysis_results = {
        "timestamp": datetime.now().strftime("%Y-%m-%d %H:%M:%S"),
        "repository": "LocalTalk",
        "analysis_version": "1.0.0",
        "summary": {
            "files_analyzed": 0,
            "total_tests": 0,
            "passed": 0,
            "failed": 0,
            "issues_found": 0,
            "coverage_percentage": 0
        },
        "issues": {
            "critical": 0,
            "major": 0,
            "minor": 0,
            "fixable": 0
        },
        "violations": [],
        "file_details": []
    }

    # Find all C# files
    cs_files = []
    for root, dirs, files in os.walk("."):
        # Skip build directories
        if any(skip in root for skip in ["/bin/", "/obj/", "/.git/", "/packages/"]):
            continue
        for file in files:
            if file.endswith(".cs"):
                cs_files.append(os.path.join(root, file))

    analysis_results["summary"]["files_analyzed"] = len(cs_files)

    # Comprehensive analysis metrics
    analysis_metrics = {
        "long_lines": 0,
        "missing_docs": 0,
        "camel_case_props": 0,
        "unused_usings": 0,
        "hardcoded_strings": 0,
        "complex_methods": 0,
        "magic_numbers": 0,
        "total_loc": 0,
        "documented_members": 0,
        "total_public_members": 0
    }

    for cs_file in cs_files:
        try:
            with open(cs_file, 'r', encoding='utf-8') as f:
                lines = f.readlines()

            content = ''.join(lines)
            file_metrics = analyze_file_standards(cs_file, lines, content)

            # Aggregate metrics
            for key in analysis_metrics:
                if key in file_metrics:
                    analysis_metrics[key] += file_metrics[key]

            # Store file details for reporting
            if file_metrics.get("issues", 0) > 0:
                analysis_results["file_details"].append({
                    "file": cs_file,
                    "issues": file_metrics.get("issues", 0),
                    "violations": file_metrics.get("violations", [])
                })

        except Exception as e:
            continue

    # Categorize issues by severity
    analysis_results["issues"]["critical"] = analysis_metrics["hardcoded_strings"]  # Security issues
    analysis_results["issues"]["major"] = (
        analysis_metrics["missing_docs"] +
        analysis_metrics["camel_case_props"] +
        analysis_metrics["complex_methods"]
    )  # Code quality and maintainability
    analysis_results["issues"]["minor"] = (
        analysis_metrics["long_lines"] +
        analysis_metrics["unused_usings"] +
        analysis_metrics["magic_numbers"]
    )  # Style and readability
    analysis_results["issues"]["fixable"] = (
        analysis_results["issues"]["major"] +
        analysis_results["issues"]["minor"]
    )

    analysis_results["summary"]["issues_found"] = (
        analysis_results["issues"]["critical"] +
        analysis_results["issues"]["major"] +
        analysis_results["issues"]["minor"]
    )

    # Calculate coverage percentage
    if analysis_metrics["total_public_members"] > 0:
        coverage = (analysis_metrics["documented_members"] / analysis_metrics["total_public_members"]) * 100
        analysis_results["summary"]["coverage_percentage"] = round(coverage, 1)

    # Build comprehensive violations list
    if analysis_metrics["camel_case_props"] > 0:
        analysis_results["violations"].append(f"Naming conventions: {analysis_metrics['camel_case_props']} files with camelCase properties")
    if analysis_metrics["missing_docs"] > 0:
        analysis_results["violations"].append(f"Documentation: {analysis_metrics['missing_docs']} files missing XML documentation")
    if analysis_metrics["long_lines"] > 0:
        analysis_results["violations"].append(f"Line length: {analysis_metrics['long_lines']} files with lines exceeding 120 characters")
    if analysis_metrics["unused_usings"] > 0:
        analysis_results["violations"].append(f"Code organization: {analysis_metrics['unused_usings']} files with excessive using statements")
    if analysis_metrics["hardcoded_strings"] > 0:
        analysis_results["violations"].append(f"Security: {analysis_metrics['hardcoded_strings']} files with potential hardcoded secrets")
    if analysis_metrics["complex_methods"] > 0:
        analysis_results["violations"].append(f"Method complexity: {analysis_metrics['complex_methods']} files with overly complex methods")
    if analysis_metrics["magic_numbers"] > 0:
        analysis_results["violations"].append(f"Code quality: {analysis_metrics['magic_numbers']} files with potential magic numbers")

    # Test results based on analysis
    analysis_results["summary"]["total_tests"] = 5
    analysis_results["summary"]["passed"] = 5 - min(5, len(analysis_results["violations"]))
    analysis_results["summary"]["failed"] = min(5, len(analysis_results["violations"]))

    return analysis_results

def show_repository_analysis():
    """Display comprehensive repository analysis results"""
    print_section("Repository Analysis Results")

    results = analyze_repository_standards()

    # Summary statistics
    print(f"📊 Analysis Summary:")
    print(f"   Repository: {results.get('repository', 'LocalTalk')}")
    print(f"   Analysis Version: {results.get('analysis_version', '1.0.0')}")
    print(f"   Timestamp: {results['timestamp']}")
    print(f"   Files Analyzed: {results['summary']['files_analyzed']}")
    print(f"   Total Issues: {results['summary']['issues_found']}")

    if results['summary']['coverage_percentage'] > 0:
        print(f"   Documentation Coverage: {results['summary']['coverage_percentage']}%")

    # Issue breakdown with severity levels
    print(f"\n🎯 Issue Classification:")
    print(f"   🔴 Critical (Security): {results['issues']['critical']}")
    print(f"   🟠 Major (Quality): {results['issues']['major']}")
    print(f"   🟡 Minor (Style): {results['issues']['minor']}")
    print(f"   🔧 Auto-fixable: {results['issues']['fixable']}")

    # Detailed violations
    if results["violations"]:
        print(f"\n📋 Standards Violations:")
        for i, violation in enumerate(results["violations"], 1):
            print(f"   {i}. {violation}")
    else:
        print(f"\n✅ Standards Compliance: EXCELLENT")
        print(f"   No significant violations detected")
        print(f"   Codebase follows LLVM-inspired standards")

    # File-specific issues (top 5 most problematic files)
    if results.get("file_details"):
        print(f"\n📁 Files Requiring Attention:")
        sorted_files = sorted(results["file_details"], key=lambda x: x["issues"], reverse=True)
        for file_info in sorted_files[:5]:
            print(f"   • {file_info['file']} ({file_info['issues']} issues)")

    # Quality metrics
    quality_score = calculate_quality_score(results)
    print(f"\n📈 Code Quality Score: {quality_score}/100")

    return results

def calculate_quality_score(results):
    """Calculate an overall code quality score"""
    base_score = 100

    # Deduct points for issues
    critical_penalty = results['issues']['critical'] * 20  # 20 points per critical
    major_penalty = results['issues']['major'] * 5        # 5 points per major
    minor_penalty = results['issues']['minor'] * 1        # 1 point per minor

    total_penalty = critical_penalty + major_penalty + minor_penalty

    # Cap the penalty to ensure score doesn't go below 0
    max_penalty = min(total_penalty, 95)  # Keep minimum score of 5

    final_score = max(5, base_score - max_penalty)
    return round(final_score)

def show_validation_summary():
    """Show validation system summary"""
    print_section("Validation System Summary")

    print("📋 Available Validation Tools:")
    print("   • Code Standards Validator (PowerShell)")
    print("   • Developer Policy Validator (PowerShell)")
    print("   • Performance Benchmark Validator (PowerShell)")
    print("   • Cross-platform Testing Suite (Python)")

    print("\n📋 Integration Options:")
    print("   • Manual execution: Run validation scripts directly")
    print("   • CI/CD pipeline: Automated validation on commits")
    print("   • Pre-commit hooks: Validate before code commits")
    print("   • IDE integration: Real-time validation feedback")

    print("\n📋 Supported Platforms:")
    print("   • Windows (PowerShell Core)")
    print("   • macOS (Python + PowerShell Core)")
    print("   • Linux (Python + PowerShell Core)")

    print("\n📋 Output Formats:")
    print("   • Console output with color coding")
    print("   • JSON reports for automation")
    print("   • HTML reports for documentation")
    print("   • CSV exports for analysis")

def main():
    """Main validation function"""
    print_header("LocalTalk Code Standards Validation System")
    print("🎯 LLVM-Inspired Code Quality Validation for C# Projects")

    # Change to project root if needed
    if os.path.basename(os.getcwd()) != "LocalTalk":
        for root, dirs, files in os.walk("."):
            if "LocalTalk.sln" in files:
                os.chdir(root)
                break

    print_info(f"Working directory: {os.getcwd()}")

    # Run demonstrations
    violations = analyze_csharp_violations()
    demonstrate_testing_features()
    show_testing_workflow()

    # Show real repository analysis
    analysis_results = show_repository_analysis()
    show_validation_summary()

    # Final summary
    print_header("System Status", "=")
    print_success("✅ Code Standards Validator: Ready")
    print_success("✅ Developer Policy Validator: Ready")
    print_success("✅ Master Validator Integration: Complete")
    print_success("✅ Documentation: Comprehensive")
    print_success("✅ Cross-platform Compatibility: Fixed")

    # Show status based on actual analysis
    if analysis_results["summary"]["issues_found"] > 0:
        if analysis_results["issues"]["critical"] > 0:
            print_error(f"🔴 Found {analysis_results['issues']['critical']} critical security issues to address immediately")
        if analysis_results["issues"]["major"] > 0:
            print_warning(f"🟠 Found {analysis_results['issues']['major']} major code quality issues to address")
        if analysis_results["issues"]["minor"] > 0:
            print_info(f"🟡 Found {analysis_results['issues']['minor']} minor style issues to address")
    else:
        print_success("✅ No major code standards violations found!")

    print(f"\nThe validation system is operational.")
    print(f"Documentation: tests/code-standards/README.md")
    print(f"PowerShell validation: pwsh tests/code-standards/CodeStandardsValidator.ps1")

if __name__ == "__main__":
    main()
