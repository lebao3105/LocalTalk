# Code Standards Validation Script
# This script validates C# code against adapted LLVM coding standards principles
# Ensures code quality, maintainability, and consistency across the LocalTalk codebase

param(
    [string[]]$SourcePaths = @("Shared", "LocalTalk", "LocalTalkUWP"),
    [string]$TestResultsPath = "tests/results/code-standards-$(Get-Date -Format 'yyyyMMdd-HHmmss').json",
    [switch]$Verbose = $false,
    [switch]$FixableIssues = $false,
    [string[]]$ExcludePatterns = @("obj/*", "bin/*", "packages/*", "*.Designer.cs", "*.g.cs")
)

$ErrorActionPreference = "Stop"
$VerbosePreference = if ($Verbose) { "Continue" } else { "SilentlyContinue" }

Write-Host "LocalTalk Code Standards Validation" -ForegroundColor Green
Write-Host "===================================" -ForegroundColor Green

# Configuration based on adapted LLVM standards for C#
$CodeStandards = @{
    # Naming Conventions (adapted from LLVM to C# standards)
    NamingConventions = @{
        Classes = "PascalCase"           # e.g., FileTransferManager
        Interfaces = "IPascalCase"       # e.g., IFileTransferProtocol
        Methods = "PascalCase"           # e.g., TransferFile()
        Properties = "PascalCase"        # e.g., TransferSpeed
        Fields = "camelCase"             # e.g., transferSpeed (private)
        Constants = "PascalCase"         # e.g., MaxTransferSize
        Parameters = "camelCase"         # e.g., fileName
        LocalVariables = "camelCase"     # e.g., fileSize
    }
    
    # Code Quality Standards
    QualityStandards = @{
        MaxLineLength = 120              # Adapted from LLVM's 80 to modern C# standards
        MaxMethodLength = 50             # Lines per method
        MaxClassLength = 500             # Lines per class
        MaxParameterCount = 5            # Parameters per method
        RequireDocumentation = $true     # XML documentation required
        RequireErrorHandling = $true     # Exception handling required
        ProhibitMagicNumbers = $true     # Use named constants
        RequireNullChecks = $true        # Null parameter validation
    }
    
    # Security Standards (adapted from LLVM security practices)
    SecurityStandards = @{
        ProhibitHardcodedSecrets = $true
        RequireInputValidation = $true
        ProhibitSqlInjection = $true
        RequireSecureRandom = $true
        ProhibitWeakCrypto = $true
    }
    
    # Code Organization (adapted from LLVM structure principles)
    OrganizationStandards = @{
        RequireNamespaces = $true
        RequireUsingSorting = $true
        ProhibitUnusedUsings = $true
        RequireRegionOrganization = $false  # Optional for C#
        RequireFileHeaders = $true
    }
}

$ValidationResults = @{
    Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    SourcePaths = $SourcePaths
    Standards = $CodeStandards
    Tests = @{}
    Issues = @{
        Critical = @()
        Major = @()
        Minor = @()
        Fixable = @()
    }
    Summary = @{
        Total = 0
        Passed = 0
        Failed = 0
        FilesAnalyzed = 0
        IssuesFound = 0
    }
}

# Ensure results directory exists and fix path separators for cross-platform compatibility
$TestResultsPath = $TestResultsPath -replace '\\', [System.IO.Path]::DirectorySeparatorChar
$ResultsDir = Split-Path $TestResultsPath -Parent
if (!(Test-Path $ResultsDir)) {
    New-Item -ItemType Directory -Path $ResultsDir -Force | Out-Null
}

