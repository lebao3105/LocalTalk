# Master Assumption Validation Script
# This script orchestrates all 10 critical assumption validation tests for LocalTalk

param(
    [string[]]$TestCategories = @("All"),
    [string]$Configuration = "Release",
    [string]$SenderIP = "",
    [string]$ReceiverIP = "",
    [switch]$GenerateTestData = $false,
    [switch]$Verbose = $false,
    [switch]$ContinueOnFailure = $false,
    [string]$ReportPath = "tests\results\master-validation-report.html"
)

$ErrorActionPreference = if ($ContinueOnFailure) { "Continue" } else { "Stop" }
$VerbosePreference = if ($Verbose) { "Continue" } else { "SilentlyContinue" }

Write-Host "LocalTalk Master Assumption Validation" -ForegroundColor Green
Write-Host "=====================================" -ForegroundColor Green

# Configuration
$ValidationResults = @{
    Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    Configuration = $Configuration
    TestCategories = $TestCategories
    AssumptionResults = @{}
    OverallSummary = @{
        TotalAssumptions = 12
        ValidatedAssumptions = 0
        FailedAssumptions = 0
        SkippedAssumptions = 0
        OverallSuccess = $false
    }
}

# Define the 10 critical assumptions and their validation scripts
$AssumptionValidators = @{
    "CrossPlatformCompatibility" = @{
        Name = "Cross-Platform Compatibility"
        Description = "Application works seamlessly across Windows Phone 8.x and UWP platforms"
        Scripts = @(
            @{ Path = "tests\platform-deployment\WindowsPhoneDeploymentValidator.ps1"; Args = @("-Configuration", $Configuration) },
            @{ Path = "tests\platform-deployment\UWPDeploymentValidator.ps1"; Args = @("-Configuration", $Configuration) },
            @{ Path = "tests\platform-deployment\SharedCodeConsistencyValidator.ps1"; Args = @("-Configuration", $Configuration) }
        )
        Critical = $true
    }
    
    "RealDeviceFileTransfer" = @{
        Name = "Real Device File Transfer Reliability"
        Description = "File transfers work reliably between real devices in various conditions"
        Scripts = @(
            @{ Path = "tests\file-transfer\SameNetworkTransferValidator.ps1"; Args = @("-SenderIP", $SenderIP, "-ReceiverIP", $ReceiverIP, "-GenerateTestFiles") }
        )
        Critical = $true
        RequiresDevices = $true
    }
    
    "SecurityValidation" = @{
        Name = "Security Validation Effectiveness"
        Description = "Security measures effectively block real threats without false positives"
        Scripts = @(
            @{ Path = "tests\security\SecurityValidationTester.ps1"; Args = @() }
        )
        Critical = $true
    }
    
    "PerformanceStandards" = @{
        Name = "Performance Standards"
        Description = "Performance meets acceptable standards for real-world usage"
        Scripts = @(
            @{ Path = "tests\performance\PerformanceBenchmarkValidator.ps1"; Args = @("-GenerateTestFiles") }
        )
        Critical = $true
    }
    
    "NetworkDiscoveryReliability" = @{
        Name = "Network Discovery Reliability"
        Description = "Device discovery works reliably across different network configurations"
        Scripts = @(
            @{ Path = "tests\network\NetworkDiscoveryValidator.ps1"; Args = @() }
        )
        Critical = $false
    }
    
    "LocalSendProtocolCompatibility" = @{
        Name = "LocalSend Protocol Compatibility"
        Description = "Complete compatibility with official LocalSend protocol and ecosystem"
        Scripts = @(
            @{ Path = "tests\protocol\LocalSendCompatibilityValidator.ps1"; Args = @() }
        )
        Critical = $true
    }
    
    "ErrorHandlingRecovery" = @{
        Name = "Error Handling and Recovery"
        Description = "Error handling gracefully recovers from various failure scenarios"
        Scripts = @(
            @{ Path = "tests\reliability\ErrorHandlingValidator.ps1"; Args = @() }
        )
        Critical = $false
    }
    
    "MemoryManagement" = @{
        Name = "Memory Management"
        Description = "Memory usage is reasonable and doesn't cause system instability"
        Scripts = @(
            @{ Path = "tests\performance\MemoryManagementValidator.ps1"; Args = @() }
        )
        Critical = $false
    }
    
    "UIResponsiveness" = @{
        Name = "User Interface Responsiveness"
        Description = "UI remains responsive and provides good user experience"
        Scripts = @(
            @{ Path = "tests\ui\UIResponsivenessValidator.ps1"; Args = @() }
        )
        Critical = $false
    }
    
    "BuildEnvironmentCompatibility" = @{
        Name = "Build and Deployment Environment"
        Description = "Build process works reliably across different development environments"
        Scripts = @(
            @{ Path = "tests\build\BuildEnvironmentValidator.ps1"; Args = @("-Configuration", $Configuration) }
        )
        Critical = $false
    }

    "CodeStandardsCompliance" = @{
        Name = "Code Standards Compliance"
        Description = "Code adheres to LLVM-inspired coding standards for quality and maintainability"
        Scripts = @(
            @{ Path = "tests\code-standards\CodeStandardsValidator.ps1"; Args = @("-Verbose") }
        )
        Critical = $true
    }

    "DeveloperPolicyCompliance" = @{
        Name = "Developer Policy Compliance"
        Description = "Development practices follow LLVM-inspired policies for collaboration and quality"
        Scripts = @(
            @{ Path = "tests\code-standards\DeveloperPolicyValidator.ps1"; Args = @("-Verbose") }
        )
        Critical = $false
    }
}

