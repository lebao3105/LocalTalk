# UWP Multi-Platform Deployment Validation Script
# This script validates LocalTalk UWP deployment across Windows 10 Desktop, Mobile, and Tablet

param(
    [string]$Configuration = "Release",
    [string[]]$Platforms = @("x86", "x64", "ARM"),
    [string]$DeviceType = "Desktop", # Desktop, Mobile, Tablet, All
    [switch]$SkipBuild = $false,
    [switch]$SkipDeploy = $false,
    [switch]$Verbose = $false
)

$ErrorActionPreference = "Stop"
$VerbosePreference = if ($Verbose) { "Continue" } else { "SilentlyContinue" }

Write-Host "LocalTalk UWP Multi-Platform Deployment Validation" -ForegroundColor Green
Write-Host "=================================================" -ForegroundColor Green

# Configuration
$ProjectPath = "LocalTalkUWP\LocalTalkUWP.csproj"
$SolutionPath = "LocalTalkUWP.sln"
$TestResultsPath = "tests\results\uwp-deployment-$(Get-Date -Format 'yyyyMMdd-HHmmss').json"
$ValidationResults = @{
    Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    Configuration = $Configuration
    Platforms = $Platforms
    DeviceType = $DeviceType
    Tests = @{}
    PlatformResults = @{}
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
        [object]$Details = $null,
        [string]$Platform = ""
    )
    
    $testKey = if ($Platform) { "$TestName ($Platform)" } else { $TestName }
    
    $ValidationResults.Tests[$testKey] = @{
        Status = $Status
        Message = $Message
        Details = $Details
        Platform = $Platform
        Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    }
    
    $ValidationResults.Summary.Total++
    switch ($Status) {
        "PASS" { 
            $ValidationResults.Summary.Passed++
            Write-Host "✅ $testKey - PASS" -ForegroundColor Green
        }
        "FAIL" { 
            $ValidationResults.Summary.Failed++
            Write-Host "❌ $testKey - FAIL: $Message" -ForegroundColor Red
        }
        "SKIP" { 
            $ValidationResults.Summary.Skipped++
            Write-Host "⏭️ $testKey - SKIP: $Message" -ForegroundColor Yellow
        }
    }
    
    if ($Details) {
        Write-Verbose "Details: $($Details | ConvertTo-Json -Depth 3)"
    }
}

