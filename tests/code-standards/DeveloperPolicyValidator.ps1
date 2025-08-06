# Developer Policy Validation Script
# This script validates adherence to LLVM-inspired developer policies
# Ensures proper development practices, attribution, and quality standards

param(
    [string]$RepositoryPath = ".",
    [string]$TestResultsPath = "tests/results/developer-policy-$(Get-Date -Format 'yyyyMMdd-HHmmss').json",
    [switch]$Verbose = $false,
    [int]$CommitHistoryDepth = 50,
    [string[]]$RequiredFileHeaders = @("LICENSE", "README.md", "CONTRIBUTING.md")
)

$ErrorActionPreference = "Stop"
$VerbosePreference = if ($Verbose) { "Continue" } else { "SilentlyContinue" }

Write-Host "LocalTalk Developer Policy Validation" -ForegroundColor Green
Write-Host "====================================" -ForegroundColor Green

# Developer Policy Standards (adapted from LLVM)
$PolicyStandards = @{
    # Commit Message Standards
    CommitStandards = @{
        RequireDescriptiveTitle = $true
        MaxTitleLength = 72
        RequireBody = $false  # Optional for small changes
        RequireIssueReference = $false  # Optional
        ProhibitFixupCommits = $true
        RequireSignedCommits = $false  # Optional
    }
    
    # Attribution Standards
    AttributionStandards = @{
        RequireAuthorInfo = $true
        RequireCommitterInfo = $true
        ProhibitAnonymousCommits = $true
        RequireEmailValidation = $true
    }
    
    # Quality Standards
    QualityStandards = @{
        RequireTestCoverage = $true
        ProhibitDebugCode = $true
        RequireDocumentation = $true
        RequireLicenseHeaders = $false  # Optional for this project
        RequireChangelogUpdates = $false  # Optional
    }
    
    # Security Standards
    SecurityStandards = @{
        ProhibitSecretsInHistory = $true
        RequireSecurityReview = $false  # Optional
        ProhibitBinaryCommits = $true
        RequireVulnerabilityDisclosure = $false  # Optional
    }
    
    # Project Structure Standards
    StructureStandards = @{
        RequireProjectFiles = $true
        RequireDocumentationFiles = $true
        RequireTestStructure = $true
        RequireBuildScripts = $true
    }
}

$ValidationResults = @{
    Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    RepositoryPath = $RepositoryPath
    Standards = $PolicyStandards
    Tests = @{}
    PolicyViolations = @{
        Critical = @()
        Major = @()
        Minor = @()
    }
    Summary = @{
        Total = 0
        Passed = 0
        Failed = 0
        CommitsAnalyzed = 0
        ViolationsFound = 0
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
            $ValidationResults.Summary.ViolationsFound++
            
            # Categorize violations by severity
            $violation = @{
                Test = $TestName
                Message = $Message
                Details = $Details
            }
            
            switch ($Severity) {
                "Critical" { $ValidationResults.PolicyViolations.Critical += $violation }
                "Major" { $ValidationResults.PolicyViolations.Major += $violation }
                "Minor" { $ValidationResults.PolicyViolations.Minor += $violation }
            }
            
            $severityColor = switch ($Severity) {
                "Critical" { "Magenta" }
                "Major" { "Red" }
                "Minor" { "Yellow" }
                default { "Red" }
            }
            
            Write-Host "❌ $TestName - FAIL [$Severity]: $Message" -ForegroundColor $severityColor
        }
        "SKIP" {
            Write-Host "⏭️ $TestName - SKIP: $Message" -ForegroundColor Yellow
        }
    }
    
    if ($Details -and $Verbose) {
        Write-Verbose "Details: $($Details | ConvertTo-Json -Depth 2)"
    }
}