function Write-AssumptionResult {
    param(
        [string]$AssumptionKey,
        [string]$Status,
        [string]$Message = "",
        [object]$Details = $null
    )
    
    $assumption = $AssumptionValidators[$AssumptionKey]
    $ValidationResults.AssumptionResults[$AssumptionKey] = @{
        Name = $assumption.Name
        Description = $assumption.Description
        Status = $Status
        Message = $Message
        Details = $Details
        Critical = $assumption.Critical
        Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    }
    
    switch ($Status) {
        "PASS" { 
            $ValidationResults.OverallSummary.ValidatedAssumptions++
            Write-Host "✅ $($assumption.Name) - VALIDATED" -ForegroundColor Green
        }
        "FAIL" { 
            $ValidationResults.OverallSummary.FailedAssumptions++
            Write-Host "❌ $($assumption.Name) - FAILED: $Message" -ForegroundColor Red
        }
        "SKIP" { 
            $ValidationResults.OverallSummary.SkippedAssumptions++
            Write-Host "⏭️ $($assumption.Name) - SKIPPED: $Message" -ForegroundColor Yellow
        }
    }
    
    if ($Details) {
        Write-Verbose "Details: $($Details | ConvertTo-Json -Depth 2)"
    }
}

function Test-Assumption {
    param(
        [string]$AssumptionKey
    )
    
    $assumption = $AssumptionValidators[$AssumptionKey]
    Write-Host "`n--- Testing Assumption: $($assumption.Name) ---" -ForegroundColor Magenta
    Write-Host "Description: $($assumption.Description)" -ForegroundColor Gray
    
    # Check if assumption requires devices and they're not available
    if ($assumption.RequiresDevices -and (!$SenderIP -or !$ReceiverIP)) {
        Write-AssumptionResult -AssumptionKey $AssumptionKey -Status "SKIP" -Message "Requires device IPs (SenderIP and ReceiverIP parameters)"
        return $false
    }
    
    $scriptResults = @()
    $overallSuccess = $true
    
    foreach ($script in $assumption.Scripts) {
        $scriptPath = $script.Path
        $scriptArgs = $script.Args
        
        if (!(Test-Path $scriptPath)) {
            Write-Host "  ⚠️ Script not found: $scriptPath" -ForegroundColor Yellow
            $scriptResults += @{
                Script = $scriptPath
                Status = "SKIP"
                Message = "Script file not found"
            }
            continue
        }
        
        try {
            Write-Host "  🔄 Running: $scriptPath" -ForegroundColor Cyan
            Write-Verbose "Script arguments: $($scriptArgs -join ' ')"
            
            $startTime = Get-Date
            
            # Execute the validation script
            if ($Verbose) {
                $scriptArgs += "-Verbose"
            }
            
            $result = & PowerShell.exe -File $scriptPath @scriptArgs
            $exitCode = $LASTEXITCODE
            
            $endTime = Get-Date
            $duration = ($endTime - $startTime).TotalSeconds
            
            $scriptResult = @{
                Script = $scriptPath
                ExitCode = $exitCode
                Duration = $duration
                Status = if ($exitCode -eq 0) { "PASS" } else { "FAIL" }
                Message = if ($exitCode -eq 0) { "Completed successfully" } else { "Failed with exit code $exitCode" }
            }
            
            $scriptResults += $scriptResult
            
            if ($exitCode -ne 0) {
                $overallSuccess = $false
                Write-Host "    ❌ Failed with exit code: $exitCode" -ForegroundColor Red
            } else {
                Write-Host "    ✅ Completed successfully in $([math]::Round($duration, 2))s" -ForegroundColor Green
            }
        } catch {
            $scriptResult = @{
                Script = $scriptPath
                Status = "FAIL"
                Message = "Script execution error: $($_.Exception.Message)"
                Error = $_.Exception.Message
            }
            
            $scriptResults += $scriptResult
            $overallSuccess = $false
            Write-Host "    💥 Script error: $($_.Exception.Message)" -ForegroundColor Red
        }
    }
    
    # Determine overall assumption result
    if ($overallSuccess) {
        Write-AssumptionResult -AssumptionKey $AssumptionKey -Status "PASS" -Message "All validation scripts passed" -Details $scriptResults
    } else {
        $failedScripts = $scriptResults | Where-Object { $_.Status -eq "FAIL" }
        Write-AssumptionResult -AssumptionKey $AssumptionKey -Status "FAIL" -Message "$($failedScripts.Count) of $($scriptResults.Count) scripts failed" -Details $scriptResults
    }
    
    return $overallSuccess
}

