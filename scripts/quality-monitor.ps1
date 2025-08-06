# LocalTalk Quality Monitoring Script
# Comprehensive quality monitoring and reporting for sustained code quality

param(
    [string]$SourcePath = "Shared",
    [string]$ReportPath = "quality-reports",
    [switch]$GenerateDashboard = $false,
    [switch]$SendAlerts = $false,
    [string]$AlertThreshold = "Medium",
    [switch]$Verbose = $false
)

# Quality thresholds and targets
$QualityTargets = @{
    CodeQualityScore = 95
    DocumentationCoverage = 100
    SecurityVulnerabilities = 0
    PerformanceRegressions = 0
    TestCoverage = 90
    ComplexityScore = 85
}

# Alert thresholds
$AlertThresholds = @{
    Critical = @{
        CodeQualityScore = 80
        SecurityVulnerabilities = 1
        PerformanceRegressions = 1
    }
    High = @{
        CodeQualityScore = 85
        DocumentationCoverage = 90
        TestCoverage = 80
    }
    Medium = @{
        CodeQualityScore = 90
        DocumentationCoverage = 95
        TestCoverage = 85
    }
}

function Write-QualityLog {
    param([string]$Message, [string]$Level = "Info")
    
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $logMessage = "[$timestamp] [$Level] $Message"
    
    switch ($Level) {
        "Error" { Write-Host $logMessage -ForegroundColor Red }
        "Warning" { Write-Host $logMessage -ForegroundColor Yellow }
        "Success" { Write-Host $logMessage -ForegroundColor Green }
        default { Write-Host $logMessage -ForegroundColor White }
    }
    
    # Log to file
    $logFile = Join-Path $ReportPath "quality-monitor.log"
    $logMessage | Out-File -FilePath $logFile -Append -Encoding UTF8
}

function Get-CodeQualityMetrics {
    Write-QualityLog "Analyzing code quality metrics..." "Info"
    
    $metrics = @{
        FilesAnalyzed = 0
        LinesOfCode = 0
        DocumentationCoverage = 0
        CodeQualityScore = 0
        SecurityIssues = 0
        PerformanceIssues = 0
        ComplexityIssues = 0
        Violations = @()
    }
    
    try {
        # Get all C# files
        $csharpFiles = Get-ChildItem -Path $SourcePath -Filter "*.cs" -Recurse
        $metrics.FilesAnalyzed = $csharpFiles.Count
        
        $totalLines = 0
        $documentedMembers = 0
        $totalPublicMembers = 0
        $qualityIssues = @()
        
        foreach ($file in $csharpFiles) {
            $content = Get-Content $file.FullName -Raw
            $lines = Get-Content $file.FullName
            
            $totalLines += $lines.Count
            
            # Count XML documentation
            $xmlDocs = ([regex]::Matches($content, "///\s*<summary>")).Count
            $documentedMembers += $xmlDocs
            
            # Count public members
            $publicMembers = ([regex]::Matches($content, "public\s+(?:class|struct|interface|enum|\w+\s+\w+)")).Count
            $totalPublicMembers += $publicMembers
            
            # Check for quality issues
            $longLines = ($lines | Where-Object { $_.Length -gt 120 }).Count
            if ($longLines -gt 0) {
                $qualityIssues += @{
                    File = $file.Name
                    Type = "Line Length"
                    Count = $longLines
                    Severity = "Minor"
                }
            }
            
            # Check for magic numbers
            $magicNumbers = ([regex]::Matches($content, "\b(?<!const\s+\w+\s*=\s*)\d{3,}\b")).Count
            if ($magicNumbers -gt 5) {
                $qualityIssues += @{
                    File = $file.Name
                    Type = "Magic Numbers"
                    Count = $magicNumbers
                    Severity = "Medium"
                }
            }
            
            # Check for complex methods (basic heuristic)
            $complexMethods = ([regex]::Matches($content, "(?:public|private|protected|internal)\s+(?:static\s+)?(?:virtual\s+)?(?:override\s+)?(?:async\s+)?\w+\s+\w+\s*\([^)]*\)\s*{([^{}]*(?:{[^{}]*}[^{}]*)*)}")).Count
            if ($complexMethods -gt 0) {
                $qualityIssues += @{
                    File = $file.Name
                    Type = "Method Complexity"
                    Count = $complexMethods
                    Severity = "Medium"
                }
            }
        }
        
        $metrics.LinesOfCode = $totalLines
        $metrics.DocumentationCoverage = if ($totalPublicMembers -gt 0) { 
            [math]::Round(($documentedMembers / $totalPublicMembers) * 100, 2) 
        } else { 100 }
        
        # Calculate overall quality score
        $baseScore = 100
        $deductions = 0
        
        foreach ($issue in $qualityIssues) {
            switch ($issue.Severity) {
                "Critical" { $deductions += $issue.Count * 5 }
                "High" { $deductions += $issue.Count * 3 }
                "Medium" { $deductions += $issue.Count * 2 }
                "Minor" { $deductions += $issue.Count * 1 }
            }
        }
        
        $metrics.CodeQualityScore = [math]::Max(0, $baseScore - $deductions)
        $metrics.Violations = $qualityIssues
        
        Write-QualityLog "Code quality analysis completed" "Success"
        return $metrics
        
    } catch {
        Write-QualityLog "Error analyzing code quality: $($_.Exception.Message)" "Error"
        return $metrics
    }
}

