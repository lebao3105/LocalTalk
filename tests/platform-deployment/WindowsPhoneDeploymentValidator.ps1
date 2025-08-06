# Windows Phone 8.x Real Device Deployment Validation Script
# This script validates LocalTalk deployment and basic functionality on Windows Phone 8.x devices

param(
    [string]$Configuration = "Release",
    [string]$Platform = "ARM",
    [string]$DeviceId = "",
    [switch]$UseEmulator = $false,
    [switch]$SkipBuild = $false,
    [switch]$Verbose = $false
)

$ErrorActionPreference = "Stop"
$VerbosePreference = if ($Verbose) { "Continue" } else { "SilentlyContinue" }

Write-Host "LocalTalk Windows Phone 8.x Deployment Validation" -ForegroundColor Green
Write-Host "=================================================" -ForegroundColor Green

# Configuration
$ProjectPath = "LocalTalk\LocalTalk.csproj"
$SolutionPath = "LocalTalk.sln"
$TestResultsPath = "tests\results\wp8-deployment-$(Get-Date -Format 'yyyyMMdd-HHmmss').json"
$ValidationResults = @{
    Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    Configuration = $Configuration
    Platform = $Platform
    DeviceId = $DeviceId
    UseEmulator = $UseEmulator
    Tests = @{}
    Summary = @{
        Total = 0
        Passed = 0
        Failed = 0
        Skipped = 0
    }
}

# Ensure results directory exists
$ResultsDir = Split-Path $TestResultsPath -Parent
if (!(Test-Path $ResultsDir)) {
    New-Item -ItemType Directory -Path $ResultsDir -Force | Out-Null
}

function Write-TestResult {
    param(
        [string]$TestName,
        [string]$Status,
        [string]$Message = "",
        [object]$Details = $null
    )
    
    $ValidationResults.Tests[$TestName] = @{
        Status = $Status
        Message = $Message
        Details = $Details
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
            Write-Host "❌ $TestName - FAIL: $Message" -ForegroundColor Red
        }
        "SKIP" { 
            $ValidationResults.Summary.Skipped++
            Write-Host "⏭️ $TestName - SKIP: $Message" -ForegroundColor Yellow
        }
    }
    
    if ($Details) {
        Write-Verbose "Details: $($Details | ConvertTo-Json -Depth 3)"
    }
}

function Test-Prerequisites {
    Write-Host "`nValidating Prerequisites..." -ForegroundColor Cyan
    
    # Test Visual Studio installation
    try {
        $vsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
        if (Test-Path $vsWhere) {
            $vsInstances = & $vsWhere -products * -requires Microsoft.VisualStudio.Workload.ManagedDesktop -format json | ConvertFrom-Json
            if ($vsInstances.Count -gt 0) {
                Write-TestResult "Visual Studio Installation" "PASS" "Found Visual Studio: $($vsInstances[0].displayName)"
            } else {
                Write-TestResult "Visual Studio Installation" "FAIL" "No suitable Visual Studio installation found"
                return $false
            }
        } else {
            Write-TestResult "Visual Studio Installation" "FAIL" "vswhere.exe not found"
            return $false
        }
    } catch {
        Write-TestResult "Visual Studio Installation" "FAIL" "Error checking Visual Studio: $($_.Exception.Message)"
        return $false
    }
    
    # Test Windows Phone SDK
    $wpSdkPath = "${env:ProgramFiles(x86)}\Microsoft SDKs\Windows Phone\v8.0"
    if (Test-Path $wpSdkPath) {
        Write-TestResult "Windows Phone SDK" "PASS" "Windows Phone 8.0 SDK found"
    } else {
        Write-TestResult "Windows Phone SDK" "FAIL" "Windows Phone 8.0 SDK not found at $wpSdkPath"
        return $false
    }
    
    # Test MSBuild availability
    try {
        $msbuildPath = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -requires Microsoft.Component.MSBuild -find MSBuild\*\Bin\MSBuild.exe | Select-Object -First 1
        if ($msbuildPath -and (Test-Path $msbuildPath)) {
            Write-TestResult "MSBuild Availability" "PASS" "MSBuild found at $msbuildPath"
            $script:MSBuildPath = $msbuildPath
        } else {
            Write-TestResult "MSBuild Availability" "FAIL" "MSBuild not found"
            return $false
        }
    } catch {
        Write-TestResult "MSBuild Availability" "FAIL" "Error locating MSBuild: $($_.Exception.Message)"
        return $false
    }
    
    # Test project files existence
    if (Test-Path $ProjectPath) {
        Write-TestResult "Project Files" "PASS" "LocalTalk project file found"
    } else {
        Write-TestResult "Project Files" "FAIL" "LocalTalk project file not found at $ProjectPath"
        return $false
    }
    
    return $true
}