function Write-TestResult {
    param(
        [string]$TestName,
        [string]$Status,
        [string]$Message = "",
        [object]$Details = $null,
        [string]$Severity = "Major"
    )
    
    $ValidationResults.Tests[$TestName] = @{
        Status = $Status
        Message = $Message
        Details = $Details
        Severity = $Severity
        Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    }
    
    $ValidationResults.Summary.Total++
    switch ($Status) {
        "PASS" { 
            $ValidationResults.Summary.Passed++
            Write-Host "✅ $TestName - PASS" -ForegroundColor Green
        }
        "FAIL" { 
            $ValidationResults.Summary.Failed++
            $ValidationResults.Summary.IssuesFound++
            
            # Categorize issues by severity
            $issue = @{
                Test = $TestName
                Message = $Message
                Details = $Details
                File = $Details.File
                Line = $Details.Line
            }
            
            switch ($Severity) {
                "Critical" { $ValidationResults.Issues.Critical += $issue }
                "Major" { $ValidationResults.Issues.Major += $issue }
                "Minor" { $ValidationResults.Issues.Minor += $issue }
            }
            
            if ($Details.Fixable) {
                $ValidationResults.Issues.Fixable += $issue
            }
            
            $severityColor = switch ($Severity) {
                "Critical" { "Magenta" }
                "Major" { "Red" }
                "Minor" { "Yellow" }
                default { "Red" }
            }
            
            Write-Host "❌ $TestName - FAIL [$Severity]: $Message" -ForegroundColor $severityColor
        }
    }
    
    if ($Details -and $Verbose) {
        Write-Verbose "Details: $($Details | ConvertTo-Json -Depth 2)"
    }
}

function Get-CSharpFiles {
    $allFiles = @()
    
    foreach ($sourcePath in $SourcePaths) {
        if (Test-Path $sourcePath) {
            $files = Get-ChildItem $sourcePath -Recurse -Filter "*.cs"
            
            # Apply exclusion patterns (cross-platform compatible)
            foreach ($pattern in $ExcludePatterns) {
                $normalizedPattern = $pattern -replace '\\', [System.IO.Path]::DirectorySeparatorChar
                $files = $files | Where-Object {
                    $normalizedPath = $_.FullName -replace '\\', [System.IO.Path]::DirectorySeparatorChar
                    $normalizedPath -notlike "*$normalizedPattern*"
                }
            }
            
            $allFiles += $files
        } else {
            Write-Warning "Source path not found: $sourcePath"
        }
    }
    
    return $allFiles
}

function Test-NamingConventions {
    Write-Host "`nValidating Naming Conventions..." -ForegroundColor Cyan
    
    $files = Get-CSharpFiles
    $namingIssues = @()
    
    foreach ($file in $files) {
        Write-Verbose "Analyzing naming conventions in: $($file.Name)"
        $content = Get-Content $file.FullName -Raw
        $lines = Get-Content $file.FullName
        
        # Check class names (should be PascalCase)
        $classMatches = [regex]::Matches($content, "(?:public\s+|internal\s+|private\s+)?(?:static\s+)?(?:abstract\s+)?(?:sealed\s+)?(?:class|struct)\s+(\w+)")
        foreach ($match in $classMatches) {
            $className = $match.Groups[1].Value
            if ($className -cnotmatch "^[A-Z][a-zA-Z0-9]*$") {
                $lineNumber = ($content.Substring(0, $match.Index) -split "`n").Count
                $namingIssues += @{
                    File = $file.FullName
                    Line = $lineNumber
                    Type = "Class/Struct"
                    Name = $className
                    Expected = "PascalCase"
                    Fixable = $true
                }
            }
        }

        # Check property names (should be PascalCase) - including auto-properties
        $propertyMatches = [regex]::Matches($content, "(?:public\s+|private\s+|protected\s+|internal\s+)(?:static\s+)?(?:virtual\s+)?(?:override\s+)?(?:\w+\s+)?(\w+)\s*{\s*(?:get|set)")
        foreach ($match in $propertyMatches) {
            $propertyName = $match.Groups[1].Value
            if ($propertyName -cnotmatch "^[A-Z][a-zA-Z0-9]*$") {
                $lineNumber = ($content.Substring(0, $match.Index) -split "`n").Count
                $namingIssues += @{
                    File = $file.FullName
                    Line = $lineNumber
                    Type = "Property"
                    Name = $propertyName
                    Expected = "PascalCase"
                    Fixable = $true
                }
            }
        }
        
        # Check interface names (should be IPascalCase)
        $interfaceMatches = [regex]::Matches($content, "(?:public\s+|internal\s+)?interface\s+(\w+)")
        foreach ($match in $interfaceMatches) {
            $interfaceName = $match.Groups[1].Value
            if ($interfaceName -cnotmatch "^I[A-Z][a-zA-Z0-9]*$") {
                $lineNumber = ($content.Substring(0, $match.Index) -split "`n").Count
                $namingIssues += @{
                    File = $file.FullName
                    Line = $lineNumber
                    Type = "Interface"
                    Name = $interfaceName
                    Expected = "IPascalCase (starting with I)"
                    Fixable = $true
                }
            }
        }
        
        # Check method names (should be PascalCase)
        $methodMatches = [regex]::Matches($content, "(?:public\s+|private\s+|protected\s+|internal\s+)(?:static\s+)?(?:virtual\s+)?(?:override\s+)?(?:async\s+)?(?:\w+\s+)?(\w+)\s*\([^)]*\)\s*{")
        foreach ($match in $methodMatches) {
            $methodName = $match.Groups[1].Value
            # Skip constructors and special methods
            if ($methodName -notmatch "^(get_|set_|add_|remove_)" -and $methodName -cnotmatch "^[A-Z][a-zA-Z0-9]*$") {
                $lineNumber = ($content.Substring(0, $match.Index) -split "`n").Count
                $namingIssues += @{
                    File = $file.FullName
                    Line = $lineNumber
                    Type = "Method"
                    Name = $methodName
                    Expected = "PascalCase"
                    Fixable = $true
                }
            }
        }
    }
    
    $ValidationResults.Summary.FilesAnalyzed = $files.Count
    
    if ($namingIssues.Count -eq 0) {
        Write-TestResult "Naming Conventions" "PASS" "All naming conventions follow standards"
        return $true
    } else {
        $message = "$($namingIssues.Count) naming convention violations found"
        Write-TestResult "Naming Conventions" "FAIL" $message -Details @{ Issues = $namingIssues; Fixable = $true } -Severity "Major"
        return $false
    }
}