function Get-SecurityMetrics {
    Write-QualityLog "Analyzing security metrics..." "Info"
    
    $securityMetrics = @{
        VulnerabilitiesFound = 0
        SecurityScore = 100
        Issues = @()
    }
    
    try {
        # Check for common security issues
        $csharpFiles = Get-ChildItem -Path $SourcePath -Filter "*.cs" -Recurse
        $securityIssues = @()
        
        foreach ($file in $csharpFiles) {
            $content = Get-Content $file.FullName -Raw
            
            # Check for hardcoded secrets
            if ($content -match "password\s*=\s*[""'][^""']+[""']|key\s*=\s*[""'][^""']+[""']") {
                $securityIssues += @{
                    File = $file.Name
                    Type = "Hardcoded Secret"
                    Severity = "Critical"
                }
            }
            
            # Check for SQL injection vulnerabilities
            if ($content -match "ExecuteQuery\s*\(\s*[""'][^""']*\+|CommandText\s*=\s*[^;]*\+") {
                $securityIssues += @{
                    File = $file.Name
                    Type = "SQL Injection Risk"
                    Severity = "High"
                }
            }
            
            # Check for weak cryptography
            if ($content -match "MD5|SHA1(?!256)|DES(?!3)") {
                $securityIssues += @{
                    File = $file.Name
                    Type = "Weak Cryptography"
                    Severity = "Medium"
                }
            }
        }
        
        $securityMetrics.VulnerabilitiesFound = $securityIssues.Count
        $securityMetrics.Issues = $securityIssues
        
        # Calculate security score
        $criticalIssues = ($securityIssues | Where-Object { $_.Severity -eq "Critical" }).Count
        $highIssues = ($securityIssues | Where-Object { $_.Severity -eq "High" }).Count
        $mediumIssues = ($securityIssues | Where-Object { $_.Severity -eq "Medium" }).Count
        
        $securityScore = 100 - ($criticalIssues * 25) - ($highIssues * 15) - ($mediumIssues * 5)
        $securityMetrics.SecurityScore = [math]::Max(0, $securityScore)
        
        Write-QualityLog "Security analysis completed" "Success"
        return $securityMetrics
        
    } catch {
        Write-QualityLog "Error analyzing security: $($_.Exception.Message)" "Error"
        return $securityMetrics
    }
}

function Generate-QualityReport {
    param($Metrics, $SecurityMetrics)
    
    Write-QualityLog "Generating quality report..." "Info"
    
    $reportData = @{
        Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
        Summary = @{
            FilesAnalyzed = $Metrics.FilesAnalyzed
            LinesOfCode = $Metrics.LinesOfCode
            CodeQualityScore = $Metrics.CodeQualityScore
            DocumentationCoverage = $Metrics.DocumentationCoverage
            SecurityScore = $SecurityMetrics.SecurityScore
            VulnerabilitiesFound = $SecurityMetrics.VulnerabilitiesFound
        }
        QualityTargets = $QualityTargets
        Violations = $Metrics.Violations
        SecurityIssues = $SecurityMetrics.Issues
        Recommendations = @()
    }
    
    # Generate recommendations
    if ($Metrics.CodeQualityScore -lt $QualityTargets.CodeQualityScore) {
        $reportData.Recommendations += "Code quality score ($($Metrics.CodeQualityScore)) is below target ($($QualityTargets.CodeQualityScore)). Address quality violations."
    }
    
    if ($Metrics.DocumentationCoverage -lt $QualityTargets.DocumentationCoverage) {
        $reportData.Recommendations += "Documentation coverage ($($Metrics.DocumentationCoverage)%) is below target ($($QualityTargets.DocumentationCoverage)%). Add XML documentation to public members."
    }
    
    if ($SecurityMetrics.VulnerabilitiesFound -gt $QualityTargets.SecurityVulnerabilities) {
        $reportData.Recommendations += "Security vulnerabilities found ($($SecurityMetrics.VulnerabilitiesFound)). Address security issues immediately."
    }
    
    # Save report
    $reportFile = Join-Path $ReportPath "quality-report-$(Get-Date -Format 'yyyyMMdd-HHmmss').json"
    $reportData | ConvertTo-Json -Depth 10 | Out-File -FilePath $reportFile -Encoding UTF8
    
    Write-QualityLog "Quality report saved to: $reportFile" "Success"
    return $reportData
}