function Test-CommitMessageStandards {
    Write-Host "`nValidating Commit Message Standards..." -ForegroundColor Cyan
    
    try {
        # Check if we're in a git repository
        $gitCheck = git rev-parse --git-dir 2>$null
        if (-not $gitCheck) {
            Write-TestResult "Commit Message Standards" "SKIP" "Not a git repository"
            return $false
        }
        
        # Get recent commit messages
        $commits = git log --oneline -n $CommitHistoryDepth --pretty=format:"%H|%s|%b" 2>$null
        if (-not $commits) {
            Write-TestResult "Commit Message Standards" "SKIP" "No commit history found"
            return $false
        }
        
        $commitViolations = @()
        $commitsAnalyzed = 0
        
        foreach ($commitLine in $commits) {
            if ($commitLine.Trim() -eq "") { continue }
            
            $parts = $commitLine -split '\|', 3
            if ($parts.Count -lt 2) { continue }
            
            $hash = $parts[0]
            $title = $parts[1]
            $body = if ($parts.Count -gt 2) { $parts[2] } else { "" }
            
            $commitsAnalyzed++
            
            # Check title length
            if ($title.Length -gt $PolicyStandards.CommitStandards.MaxTitleLength) {
                $commitViolations += @{
                    Commit = $hash
                    Type = "Title Too Long"
                    Issue = "Title exceeds $($PolicyStandards.CommitStandards.MaxTitleLength) characters ($($title.Length))"
                    Title = $title
                }
            }
            
            # Check for descriptive title
            if ($PolicyStandards.CommitStandards.RequireDescriptiveTitle) {
                $nonDescriptivePatterns = @("fix", "update", "change", "modify", "wip", "temp", "test")
                $isNonDescriptive = $nonDescriptivePatterns | Where-Object { $title.ToLower().Trim() -eq $_ }
                
                if ($isNonDescriptive) {
                    $commitViolations += @{
                        Commit = $hash
                        Type = "Non-Descriptive Title"
                        Issue = "Commit title should be more descriptive than '$title'"
                        Title = $title
                    }
                }
            }
            
            # Check for fixup commits
            if ($PolicyStandards.CommitStandards.ProhibitFixupCommits) {
                if ($title -match "^(fixup|squash)!") {
                    $commitViolations += @{
                        Commit = $hash
                        Type = "Fixup Commit"
                        Issue = "Fixup commits should be squashed before merging"
                        Title = $title
                    }
                }
            }
        }
        
        $ValidationResults.Summary.CommitsAnalyzed = $commitsAnalyzed
        
        if ($commitViolations.Count -eq 0) {
            Write-TestResult "Commit Message Standards" "PASS" "All $commitsAnalyzed commits follow message standards"
            return $true
        } else {
            $message = "$($commitViolations.Count) commit message violations found in $commitsAnalyzed commits"
            Write-TestResult "Commit Message Standards" "FAIL" $message -Details @{ Violations = $commitViolations } -Severity "Minor"
            return $false
        }
    } catch {
        Write-TestResult "Commit Message Standards" "FAIL" "Error analyzing commits: $($_.Exception.Message)"
        return $false
    }
}

function Test-AttributionStandards {
    Write-Host "`nValidating Attribution Standards..." -ForegroundColor Cyan
    
    try {
        # Check if we're in a git repository
        $gitCheck = git rev-parse --git-dir 2>$null
        if (-not $gitCheck) {
            Write-TestResult "Attribution Standards" "SKIP" "Not a git repository"
            return $false
        }
        
        # Get commit author/committer information
        $commits = git log -n $CommitHistoryDepth --pretty=format:"%H|%an|%ae|%cn|%ce" 2>$null
        if (-not $commits) {
            Write-TestResult "Attribution Standards" "SKIP" "No commit history found"
            return $false
        }
        
        $attributionViolations = @()
        
        foreach ($commitLine in $commits) {
            if ($commitLine.Trim() -eq "") { continue }
            
            $parts = $commitLine -split '\|'
            if ($parts.Count -lt 5) { continue }
            
            $hash = $parts[0]
            $authorName = $parts[1]
            $authorEmail = $parts[2]
            $committerName = $parts[3]
            $committerEmail = $parts[4]
            
            # Check for anonymous commits
            if ($PolicyStandards.AttributionStandards.ProhibitAnonymousCommits) {
                if ($authorName -match "^(unknown|anonymous|user|root)$" -or $authorEmail -match "^(noreply|example\.com|localhost)") {
                    $attributionViolations += @{
                        Commit = $hash
                        Type = "Anonymous Commit"
                        Issue = "Commit has anonymous or invalid author information"
                        Author = "$authorName <$authorEmail>"
                    }
                }
            }
            
            # Check email validation
            if ($PolicyStandards.AttributionStandards.RequireEmailValidation) {
                if ($authorEmail -notmatch "^[^@]+@[^@]+\.[^@]+$") {
                    $attributionViolations += @{
                        Commit = $hash
                        Type = "Invalid Email"
                        Issue = "Author email format is invalid"
                        Author = "$authorName <$authorEmail>"
                    }
                }
            }
        }
        
        if ($attributionViolations.Count -eq 0) {
            Write-TestResult "Attribution Standards" "PASS" "All commits have proper attribution"
            return $true
        } else {
            $message = "$($attributionViolations.Count) attribution violations found"
            Write-TestResult "Attribution Standards" "FAIL" $message -Details @{ Violations = $attributionViolations } -Severity "Major"
            return $false
        }
    } catch {
        Write-TestResult "Attribution Standards" "FAIL" "Error analyzing attribution: $($_.Exception.Message)"
        return $false
    }
}