function Get-TestCategoriesToRun {
    if ($TestCategories -contains "All") {
        return $AssumptionValidators.Keys
    } elseif ($TestCategories -contains "Critical") {
        return $AssumptionValidators.Keys | Where-Object { $AssumptionValidators[$_].Critical }
    } else {
        return $TestCategories | Where-Object { $AssumptionValidators.ContainsKey($_) }
    }
}

function New-ValidationReport {
    Write-Host "`nGenerating validation report..." -ForegroundColor Cyan
    
    $reportDir = Split-Path $ReportPath -Parent
    if (!(Test-Path $reportDir)) {
        New-Item -ItemType Directory -Path $reportDir -Force | Out-Null
    }
    
    $html = @"
<!DOCTYPE html>
<html>
<head>
    <title>LocalTalk Assumption Validation Report</title>
    <style>
        body { font-family: Arial, sans-serif; margin: 20px; }
        .header { background-color: #2c3e50; color: white; padding: 20px; border-radius: 5px; }
        .summary { background-color: #ecf0f1; padding: 15px; margin: 20px 0; border-radius: 5px; }
        .assumption { margin: 20px 0; padding: 15px; border: 1px solid #bdc3c7; border-radius: 5px; }
        .pass { border-left: 5px solid #27ae60; }
        .fail { border-left: 5px solid #e74c3c; }
        .skip { border-left: 5px solid #f39c12; }
        .critical { background-color: #fdf2e9; }
        .status { font-weight: bold; padding: 5px 10px; border-radius: 3px; color: white; }
        .status.pass { background-color: #27ae60; }
        .status.fail { background-color: #e74c3c; }
        .status.skip { background-color: #f39c12; }
        .details { margin-top: 10px; font-size: 0.9em; color: #7f8c8d; }
        .timestamp { font-size: 0.8em; color: #95a5a6; }
    </style>
</head>
<body>
    <div class="header">
        <h1>LocalTalk Assumption Validation Report</h1>
        <p>Generated: $($ValidationResults.Timestamp)</p>
        <p>Configuration: $($ValidationResults.Configuration)</p>
    </div>
    
    <div class="summary">
        <h2>Validation Summary</h2>
        <p><strong>Total Assumptions:</strong> $($ValidationResults.OverallSummary.TotalAssumptions)</p>
        <p><strong>Validated:</strong> $($ValidationResults.OverallSummary.ValidatedAssumptions)</p>
        <p><strong>Failed:</strong> $($ValidationResults.OverallSummary.FailedAssumptions)</p>
        <p><strong>Skipped:</strong> $($ValidationResults.OverallSummary.SkippedAssumptions)</p>
        <p><strong>Overall Result:</strong> <span class="status $(if ($ValidationResults.OverallSummary.OverallSuccess) { 'pass' } else { 'fail' })">$(if ($ValidationResults.OverallSummary.OverallSuccess) { 'PASS' } else { 'FAIL' })</span></p>
    </div>
    
    <h2>Assumption Results</h2>
"@
    
    foreach ($key in $ValidationResults.AssumptionResults.Keys) {
        $result = $ValidationResults.AssumptionResults[$key]
        $statusClass = $result.Status.ToLower()
        $criticalClass = if ($result.Critical) { "critical" } else { "" }
        
        $html += @"
    <div class="assumption $statusClass $criticalClass">
        <h3>$($result.Name) $(if ($result.Critical) { '(Critical)' } else { '' })</h3>
        <p><strong>Description:</strong> $($result.Description)</p>
        <p><strong>Status:</strong> <span class="status $statusClass">$($result.Status)</span></p>
        $(if ($result.Message) { "<p><strong>Message:</strong> $($result.Message)</p>" } else { "" })
        <div class="timestamp">Tested: $($result.Timestamp)</div>
    </div>
"@
    }
    
    $html += @"
</body>
</html>
"@
    
    $html | Out-File -FilePath $ReportPath -Encoding UTF8
    Write-Host "Report saved to: $ReportPath" -ForegroundColor Green
}

# Main execution
try {
    Write-Host "Starting master assumption validation..." -ForegroundColor White
    Write-Host "Test Categories: $($TestCategories -join ', ')" -ForegroundColor Gray
    
    $categoriesToTest = Get-TestCategoriesToRun
    Write-Host "Assumptions to validate: $($categoriesToTest.Count)" -ForegroundColor White
    
    $overallSuccess = $true
    $criticalFailures = 0
    
    foreach ($category in $categoriesToTest) {
        $success = Test-Assumption -AssumptionKey $category
        
        if (!$success) {
            $overallSuccess = $false
            if ($AssumptionValidators[$category].Critical) {
                $criticalFailures++
            }
        }
        
        # Stop on critical failure if not continuing on failure
        if (!$success -and $AssumptionValidators[$category].Critical -and !$ContinueOnFailure) {
            Write-Host "`n🛑 Critical assumption failed. Stopping validation." -ForegroundColor Red
            break
        }
    }
    
    # Final assessment
    $ValidationResults.OverallSummary.OverallSuccess = $overallSuccess -and ($criticalFailures -eq 0)
    
    # Generate report
    New-ValidationReport
    
    # Final summary
    Write-Host "`n" + "="*50 -ForegroundColor White
    Write-Host "MASTER VALIDATION SUMMARY" -ForegroundColor White
    Write-Host "="*50 -ForegroundColor White
    Write-Host "Total Assumptions: $($ValidationResults.OverallSummary.TotalAssumptions)" -ForegroundColor White
    Write-Host "Validated: $($ValidationResults.OverallSummary.ValidatedAssumptions)" -ForegroundColor Green
    Write-Host "Failed: $($ValidationResults.OverallSummary.FailedAssumptions)" -ForegroundColor Red
    Write-Host "Skipped: $($ValidationResults.OverallSummary.SkippedAssumptions)" -ForegroundColor Yellow
    Write-Host "Critical Failures: $criticalFailures" -ForegroundColor $(if ($criticalFailures -gt 0) { "Red" } else { "Green" })
    
    if ($ValidationResults.OverallSummary.OverallSuccess) {
        Write-Host "`n🎉 ALL CRITICAL ASSUMPTIONS VALIDATED - LocalTalk is ready for production!" -ForegroundColor Green
        exit 0
    } else {
        Write-Host "`n⚠️ VALIDATION FAILED - Critical assumptions not met. Review report for details." -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host "`n💥 Master validation error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