function Send-QualityAlerts {
    param($ReportData)
    
    if (-not $SendAlerts) { return }
    
    Write-QualityLog "Checking for quality alerts..." "Info"
    
    $alerts = @()
    $threshold = $AlertThresholds[$AlertThreshold]
    
    # Check code quality score
    if ($ReportData.Summary.CodeQualityScore -lt $threshold.CodeQualityScore) {
        $alerts += @{
            Type = "Code Quality"
            Severity = $AlertThreshold
            Message = "Code quality score ($($ReportData.Summary.CodeQualityScore)) below threshold ($($threshold.CodeQualityScore))"
        }
    }
    
    # Check security vulnerabilities
    if ($ReportData.Summary.VulnerabilitiesFound -gt $threshold.SecurityVulnerabilities) {
        $alerts += @{
            Type = "Security"
            Severity = $AlertThreshold
            Message = "Security vulnerabilities found: $($ReportData.Summary.VulnerabilitiesFound)"
        }
    }
    
    # Check documentation coverage
    if ($threshold.DocumentationCoverage -and $ReportData.Summary.DocumentationCoverage -lt $threshold.DocumentationCoverage) {
        $alerts += @{
            Type = "Documentation"
            Severity = $AlertThreshold
            Message = "Documentation coverage ($($ReportData.Summary.DocumentationCoverage)%) below threshold ($($threshold.DocumentationCoverage)%)"
        }
    }
    
    if ($alerts.Count -gt 0) {
        Write-QualityLog "Quality alerts triggered:" "Warning"
        foreach ($alert in $alerts) {
            Write-QualityLog "  [$($alert.Severity)] $($alert.Type): $($alert.Message)" "Warning"
        }
        
        # Save alerts
        $alertFile = Join-Path $ReportPath "quality-alerts-$(Get-Date -Format 'yyyyMMdd-HHmmss').json"
        $alerts | ConvertTo-Json -Depth 5 | Out-File -FilePath $alertFile -Encoding UTF8
    } else {
        Write-QualityLog "No quality alerts triggered" "Success"
    }
}

# Main execution
try {
    Write-QualityLog "Starting LocalTalk quality monitoring..." "Info"
    
    # Ensure report directory exists
    if (-not (Test-Path $ReportPath)) {
        New-Item -ItemType Directory -Path $ReportPath -Force | Out-Null
    }
    
    # Collect metrics
    $codeMetrics = Get-CodeQualityMetrics
    $securityMetrics = Get-SecurityMetrics
    
    # Generate report
    $report = Generate-QualityReport -Metrics $codeMetrics -SecurityMetrics $securityMetrics
    
    # Send alerts if needed
    Send-QualityAlerts -ReportData $report
    
    # Display summary
    Write-QualityLog "Quality Monitoring Summary:" "Info"
    Write-QualityLog "  Files Analyzed: $($report.Summary.FilesAnalyzed)" "Info"
    Write-QualityLog "  Code Quality Score: $($report.Summary.CodeQualityScore)/100" "Info"
    Write-QualityLog "  Documentation Coverage: $($report.Summary.DocumentationCoverage)%" "Info"
    Write-QualityLog "  Security Score: $($report.Summary.SecurityScore)/100" "Info"
    Write-QualityLog "  Vulnerabilities: $($report.Summary.VulnerabilitiesFound)" "Info"
    
    Write-QualityLog "Quality monitoring completed successfully" "Success"
    
} catch {
    Write-QualityLog "Quality monitoring failed: $($_.Exception.Message)" "Error"
    exit 1
}