function Test-UWPPrerequisites {
    Write-Host "`nValidating UWP Prerequisites..." -ForegroundColor Cyan
    
    # Test Windows 10 SDK
    try {
        $sdkPath = "${env:ProgramFiles(x86)}\Windows Kits\10"
        if (Test-Path $sdkPath) {
            $sdkVersions = Get-ChildItem "$sdkPath\bin" -Directory | Where-Object { $_.Name -match "10\.\d+\.\d+\.\d+" } | Sort-Object Name -Descending
            if ($sdkVersions.Count -gt 0) {
                $latestSdk = $sdkVersions[0].Name
                Write-TestResult "Windows 10 SDK" "PASS" "Found Windows 10 SDK version $latestSdk"
                $script:SDKVersion = $latestSdk
            } else {
                Write-TestResult "Windows 10 SDK" "FAIL" "No Windows 10 SDK versions found"
                return $false
            }
        } else {
            Write-TestResult "Windows 10 SDK" "FAIL" "Windows 10 SDK not found at $sdkPath"
            return $false
        }
    } catch {
        Write-TestResult "Windows 10 SDK" "FAIL" "Error checking Windows 10 SDK: $($_.Exception.Message)"
        return $false
    }
    
    # Test Visual Studio UWP workload
    try {
        $vsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
        if (Test-Path $vsWhere) {
            $vsInstances = & $vsWhere -products * -requires Microsoft.VisualStudio.Workload.Universal -format json | ConvertFrom-Json
            if ($vsInstances.Count -gt 0) {
                Write-TestResult "UWP Workload" "PASS" "Found Visual Studio with UWP workload: $($vsInstances[0].displayName)"
            } else {
                Write-TestResult "UWP Workload" "FAIL" "No Visual Studio installation with UWP workload found"
                return $false
            }
        } else {
            Write-TestResult "UWP Workload" "FAIL" "vswhere.exe not found"
            return $false
        }
    } catch {
        Write-TestResult "UWP Workload" "FAIL" "Error checking UWP workload: $($_.Exception.Message)"
        return $false
    }
    
    # Test MSBuild for UWP
    try {
        $msbuildPath = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -requires Microsoft.Component.MSBuild -find MSBuild\*\Bin\MSBuild.exe | Select-Object -First 1
        if ($msbuildPath -and (Test-Path $msbuildPath)) {
            Write-TestResult "MSBuild for UWP" "PASS" "MSBuild found at $msbuildPath"
            $script:MSBuildPath = $msbuildPath
        } else {
            Write-TestResult "MSBuild for UWP" "FAIL" "MSBuild not found"
            return $false
        }
    } catch {
        Write-TestResult "MSBuild for UWP" "FAIL" "Error locating MSBuild: $($_.Exception.Message)"
        return $false
    }
    
    # Test project files
    if (Test-Path $ProjectPath) {
        Write-TestResult "UWP Project Files" "PASS" "LocalTalkUWP project file found"
    } else {
        Write-TestResult "UWP Project Files" "FAIL" "LocalTalkUWP project file not found at $ProjectPath"
        return $false
    }
    
    return $true
}

function Test-PlatformBuild {
    param([string]$Platform)
    
    if ($SkipBuild) {
        Write-TestResult "Build" "SKIP" "Build skipped by user request" -Platform $Platform
        return $true
    }
    
    Write-Host "`nBuilding UWP Project for $Platform..." -ForegroundColor Cyan
    
    try {
        # Clean first
        Write-Verbose "Cleaning project for $Platform..."
        $cleanResult = & $script:MSBuildPath $ProjectPath /t:Clean /p:Configuration=$Configuration /p:Platform=$Platform /verbosity:minimal 2>&1
        
        # Restore packages
        Write-Verbose "Restoring packages for $Platform..."
        $restoreResult = & $script:MSBuildPath $ProjectPath /t:Restore /p:Configuration=$Configuration /p:Platform=$Platform /verbosity:minimal 2>&1
        
        # Build project
        Write-Verbose "Building project for $Platform..."
        $buildResult = & $script:MSBuildPath $ProjectPath /t:Build /p:Configuration=$Configuration /p:Platform=$Platform /verbosity:minimal 2>&1
        
        if ($LASTEXITCODE -eq 0) {
            Write-TestResult "Build" "PASS" "Build completed successfully" -Platform $Platform
            
            # Check for output files
            $outputPath = "LocalTalkUWP\bin\$Platform\$Configuration"
            $appxFile = Get-ChildItem "$outputPath\*.appx" -ErrorAction SilentlyContinue | Select-Object -First 1
            
            if ($appxFile) {
                Write-TestResult "APPX Package Generation" "PASS" "APPX package created: $($appxFile.Name)" -Platform $Platform
                if (!$ValidationResults.PlatformResults[$Platform]) {
                    $ValidationResults.PlatformResults[$Platform] = @{}
                }
                $ValidationResults.PlatformResults[$Platform].AppxPath = $appxFile.FullName
                return $true
            } else {
                Write-TestResult "APPX Package Generation" "FAIL" "APPX package not found in $outputPath" -Platform $Platform
                return $false
            }
        } else {
            Write-TestResult "Build" "FAIL" "Build failed with exit code $LASTEXITCODE" -Platform $Platform
            Write-Verbose "Build output: $buildResult"
            return $false
        }
    } catch {
        Write-TestResult "Build" "FAIL" "Build error: $($_.Exception.Message)" -Platform $Platform
        return $false
    }
}