function Test-CodeQuality {
    Write-Host "`nValidating Code Quality Standards..." -ForegroundColor Cyan

    $files = Get-CSharpFiles
    $qualityIssues = @()

    foreach ($file in $files) {
        Write-Verbose "Analyzing code quality in: $($file.Name)"
        $lines = Get-Content $file.FullName
        $content = Get-Content $file.FullName -Raw

        # Check line length
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i].Length -gt $CodeStandards.QualityStandards.MaxLineLength) {
                $qualityIssues += @{
                    File = $file.FullName
                    Line = $i + 1
                    Type = "Line Length"
                    Issue = "Line exceeds $($CodeStandards.QualityStandards.MaxLineLength) characters ($($lines[$i].Length))"
                    Fixable = $true
                }
            }
        }

        # Check method length
        $methodMatches = [regex]::Matches($content, "(?:public\s+|private\s+|protected\s+|internal\s+)(?:static\s+)?(?:virtual\s+)?(?:override\s+)?(?:async\s+)?(?:\w+\s+)?(\w+)\s*\([^)]*\)\s*{([^{}]*(?:{[^{}]*}[^{}]*)*?)}", [System.Text.RegularExpressions.RegexOptions]::Singleline)
        foreach ($match in $methodMatches) {
            $methodName = $match.Groups[1].Value
            $methodBody = $match.Groups[2].Value
            $methodLines = ($methodBody -split "`n").Count

            if ($methodLines -gt $CodeStandards.QualityStandards.MaxMethodLength) {
                $lineNumber = ($content.Substring(0, $match.Index) -split "`n").Count
                $qualityIssues += @{
                    File = $file.FullName
                    Line = $lineNumber
                    Type = "Method Length"
                    Issue = "Method '$methodName' has $methodLines lines (max: $($CodeStandards.QualityStandards.MaxMethodLength))"
                    Fixable = $false
                }
            }
        }

        # Check for magic numbers
        $magicNumberMatches = [regex]::Matches($content, "\b(?<!const\s+\w+\s*=\s*)(?<!case\s+)(?<!default\s*:)\d{2,}\b")
        foreach ($match in $magicNumberMatches) {
            $number = $match.Value
            # Skip common acceptable numbers
            if ($number -notin @("100", "200", "404", "500", "1000", "1024")) {
                $lineNumber = ($content.Substring(0, $match.Index) -split "`n").Count
                $qualityIssues += @{
                    File = $file.FullName
                    Line = $lineNumber
                    Type = "Magic Number"
                    Issue = "Magic number '$number' should be a named constant"
                    Fixable = $true
                }
            }
        }

        # Check for missing XML documentation on public members
        if ($CodeStandards.QualityStandards.RequireDocumentation) {
            $publicMemberMatches = [regex]::Matches($content, "public\s+(?:static\s+)?(?:virtual\s+)?(?:override\s+)?(?:class|interface|enum|struct|\w+\s+\w+\s*\(|\w+\s+\w+\s*{)")
            foreach ($match in $publicMemberMatches) {
                $beforeMatch = $content.Substring(0, $match.Index)
                $linesBefore = $beforeMatch -split "`n"
                $lineNumber = $linesBefore.Count

                # Check if there's XML documentation before this member
                $hasDocumentation = $false
                for ($j = $linesBefore.Count - 2; $j -ge 0 -and $j -ge $linesBefore.Count - 5; $j--) {
                    if ($lines[$j] -match "^\s*///") {
                        $hasDocumentation = $true
                        break
                    }
                    if ($lines[$j].Trim() -ne "" -and $lines[$j] -notmatch "^\s*\[") {
                        break
                    }
                }

                if (-not $hasDocumentation) {
                    $qualityIssues += @{
                        File = $file.FullName
                        Line = $lineNumber
                        Type = "Missing Documentation"
                        Issue = "Public member lacks XML documentation"
                        Fixable = $true
                    }
                }
            }
        }
    }

    if ($qualityIssues.Count -eq 0) {
        Write-TestResult "Code Quality Standards" "PASS" "All code quality standards met"
        return $true
    } else {
        $message = "$($qualityIssues.Count) code quality issues found"
        Write-TestResult "Code Quality Standards" "FAIL" $message -Details @{ Issues = $qualityIssues } -Severity "Major"
        return $false
    }
}

