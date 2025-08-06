# Code Standards Testing

This directory contains testing scripts that enforce LLVM-inspired coding standards and developer policies adapted for the C# LocalTalk codebase.

## 🎯 Overview

The code standards testing ensures:
- **Code Quality**: Consistent naming, formatting, and structure
- **Security**: No hardcoded secrets, proper input validation
- **Maintainability**: Documentation, error handling, organization
- **Developer Practices**: Proper commit messages, attribution, project structure

## 📋 Testing Scripts

### CodeStandardsValidator.ps1

Validates C# code against adapted LLVM coding standards:

```powershell
.\tests\code-standards\CodeStandardsValidator.ps1 -SourcePaths @("Shared", "LocalTalk", "LocalTalkUWP") -Verbose
```

**Parameters:**
- `SourcePaths`: Array of source directories to analyze (default: Shared, LocalTalk, LocalTalkUWP)
- `TestResultsPath`: Output path for JSON results
- `Verbose`: Enable detailed output
- `FixableIssues`: Show issues that can be automatically fixed
- `ExcludePatterns`: Patterns to exclude from analysis

**Standards Checked:**

#### Naming Conventions
- **Classes**: PascalCase (e.g., `FileTransferManager`)
- **Interfaces**: IPascalCase (e.g., `IFileTransferProtocol`)
- **Methods**: PascalCase (e.g., `TransferFile()`)
- **Properties**: PascalCase (e.g., `TransferSpeed`)
- **Fields**: camelCase (e.g., `transferSpeed`)
- **Parameters**: camelCase (e.g., `fileName`)

#### Code Quality
- **Line Length**: Maximum 120 characters
- **Method Length**: Maximum 50 lines
- **Class Length**: Maximum 500 lines
- **Parameter Count**: Maximum 5 parameters per method
- **Documentation**: XML documentation required for public members
- **Magic Numbers**: Use named constants instead of magic numbers

#### Security Standards
- **No Hardcoded Secrets**: Detect potential passwords, API keys, tokens
- **Input Validation**: Public methods should validate parameters
- **SQL Injection**: Detect potential SQL injection vulnerabilities
- **Weak Cryptography**: Require cryptographically secure random generators

#### Code Organization
- **Namespaces**: All files should be wrapped in namespaces
- **Using Statements**: Should be sorted alphabetically
- **Unused Usings**: Remove unused using statements
- **File Headers**: Include copyright/license information

### DeveloperPolicyValidator.ps1

Validates adherence to LLVM-inspired developer policies:

```powershell
.\tests\code-standards\DeveloperPolicyValidator.ps1 -CommitHistoryDepth 50 -Verbose
```

**Parameters:**
- `RepositoryPath`: Path to git repository (default: current directory)
- `TestResultsPath`: Output path for JSON results
- `Verbose`: Enable detailed output
- `CommitHistoryDepth`: Number of recent commits to analyze
- `RequiredFileHeaders`: Required documentation files

**Policies Checked:**

#### Commit Message Standards
- **Title Length**: Maximum 72 characters
- **Descriptive Titles**: Avoid generic titles like "fix", "update"
- **No Fixup Commits**: Fixup commits should be squashed before merging

#### Attribution Standards
- **Author Information**: Valid author name and email
- **No Anonymous Commits**: Prevent commits with invalid attribution
- **Email Validation**: Proper email format required

#### Project Structure
- **Project Files**: Required .sln, .csproj, .shproj files
- **Documentation**: Required README.md, LICENSE files
- **Test Structure**: Test directories should exist
- **Build Scripts**: Build automation scripts should be present

#### Security Policies
- **No Secrets in History**: Scan git history for potential secrets
- **No Binary Commits**: Prevent binary files in repository
- **Vulnerability Disclosure**: Security review processes

## 🚀 Quick Start

### Run Code Standards Validation
```powershell
# Basic validation
.\tests\code-standards\CodeStandardsValidator.ps1

# Detailed validation with fixable issues
.\tests\code-standards\CodeStandardsValidator.ps1 -Verbose -FixableIssues

# Custom source paths
.\tests\code-standards\CodeStandardsValidator.ps1 -SourcePaths @("Shared") -Verbose
```

### Run Developer Policy Validation
```powershell
# Basic policy validation
.\tests\code-standards\DeveloperPolicyValidator.ps1

# Extended commit history analysis
.\tests\code-standards\DeveloperPolicyValidator.ps1 -CommitHistoryDepth 100 -Verbose
```

### Run Both Validations
```powershell
# Through master validator
.\tests\MasterAssumptionValidator.ps1 -TestCategories @("CodeStandardsCompliance", "DeveloperPolicyCompliance") -Verbose
```

## 📊 Results and Reporting

### Issue Severity Levels
- **Critical**: Security vulnerabilities, secrets in code
- **Major**: Naming violations, missing documentation, attribution issues
- **Minor**: Code organization, formatting issues

### Fixable Issues
Many issues can be automatically fixed:
- Naming convention violations
- Using statement ordering
- Missing file headers
- Line length issues (with code reformatting)

### JSON Output Format
```json
{
  "Timestamp": "2024-01-15 10:30:00",
  "Summary": {
    "Total": 15,
    "Passed": 12,
    "Failed": 3,
    "FilesAnalyzed": 45,
    "IssuesFound": 8
  },
  "Issues": {
    "Critical": [],
    "Major": [
      {
        "File": "Shared/Protocol/ChunkedTransferProtocol.cs",
        "Line": 25,
        "Type": "Naming Convention",
        "Issue": "Method name should be PascalCase"
      }
    ],
    "Minor": [],
    "Fixable": []
  }
}
```

## 🛠️ Integration

### CI/CD Integration
Add to your build pipeline:
```yaml
- name: Validate Code Standards
  run: |
    .\tests\code-standards\CodeStandardsValidator.ps1 -Verbose
    .\tests\code-standards\DeveloperPolicyValidator.ps1 -Verbose
  shell: powershell
```

### Pre-commit Hooks
Create a pre-commit hook to run validation:
```powershell
# .git/hooks/pre-commit
.\tests\code-standards\CodeStandardsValidator.ps1 -SourcePaths @("Shared")
if ($LASTEXITCODE -ne 0) {
    Write-Host "Code standards validation failed. Please fix issues before committing."
    exit 1
}
```

## 🔧 Customization

### Adjusting Standards
Modify the `$CodeStandards` configuration in `CodeStandardsValidator.ps1`:
```powershell
$CodeStandards = @{
    QualityStandards = @{
        MaxLineLength = 100              # Reduce from 120
        MaxMethodLength = 30             # Reduce from 50
        RequireDocumentation = $false    # Make optional
    }
}
```

### Adding Custom Checks
Extend the validation functions:
```powershell
function Test-CustomStandards {
    # Add your custom validation logic
    # Return $true for pass, $false for fail
}
```

## 📞 Support

For issues with code standards validation:
1. Check the generated JSON results for detailed error information
2. Run with `-Verbose` for additional diagnostic information
3. Review the specific standards being enforced
4. Consider adjusting standards for project-specific requirements

The code standards validation helps maintain high code quality and consistency across the LocalTalk codebase while following industry best practices inspired by the LLVM project.