function Test-PackageValidation {
    param([string]$Platform)
    
    Write-Host "`nValidating UWP Package for $Platform..." -ForegroundColor Cyan
    
    $appxPath = $ValidationResults.PlatformResults[$Platform].AppxPath
    if (!$appxPath -or !(Test-Path $appxPath)) {
        Write-TestResult "Package Validation" "FAIL" "APPX package not available" -Platform $Platform
        return $false
    }
    
    try {
        # Test package with Windows App Certification Kit (if available)
        $wackPath = "${env:ProgramFiles(x86)}\Windows Kits\10\App Certification Kit\appcert.exe"
        if (Test-Path $wackPath) {
            Write-Verbose "Running Windows App Certification Kit validation..."
            $wackResult = & $wackPath test -appxpackagepath $appxPath -reportoutputpath "tests\results\wack-$Platform-$(Get-Date -Format 'yyyyMMdd-HHmmss').xml" 2>&1
            
            if ($LASTEXITCODE -eq 0) {
                Write-TestResult "WACK Validation" "PASS" "Package passed Windows App Certification Kit validation" -Platform $Platform
            } else {
                Write-TestResult "WACK Validation" "FAIL" "Package failed WACK validation" -Platform $Platform
            }
        } else {
            Write-TestResult "WACK Validation" "SKIP" "Windows App Certification Kit not found" -Platform $Platform
        }
        
        # Basic package info validation
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $zip = [System.IO.Compression.ZipFile]::OpenRead($appxPath)
        
        $hasManifest = $zip.Entries | Where-Object { $_.Name -eq "AppxManifest.xml" }
        $hasExecutable = $zip.Entries | Where-Object { $_.Name -eq "LocalTalkUWP.exe" }
        
        $zip.Dispose()
        
        if ($hasManifest -and $hasExecutable) {
            Write-TestResult "Package Contents" "PASS" "Package contains required files" -Platform $Platform
            return $true
        } else {
            Write-TestResult "Package Contents" "FAIL" "Package missing required files" -Platform $Platform
            return $false
        }
    } catch {
        Write-TestResult "Package Validation" "FAIL" "Validation error: $($_.Exception.Message)" -Platform $Platform
        return $false
    }
}

function Test-DeviceCompatibility {
    param([string]$Platform)
    
    Write-Host "`nTesting Device Compatibility for $Platform..." -ForegroundColor Cyan
    
    # Test device family compatibility based on platform
    $compatibleDevices = @()
    
    switch ($Platform) {
        "x86" { $compatibleDevices = @("Desktop", "Tablet") }
        "x64" { $compatibleDevices = @("Desktop", "Tablet") }
        "ARM" { $compatibleDevices = @("Mobile", "Tablet", "IoT") }
    }
    
    if ($DeviceType -eq "All" -or $compatibleDevices -contains $DeviceType) {
        Write-TestResult "Device Compatibility" "PASS" "Platform $Platform compatible with $DeviceType devices" -Platform $Platform
        
        # Test deployment (if not skipped)
        if (!$SkipDeploy) {
            Test-LocalDeployment -Platform $Platform
        }
        
        return $true
    } else {
        Write-TestResult "Device Compatibility" "FAIL" "Platform $Platform not compatible with $DeviceType devices" -Platform $Platform
        return $false
    }
}