function Test-ProjectStructureStandards {
    Write-Host "`nValidating Project Structure Standards..." -ForegroundColor Cyan

    $structureViolations = @()

    try {
        # Check for required project files
    if ($PolicyStandards.StructureStandards.RequireProjectFiles) {
        $requiredProjectFiles = @("*.sln", "*.csproj", "*.shproj")
        foreach ($pattern in $requiredProjectFiles) {
            $files = Get-ChildItem $RepositoryPath -Filter $pattern -Recurse | Where-Object { $_.FullName -notlike "*\obj\*" -and $_.FullName -notlike "*\bin\*" }
            if ($files.Count -eq 0) {
                $structureViolations += @{
                    Type = "Missing Project Files"
                    Issue = "No $pattern files found in repository"
                    Pattern = $pattern
                }
            }
        }
    }

    # Check for required documentation files
    if ($PolicyStandards.StructureStandards.RequireDocumentationFiles) {
        foreach ($requiredFile in $RequiredFileHeaders) {
            $filePath = Join-Path $RepositoryPath $requiredFile
            if (!(Test-Path $filePath)) {
                $structureViolations += @{
                    Type = "Missing Documentation"
                    Issue = "Required file not found: $requiredFile"
                    File = $requiredFile
                }
            }
        }
    }

    # Check for test structure
    if ($PolicyStandards.StructureStandards.RequireTestStructure) {
        $testDirectories = @("test", "tests", "Test", "Tests", "*Test*", "*Tests*")
        $hasTestStructure = $false

        foreach ($pattern in $testDirectories) {
            $testDirs = Get-ChildItem $RepositoryPath -Directory -Filter $pattern -Recurse | Where-Object { $_.FullName -notlike "*\obj\*" -and $_.FullName -notlike "*\bin\*" }
            if ($testDirs.Count -gt 0) {
                $hasTestStructure = $true
                break
            }
        }

        if (-not $hasTestStructure) {
            $structureViolations += @{
                Type = "Missing Test Structure"
                Issue = "No test directories found in repository"
                ExpectedPatterns = $testDirectories
            }
        }
    }

    # Check for build scripts
    if ($PolicyStandards.StructureStandards.RequireBuildScripts) {
        $buildScripts = @("build.ps1", "build.cmd", "build.bat", "Makefile", "build-validation.ps1")
        $hasBuildScript = $false

        foreach ($script in $buildScripts) {
            $scriptPath = Join-Path $RepositoryPath $script
            if (Test-Path $scriptPath) {
                $hasBuildScript = $true
                break
            }
        }

        if (-not $hasBuildScript) {
            $structureViolations += @{
                Type = "Missing Build Scripts"
                Issue = "No build scripts found in repository"
                ExpectedFiles = $buildScripts
            }
        }
    }

        if ($structureViolations.Count -eq 0) {
            Write-TestResult "Project Structure Standards" "PASS" "All project structure requirements met"
            return $true
        } else {
            $message = "$($structureViolations.Count) project structure violations found"
            Write-TestResult "Project Structure Standards" "FAIL" $message -Details @{ Violations = $structureViolations } -Severity "Major"
            return $false
        }
    } catch {
        Write-TestResult "Project Structure Standards" "FAIL" "Error analyzing project structure: $($_.Exception.Message)"
        return $false
    }
}