function Test-SecurityStandards {
    Write-Host "`nValidating Security Standards..." -ForegroundColor Cyan

    $files = Get-CSharpFiles
    $securityIssues = @()

    foreach ($file in $files) {
        Write-Verbose "Analyzing security in: $($file.Name)"
        $content = Get-Content $file.FullName -Raw
        $lines = Get-Content $file.FullName

        # Check for hardcoded secrets
        $secretPatterns = @(
            "password\s*=\s*[""'][^""']+[""']",
            "apikey\s*=\s*[""'][^""']+[""']",
            "secret\s*=\s*[""'][^""']+[""']",
            "token\s*=\s*[""'][^""']+[""']"
        )

        foreach ($pattern in $secretPatterns) {
            $matches = [regex]::Matches($content, $pattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
            foreach ($match in $matches) {
                $lineNumber = ($content.Substring(0, $match.Index) -split "`n").Count
                $securityIssues += @{
                    File = $file.FullName
                    Line = $lineNumber
                    Type = "Hardcoded Secret"
                    Issue = "Potential hardcoded secret detected"
                    Fixable = $false
                }
            }
        }

        # Check for SQL injection vulnerabilities
        $sqlPatterns = @(
            "SELECT\s+.*\+.*",
            "INSERT\s+.*\+.*",
            "UPDATE\s+.*\+.*",
            "DELETE\s+.*\+.*"
        )

        foreach ($pattern in $sqlPatterns) {
            $matches = [regex]::Matches($content, $pattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
            foreach ($match in $matches) {
                $lineNumber = ($content.Substring(0, $match.Index) -split "`n").Count
                $securityIssues += @{
                    File = $file.FullName
                    Line = $lineNumber
                    Type = "SQL Injection Risk"
                    Issue = "Potential SQL injection vulnerability (string concatenation in SQL)"
                    Fixable = $false
                }
            }
        }

        # Check for weak random number generation
        if ($content -match "new\s+Random\s*\(\s*\)") {
            $matches = [regex]::Matches($content, "new\s+Random\s*\(\s*\)")
            foreach ($match in $matches) {
                $lineNumber = ($content.Substring(0, $match.Index) -split "`n").Count
                $securityIssues += @{
                    File = $file.FullName
                    Line = $lineNumber
                    Type = "Weak Random"
                    Issue = "Use cryptographically secure random number generator for security-sensitive operations"
                    Fixable = $true
                }
            }
        }

        # Check for missing input validation on public methods
        $publicMethodMatches = [regex]::Matches($content, "public\s+(?:static\s+)?(?:virtual\s+)?(?:override\s+)?(?:async\s+)?(?:\w+\s+)?(\w+)\s*\(([^)]+)\)\s*{([^{}]*(?:{[^{}]*}[^{}]*)*?)}", [System.Text.RegularExpressions.RegexOptions]::Singleline)
        foreach ($match in $publicMethodMatches) {
            $methodName = $match.Groups[1].Value
            $parameters = $match.Groups[2].Value
            $methodBody = $match.Groups[3].Value

            # Skip constructors and property accessors
            if ($methodName -notmatch "^(get_|set_|add_|remove_)" -and $parameters.Trim() -ne "") {
                # Check if method body contains null checks or validation
                $hasValidation = $methodBody -match "(if\s*\(.*null|ArgumentNullException|ArgumentException|throw.*null)"

                if (-not $hasValidation) {
                    $lineNumber = ($content.Substring(0, $match.Index) -split "`n").Count
                    $securityIssues += @{
                        File = $file.FullName
                        Line = $lineNumber
                        Type = "Missing Input Validation"
                        Issue = "Public method '$methodName' lacks input validation"
                        Fixable = $true
                    }
                }
            }
        }
    }

    if ($securityIssues.Count -eq 0) {
        Write-TestResult "Security Standards" "PASS" "All security standards met"
        return $true
    } else {
        $criticalCount = ($securityIssues | Where-Object { $_.Type -in @("Hardcoded Secret", "SQL Injection Risk") }).Count
        $severity = if ($criticalCount -gt 0) { "Critical" } else { "Major" }
        $message = "$($securityIssues.Count) security issues found ($criticalCount critical)"
        Write-TestResult "Security Standards" "FAIL" $message -Details @{ Issues = $securityIssues } -Severity $severity
        return $false
    }
}

function Test-CodeOrganization {
    Write-Host "`nValidating Code Organization..." -ForegroundColor Cyan

    $files = Get-CSharpFiles
    $organizationIssues = @()

    foreach ($file in $files) {
        Write-Verbose "Analyzing code organization in: $($file.Name)"
        $content = Get-Content $file.FullName -Raw
        $lines = Get-Content $file.FullName

        # Check for proper namespace usage
        if ($CodeStandards.OrganizationStandards.RequireNamespaces) {
            if ($content -notmatch "namespace\s+\w+") {
                $organizationIssues += @{
                    File = $file.FullName
                    Line = 1
                    Type = "Missing Namespace"
                    Issue = "File should be wrapped in a namespace"
                    Fixable = $true
                }
            }
        }

        # Check using statement organization
        if ($CodeStandards.OrganizationStandards.RequireUsingSorting) {
            $usingMatches = [regex]::Matches($content, "using\s+([^;]+);")
            if ($usingMatches.Count -gt 1) {
                $usingStatements = @()
                foreach ($match in $usingMatches) {
                    $usingStatements += $match.Groups[1].Value.Trim()
                }

                $sortedUsings = $usingStatements | Sort-Object
                $isOrdered = $true
                for ($i = 0; $i -lt $usingStatements.Count; $i++) {
                    if ($usingStatements[$i] -ne $sortedUsings[$i]) {
                        $isOrdered = $false
                        break
                    }
                }

                if (-not $isOrdered) {
                    $organizationIssues += @{
                        File = $file.FullName
                        Line = 1
                        Type = "Using Statement Order"
                        Issue = "Using statements should be sorted alphabetically"
                        Fixable = $true
                    }
                }
            }
        }

        # Check for unused using statements
        if ($CodeStandards.OrganizationStandards.ProhibitUnusedUsings) {
            $usingMatches = [regex]::Matches($content, "using\s+([^;]+);")
            foreach ($match in $usingMatches) {
                $usingNamespace = $match.Groups[1].Value.Trim()
                $namespaceParts = $usingNamespace -split '\.'
                $lastPart = $namespaceParts[-1]

                # Simple check - look for usage of the namespace
                if ($content -notmatch "\b$lastPart\b" -and $content -notmatch "\b$usingNamespace\b") {
                    $lineNumber = ($content.Substring(0, $match.Index) -split "`n").Count
                    $organizationIssues += @{
                        File = $file.FullName
                        Line = $lineNumber
                        Type = "Unused Using"
                        Issue = "Unused using statement: $usingNamespace"
                        Fixable = $true
                    }
                }
            }
        }

        # Check for file headers (copyright, license info)
        if ($CodeStandards.OrganizationStandards.RequireFileHeaders) {
            $firstLines = $lines[0..4] -join "`n"
            if ($firstLines -notmatch "(copyright|license|author)" -and $firstLines -notmatch "//.*LocalTalk") {
                $organizationIssues += @{
                    File = $file.FullName
                    Line = 1
                    Type = "Missing File Header"
                    Issue = "File should include header with copyright/license information"
                    Fixable = $true
                }
            }
        }
    }

    if ($organizationIssues.Count -eq 0) {
        Write-TestResult "Code Organization" "PASS" "All code organization standards met"
        return $true
    } else {
        $message = "$($organizationIssues.Count) code organization issues found"
        Write-TestResult "Code Organization" "FAIL" $message -Details @{ Issues = $organizationIssues } -Severity "Minor"
        return $false
    }
}

function Test-ErrorHandling {
    Write-Host "`nValidating Error Handling Patterns..." -ForegroundColor Cyan

    $files = Get-CSharpFiles
    $errorHandlingIssues = @()

    foreach ($file in $files) {
        Write-Verbose "Analyzing error handling in: $($file.Name)"
        $content = Get-Content $file.FullName -Raw

        # Check for empty catch blocks
        $emptyCatchMatches = [regex]::Matches($content, "catch\s*\([^)]*\)\s*{\s*}")
        foreach ($match in $emptyCatchMatches) {
            $lineNumber = ($content.Substring(0, $match.Index) -split "`n").Count
            $errorHandlingIssues += @{
                File = $file.FullName
                Line = $lineNumber
                Type = "Empty Catch Block"
                Issue = "Empty catch block - should log error or handle appropriately"
                Fixable = $false
            }
        }

        # Check for generic Exception catching
        $genericCatchMatches = [regex]::Matches($content, "catch\s*\(\s*Exception\s+\w+\s*\)")
        foreach ($match in $genericCatchMatches) {
            $lineNumber = ($content.Substring(0, $match.Index) -split "`n").Count
            $errorHandlingIssues += @{
                File = $file.FullName
                Line = $lineNumber
                Type = "Generic Exception Catch"
                Issue = "Catching generic Exception - consider specific exception types"
                Fixable = $false
            }
        }

        # Check for methods that should have error handling but don't
        $riskyMethodPatterns = @(
            "File\.",
            "Directory\.",
            "HttpClient",
            "WebRequest",
            "Socket",
            "Stream"
        )

        foreach ($pattern in $riskyMethodPatterns) {
            $matches = [regex]::Matches($content, $pattern)
            foreach ($match in $matches) {
                # Check if this usage is within a try-catch block
                $beforeMatch = $content.Substring(0, $match.Index)
                $afterMatch = $content.Substring($match.Index)

                $tryCount = ($beforeMatch | Select-String "try\s*{" -AllMatches).Matches.Count
                $catchCount = ($beforeMatch | Select-String "catch\s*\(" -AllMatches).Matches.Count

                if ($tryCount -eq $catchCount) {
                    $lineNumber = ($beforeMatch -split "`n").Count
                    $errorHandlingIssues += @{
                        File = $file.FullName
                        Line = $lineNumber
                        Type = "Missing Error Handling"
                        Issue = "Risky operation '$pattern' should be wrapped in try-catch"
                        Fixable = $false
                    }
                }
            }
        }
    }

    if ($errorHandlingIssues.Count -eq 0) {
        Write-TestResult "Error Handling" "PASS" "All error handling patterns are appropriate"
        return $true
    } else {
        $message = "$($errorHandlingIssues.Count) error handling issues found"
        Write-TestResult "Error Handling" "FAIL" $message -Details @{ Issues = $errorHandlingIssues } -Severity "Major"
        return $false
    }
}

# Main execution
try {
    Write-Host "Starting code standards validation..." -ForegroundColor White
    Write-Host "Source paths: $($SourcePaths -join ', ')" -ForegroundColor Gray
    Write-Host "Standards configuration:" -ForegroundColor Gray
    Write-Host "  Max line length: $($CodeStandards.QualityStandards.MaxLineLength)" -ForegroundColor Gray
    Write-Host "  Max method length: $($CodeStandards.QualityStandards.MaxMethodLength)" -ForegroundColor Gray
    Write-Host "  Require documentation: $($CodeStandards.QualityStandards.RequireDocumentation)" -ForegroundColor Gray

    $success = $true

    # Run all validation tests
    $success = $success -and (Test-NamingConventions)
    $success = $success -and (Test-CodeQuality)
    $success = $success -and (Test-SecurityStandards)
    $success = $success -and (Test-CodeOrganization)
    $success = $success -and (Test-ErrorHandling)

    # Save results
    $ValidationResults | ConvertTo-Json -Depth 5 | Out-File -FilePath $TestResultsPath -Encoding UTF8

    # Summary
    Write-Host "`nValidation Summary:" -ForegroundColor White
    Write-Host "==================" -ForegroundColor White
    Write-Host "Files Analyzed: $($ValidationResults.Summary.FilesAnalyzed)" -ForegroundColor White
    Write-Host "Total Tests: $($ValidationResults.Summary.Total)" -ForegroundColor White
    Write-Host "Passed: $($ValidationResults.Summary.Passed)" -ForegroundColor Green
    Write-Host "Failed: $($ValidationResults.Summary.Failed)" -ForegroundColor Red
    Write-Host "Issues Found: $($ValidationResults.Summary.IssuesFound)" -ForegroundColor Yellow

    # Issue breakdown
    if ($ValidationResults.Summary.IssuesFound -gt 0) {
        Write-Host "`nIssue Breakdown:" -ForegroundColor White
        Write-Host "Critical: $($ValidationResults.Issues.Critical.Count)" -ForegroundColor Magenta
        Write-Host "Major: $($ValidationResults.Issues.Major.Count)" -ForegroundColor Red
        Write-Host "Minor: $($ValidationResults.Issues.Minor.Count)" -ForegroundColor Yellow
        Write-Host "Fixable: $($ValidationResults.Issues.Fixable.Count)" -ForegroundColor Cyan

        if ($FixableIssues -and $ValidationResults.Issues.Fixable.Count -gt 0) {
            Write-Host "`nFixable Issues:" -ForegroundColor Cyan
            foreach ($issue in $ValidationResults.Issues.Fixable) {
                Write-Host "  $($issue.File):$($issue.Line) - $($issue.Message)" -ForegroundColor Gray
            }
        }
    }

    Write-Host "Results saved to: $TestResultsPath" -ForegroundColor Gray

    if ($success -and $ValidationResults.Summary.Failed -eq 0) {
        Write-Host "`n✅ Code standards validation PASSED" -ForegroundColor Green
        exit 0
    } else {
        Write-Host "`n❌ Code standards validation FAILED" -ForegroundColor Red

        # Show critical issues
        if ($ValidationResults.Issues.Critical.Count -gt 0) {
            Write-Host "`nCritical Issues (must be fixed):" -ForegroundColor Magenta
            foreach ($issue in $ValidationResults.Issues.Critical) {
                Write-Host "  $($issue.File):$($issue.Line) - $($issue.Message)" -ForegroundColor Magenta
            }
        }

        exit 1
    }

    # Close remaining blocks
    }
    }
} catch {
    Write-Host "`n💥 Validation script error: $($_.Exception.Message)" -ForegroundColor Red
    Write-TestResult "Script Execution" "FAIL" $_.Exception.Message
    $ValidationResults | ConvertTo-Json -Depth 5 | Out-File -FilePath $TestResultsPath -Encoding UTF8
    exit 1
}