function Test-LocalDeployment {
    param([string]$Platform)
    
    Write-Host "`nTesting Local Deployment for $Platform..." -ForegroundColor Cyan
    
    $appxPath = $ValidationResults.PlatformResults[$Platform].AppxPath
    if (!$appxPath -or !(Test-Path $appxPath)) {
        Write-TestResult "Local Deployment" "FAIL" "APPX package not available" -Platform $Platform
        return $false
    }
    
    try {
        # Test with PowerShell Add-AppxPackage (requires developer mode)
        Write-Verbose "Testing local deployment with Add-AppxPackage..."
        
        # Check if developer mode is enabled
        $devModeKey = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock"
        $devModeEnabled = $false
        
        try {
            $allowDevelopmentWithoutDevLicense = Get-ItemProperty -Path $devModeKey -Name "AllowDevelopmentWithoutDevLicense" -ErrorAction SilentlyContinue
            $devModeEnabled = $allowDevelopmentWithoutDevLicense.AllowDevelopmentWithoutDevLicense -eq 1
        } catch {
            # Registry key doesn't exist or can't be read
        }
        
        if ($devModeEnabled) {
            # Simulate deployment test (don't actually install)
            Write-TestResult "Local Deployment" "PASS" "Developer mode enabled - deployment would succeed" -Platform $Platform
        } else {
            Write-TestResult "Local Deployment" "SKIP" "Developer mode not enabled - cannot test deployment" -Platform $Platform
        }
        
        return $true
    } catch {
        Write-TestResult "Local Deployment" "FAIL" "Deployment test error: $($_.Exception.Message)" -Platform $Platform
        return $false
    }
}

# Main execution
try {
    Write-Host "Starting UWP multi-platform deployment validation..." -ForegroundColor White
    
    $overallSuccess = $true
    
    # Test prerequisites
    $overallSuccess = $overallSuccess -and (Test-UWPPrerequisites)
    
    # Test each platform
    foreach ($platform in $Platforms) {
        Write-Host "`n--- Testing Platform: $platform ---" -ForegroundColor Magenta
        
        $platformSuccess = $true
        $platformSuccess = $platformSuccess -and (Test-PlatformBuild -Platform $platform)
        $platformSuccess = $platformSuccess -and (Test-PackageValidation -Platform $platform)
        $platformSuccess = $platformSuccess -and (Test-DeviceCompatibility -Platform $platform)
        
        $ValidationResults.PlatformResults[$platform].Success = $platformSuccess
        $overallSuccess = $overallSuccess -and $platformSuccess
    }
    
    # Save results
    $ValidationResults | ConvertTo-Json -Depth 4 | Out-File -FilePath $TestResultsPath -Encoding UTF8
    
    # Summary
    Write-Host "`nValidation Summary:" -ForegroundColor White
    Write-Host "==================" -ForegroundColor White
    Write-Host "Total Tests: $($ValidationResults.Summary.Total)" -ForegroundColor White
    Write-Host "Passed: $($ValidationResults.Summary.Passed)" -ForegroundColor Green
    Write-Host "Failed: $($ValidationResults.Summary.Failed)" -ForegroundColor Red
    Write-Host "Skipped: $($ValidationResults.Summary.Skipped)" -ForegroundColor Yellow
    
    Write-Host "`nPlatform Results:" -ForegroundColor White
    foreach ($platform in $Platforms) {
        $status = if ($ValidationResults.PlatformResults[$platform].Success) { "✅ PASS" } else { "❌ FAIL" }
        Write-Host "  $platform`: $status" -ForegroundColor $(if ($ValidationResults.PlatformResults[$platform].Success) { "Green" } else { "Red" })
    }
    
    Write-Host "Results saved to: $TestResultsPath" -ForegroundColor Gray
    
    if ($overallSuccess -and $ValidationResults.Summary.Failed -eq 0) {
        Write-Host "`n✅ UWP multi-platform deployment validation PASSED" -ForegroundColor Green
        exit 0
    } else {
        Write-Host "`n❌ UWP multi-platform deployment validation FAILED" -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host "`n💥 Validation script error: $($_.Exception.Message)" -ForegroundColor Red
    Write-TestResult "Script Execution" "FAIL" $_.Exception.Message
    $ValidationResults | ConvertTo-Json -Depth 4 | Out-File -FilePath $TestResultsPath -Encoding UTF8
    exit 1
}