function Test-SecurityPolicyStandards {
    Write-Host "`nValidating Security Policy Standards..." -ForegroundColor Cyan

    $securityViolations = @()

    try {
        # Check for secrets in git history
        if ($PolicyStandards.SecurityStandards.ProhibitSecretsInHistory) {
            $gitCheck = git rev-parse --git-dir 2>$null
            if ($gitCheck) {
                # Check for common secret patterns in commit history
                $secretPatterns = @(
                    "password\s*=\s*[""'][^""']+[""']",
                    "api[_-]?key\s*=\s*[""'][^""']+[""']",
                    "secret\s*=\s*[""'][^""']+[""']",
                    "token\s*=\s*[""'][^""']+[""']",
                    "-----BEGIN\s+(RSA\s+)?PRIVATE\s+KEY-----"
                )

                foreach ($pattern in $secretPatterns) {
                    $results = git log --all -p -S $pattern --pickaxe-regex 2>$null
                    if ($results) {
                        $securityViolations += @{
                            Type = "Secrets in History"
                            Issue = "Potential secrets found in git history matching pattern: $pattern"
                            Pattern = $pattern
                        }
                    }
                }
            }
        }

        # Check for binary files in commits
        if ($PolicyStandards.SecurityStandards.ProhibitBinaryCommits) {
            $binaryExtensions = @(".exe", ".dll", ".so", ".dylib", ".bin", ".zip", ".rar", ".7z")
            $binaryFiles = @()

            foreach ($ext in $binaryExtensions) {
                $files = Get-ChildItem $RepositoryPath -Recurse -Filter "*$ext" | Where-Object {
                    $_.FullName -notlike "*\obj\*" -and
                    $_.FullName -notlike "*\bin\*" -and
                    $_.FullName -notlike "*\packages\*"
                }
                $binaryFiles += $files
            }

            if ($binaryFiles.Count -gt 0) {
                $securityViolations += @{
                    Type = "Binary Files in Repository"
                    Issue = "$($binaryFiles.Count) binary files found in repository"
                    Files = $binaryFiles | ForEach-Object { $_.FullName }
                }
            }
        }

        if ($securityViolations.Count -eq 0) {
            Write-TestResult "Security Policy Standards" "PASS" "All security policy requirements met"
            return $true
        } else {
            $criticalCount = ($securityViolations | Where-Object { $_.Type -eq "Secrets in History" }).Count
            $severity = if ($criticalCount -gt 0) { "Critical" } else { "Major" }
            $message = "$($securityViolations.Count) security policy violations found"
            Write-TestResult "Security Policy Standards" "FAIL" $message -Details @{ Violations = $securityViolations } -Severity $severity
            return $false
        }
    } catch {
        Write-TestResult "Security Policy Standards" "FAIL" "Error analyzing security policies: $($_.Exception.Message)"
        return $false
    }
}

# Main execution
try {
    Write-Host "Starting developer policy validation..." -ForegroundColor White
    Write-Host "Repository path: $RepositoryPath" -ForegroundColor Gray
    Write-Host "Commit history depth: $CommitHistoryDepth" -ForegroundColor Gray

    $success = $true

    # Run all validation tests
    $success = $success -and (Test-CommitMessageStandards)
    $success = $success -and (Test-AttributionStandards)
    $success = $success -and (Test-ProjectStructureStandards)
    $success = $success -and (Test-SecurityPolicyStandards)

    # Save results
    $ValidationResults | ConvertTo-Json -Depth 5 | Out-File -FilePath $TestResultsPath -Encoding UTF8

    # Summary
    Write-Host "`nValidation Summary:" -ForegroundColor White
    Write-Host "==================" -ForegroundColor White
    Write-Host "Total Tests: $($ValidationResults.Summary.Total)" -ForegroundColor White
    Write-Host "Passed: $($ValidationResults.Summary.Passed)" -ForegroundColor Green
    Write-Host "Failed: $($ValidationResults.Summary.Failed)" -ForegroundColor Red
    Write-Host "Commits Analyzed: $($ValidationResults.Summary.CommitsAnalyzed)" -ForegroundColor White
    Write-Host "Violations Found: $($ValidationResults.Summary.ViolationsFound)" -ForegroundColor Yellow

    # Violation breakdown
    if ($ValidationResults.Summary.ViolationsFound -gt 0) {
        Write-Host "`nViolation Breakdown:" -ForegroundColor White
        Write-Host "Critical: $($ValidationResults.PolicyViolations.Critical.Count)" -ForegroundColor Magenta
        Write-Host "Major: $($ValidationResults.PolicyViolations.Major.Count)" -ForegroundColor Red
        Write-Host "Minor: $($ValidationResults.PolicyViolations.Minor.Count)" -ForegroundColor Yellow

        # Show critical violations
        if ($ValidationResults.PolicyViolations.Critical.Count -gt 0) {
            Write-Host "`nCritical Violations (must be addressed):" -ForegroundColor Magenta
            foreach ($violation in $ValidationResults.PolicyViolations.Critical) {
                Write-Host "  $($violation.Test): $($violation.Message)" -ForegroundColor Magenta
            }
        }
    }

    Write-Host "Results saved to: $TestResultsPath" -ForegroundColor Gray

    if ($success -and $ValidationResults.Summary.Failed -eq 0) {
        Write-Host "`n✅ Developer policy validation PASSED" -ForegroundColor Green
        exit 0
    } else {
        Write-Host "`n❌ Developer policy validation FAILED" -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host "`n💥 Validation script error: $($_.Exception.Message)" -ForegroundColor Red
    Write-TestResult "Script Execution" "FAIL" $_.Exception.Message
    $ValidationResults | ConvertTo-Json -Depth 5 | Out-File -FilePath $TestResultsPath -Encoding UTF8
    exit 1
}