function Test-ProjectBuild {
    if ($SkipBuild) {
        Write-TestResult "Project Build" "SKIP" "Build skipped by user request"
        return $true
    }
    
    Write-Host "`nBuilding Windows Phone Project..." -ForegroundColor Cyan
    
    try {
        # Clean first
        Write-Verbose "Cleaning project..."
        $cleanResult = & $script:MSBuildPath $ProjectPath /t:Clean /p:Configuration=$Configuration /p:Platform=$Platform /verbosity:minimal 2>&1
        
        # Build project
        Write-Verbose "Building project..."
        $buildResult = & $script:MSBuildPath $ProjectPath /t:Build /p:Configuration=$Configuration /p:Platform=$Platform /verbosity:minimal 2>&1
        
        if ($LASTEXITCODE -eq 0) {
            Write-TestResult "Project Build" "PASS" "Build completed successfully"
            
            # Check for output files
            $outputPath = "LocalTalk\Bin\$Platform\$Configuration"
            $xapFile = "$outputPath\LocalTalk_$($Configuration)_$Platform.xap"
            
            if (Test-Path $xapFile) {
                Write-TestResult "XAP Package Generation" "PASS" "XAP package created: $xapFile"
                $script:XapPath = $xapFile
                return $true
            } else {
                Write-TestResult "XAP Package Generation" "FAIL" "XAP package not found at $xapFile"
                return $false
            }
        } else {
            Write-TestResult "Project Build" "FAIL" "Build failed with exit code $LASTEXITCODE"
            Write-Verbose "Build output: $buildResult"
            return $false
        }
    } catch {
        Write-TestResult "Project Build" "FAIL" "Build error: $($_.Exception.Message)"
        return $false
    }
}

function Test-DeviceConnection {
    Write-Host "`nTesting Device Connection..." -ForegroundColor Cyan
    
    if ($UseEmulator) {
        Write-TestResult "Device Connection" "SKIP" "Using emulator instead of real device"
        return $true
    }
    
    try {
        # Check for Windows Phone Application Deployment tool
        $deployToolPath = "${env:ProgramFiles(x86)}\Microsoft SDKs\Windows Phone\v8.0\Tools\AppDeploy\AppDeployCmd.exe"
        
        if (!(Test-Path $deployToolPath)) {
            Write-TestResult "Device Connection" "FAIL" "AppDeployCmd.exe not found"
            return $false
        }
        
        # List connected devices
        $deviceListResult = & $deployToolPath /EnumerateDevices 2>&1
        
        if ($LASTEXITCODE -eq 0) {
            $devices = $deviceListResult | Where-Object { $_ -match "Device:" }
            if ($devices.Count -gt 0) {
                Write-TestResult "Device Connection" "PASS" "Found $($devices.Count) connected device(s)"
                $ValidationResults.Tests["Device Connection"].Details = @{ Devices = $devices }
                return $true
            } else {
                Write-TestResult "Device Connection" "FAIL" "No Windows Phone devices connected"
                return $false
            }
        } else {
            Write-TestResult "Device Connection" "FAIL" "Failed to enumerate devices"
            return $false
        }
    } catch {
        Write-TestResult "Device Connection" "FAIL" "Error checking device connection: $($_.Exception.Message)"
        return $false
    }
}

function Test-ApplicationDeployment {
    Write-Host "`nTesting Application Deployment..." -ForegroundColor Cyan
    
    if (!$script:XapPath -or !(Test-Path $script:XapPath)) {
        Write-TestResult "Application Deployment" "FAIL" "XAP package not available for deployment"
        return $false
    }
    
    try {
        $deployToolPath = "${env:ProgramFiles(x86)}\Microsoft SDKs\Windows Phone\v8.0\Tools\AppDeploy\AppDeployCmd.exe"
        
        if ($UseEmulator) {
            # Deploy to emulator
            Write-Verbose "Deploying to emulator..."
            $deployResult = & $deployToolPath /installlaunch $script:XapPath /targetdevice:xd 2>&1
        } else {
            # Deploy to device
            if ($DeviceId) {
                Write-Verbose "Deploying to device $DeviceId..."
                $deployResult = & $deployToolPath /installlaunch $script:XapPath /targetdevice:$DeviceId 2>&1
            } else {
                Write-Verbose "Deploying to first available device..."
                $deployResult = & $deployToolPath /installlaunch $script:XapPath /targetdevice:de 2>&1
            }
        }
        
        if ($LASTEXITCODE -eq 0) {
            Write-TestResult "Application Deployment" "PASS" "Application deployed and launched successfully"
            return $true
        } else {
            Write-TestResult "Application Deployment" "FAIL" "Deployment failed with exit code $LASTEXITCODE"
            Write-Verbose "Deployment output: $deployResult"
            return $false
        }
    } catch {
        Write-TestResult "Application Deployment" "FAIL" "Deployment error: $($_.Exception.Message)"
        return $false
    }
}

function Test-BasicFunctionality {
    Write-Host "`nTesting Basic Functionality..." -ForegroundColor Cyan
    
    # Note: This would require actual device interaction or automated UI testing
    # For now, we'll create a framework for manual validation
    
    $functionalityTests = @(
        "Application Launch",
        "Main Page Load",
        "Send Page Navigation",
        "Receive Page Navigation",
        "File Picker Access",
        "Network Discovery",
        "HTTP Server Start"
    )
    
    Write-Host "Manual functionality validation required:" -ForegroundColor Yellow
    Write-Host "Please verify the following on the deployed device:" -ForegroundColor Yellow
    
    foreach ($test in $functionalityTests) {
        Write-Host "  - $test" -ForegroundColor Yellow
    }
    
    Write-TestResult "Basic Functionality" "SKIP" "Manual validation required - see console output for checklist"
    return $true
}

# Main execution
try {
    Write-Host "Starting Windows Phone 8.x deployment validation..." -ForegroundColor White
    
    $success = $true
    
    # Run validation tests
    $success = $success -and (Test-Prerequisites)
    $success = $success -and (Test-ProjectBuild)
    $success = $success -and (Test-DeviceConnection)
    $success = $success -and (Test-ApplicationDeployment)
    $success = $success -and (Test-BasicFunctionality)
    
    # Save results
    $ValidationResults | ConvertTo-Json -Depth 4 | Out-File -FilePath $TestResultsPath -Encoding UTF8
    
    # Summary
    Write-Host "`nValidation Summary:" -ForegroundColor White
    Write-Host "==================" -ForegroundColor White
    Write-Host "Total Tests: $($ValidationResults.Summary.Total)" -ForegroundColor White
    Write-Host "Passed: $($ValidationResults.Summary.Passed)" -ForegroundColor Green
    Write-Host "Failed: $($ValidationResults.Summary.Failed)" -ForegroundColor Red
    Write-Host "Skipped: $($ValidationResults.Summary.Skipped)" -ForegroundColor Yellow
    Write-Host "Results saved to: $TestResultsPath" -ForegroundColor Gray
    
    if ($success -and $ValidationResults.Summary.Failed -eq 0) {
        Write-Host "`n✅ Windows Phone 8.x deployment validation PASSED" -ForegroundColor Green
        exit 0
    } else {
        Write-Host "`n❌ Windows Phone 8.x deployment validation FAILED" -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host "`n💥 Validation script error: $($_.Exception.Message)" -ForegroundColor Red
    Write-TestResult "Script Execution" "FAIL" $_.Exception.Message
    $ValidationResults | ConvertTo-Json -Depth 4 | Out-File -FilePath $TestResultsPath -Encoding UTF8
    exit 1
}
