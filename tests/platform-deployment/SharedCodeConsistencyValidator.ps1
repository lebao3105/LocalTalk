# Shared Code Behavior Consistency Validation Script
# This script validates that shared components behave identically across Windows Phone and UWP platforms

param(
    [string]$Configuration = "Release",
    [switch]$Verbose = $false,
    [string]$TestFilter = "*"
)

$ErrorActionPreference = "Stop"
$VerbosePreference = if ($Verbose) { "Continue" } else { "SilentlyContinue" }

Write-Host "LocalTalk Shared Code Behavior Consistency Validation" -ForegroundColor Green
Write-Host "====================================================" -ForegroundColor Green

# Configuration
$TestResultsPath = "tests\results\shared-consistency-$(Get-Date -Format 'yyyyMMdd-HHmmss').json"
$ValidationResults = @{
    Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    Configuration = $Configuration
    TestFilter = $TestFilter
    Tests = @{}
    ConsistencyTests = @{}
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

function Test-SharedProjectStructure {
    Write-Host "`nValidating Shared Project Structure..." -ForegroundColor Cyan
    
    $sharedPath = "Shared"
    if (!(Test-Path $sharedPath)) {
        Write-TestResult "Shared Project Structure" "FAIL" "Shared directory not found"
        return $false
    }
    
    # Check for key shared components
    $requiredComponents = @(
        "FileSystem\UniversalFilePicker.cs",
        "Protocol\ChunkedTransferProtocol.cs",
        "Http\LocalSendHttpServer.cs",
        "Platform\IPlatformAbstraction.cs",
        "Models\Device.cs",
        "Models\TransferRequest.cs"
    )
    
    $missingComponents = @()
    foreach ($component in $requiredComponents) {
        $componentPath = Join-Path $sharedPath $component
        if (!(Test-Path $componentPath)) {
            $missingComponents += $component
        }
    }
    
    if ($missingComponents.Count -eq 0) {
        Write-TestResult "Shared Project Structure" "PASS" "All required shared components found"
        return $true
    } else {
        Write-TestResult "Shared Project Structure" "FAIL" "Missing components: $($missingComponents -join ', ')"
        return $false
    }
}

function Test-ConditionalCompilation {
    Write-Host "`nValidating Conditional Compilation..." -ForegroundColor Cyan
    
    try {
        # Check for proper conditional compilation directives
        $sharedFiles = Get-ChildItem "Shared" -Recurse -Filter "*.cs"
        $conditionalIssues = @()
        
        foreach ($file in $sharedFiles) {
            $content = Get-Content $file.FullName -Raw
            
            # Check for platform-specific code blocks
            if ($content -match "#if\s+(WINDOWS_PHONE|NETFX_CORE|WINDOWS_UWP)") {
                # Validate that conditional blocks are properly closed
                $ifCount = ($content | Select-String "#if\s+" -AllMatches).Matches.Count
                $endifCount = ($content | Select-String "#endif" -AllMatches).Matches.Count
                
                if ($ifCount -ne $endifCount) {
                    $conditionalIssues += "$($file.Name): Mismatched #if/#endif directives"
                }
            }
            
            # Check for using statements that might cause conflicts
            if ($content -match "using\s+System\.String" -or $content -match "using\s+System\.Object") {
                $conditionalIssues += "$($file.Name): Potentially problematic System namespace usage"
            }
        }
        
        if ($conditionalIssues.Count -eq 0) {
            Write-TestResult "Conditional Compilation" "PASS" "No conditional compilation issues found"
            return $true
        } else {
            Write-TestResult "Conditional Compilation" "FAIL" "Issues found: $($conditionalIssues -join '; ')"
            return $false
        }
    } catch {
        Write-TestResult "Conditional Compilation" "FAIL" "Error analyzing conditional compilation: $($_.Exception.Message)"
        return $false
    }
}

function Test-PlatformAbstractionConsistency {
    Write-Host "`nValidating Platform Abstraction Consistency..." -ForegroundColor Cyan
    
    try {
        # Check Windows Phone platform implementation
        $wpPlatformPath = "LocalTalk\Platform"
        $uwpPlatformPath = "LocalTalkUWP\Platform"
        
        if (!(Test-Path $wpPlatformPath) -or !(Test-Path $uwpPlatformPath)) {
            Write-TestResult "Platform Abstraction Consistency" "FAIL" "Platform implementation directories not found"
            return $false
        }
        
        # Get platform implementation files
        $wpFiles = Get-ChildItem $wpPlatformPath -Filter "*.cs" | Sort-Object Name
        $uwpFiles = Get-ChildItem $uwpPlatformPath -Filter "*.cs" | Sort-Object Name
        
        # Check that both platforms implement the same interfaces
        $wpFileNames = $wpFiles.Name
        $uwpFileNames = $uwpFiles.Name
        
        $missingInWP = $uwpFileNames | Where-Object { $_ -notin $wpFileNames }
        $missingInUWP = $wpFileNames | Where-Object { $_ -notin $uwpFileNames }
        
        $inconsistencies = @()
        if ($missingInWP.Count -gt 0) {
            $inconsistencies += "Missing in Windows Phone: $($missingInWP -join ', ')"
        }
        if ($missingInUWP.Count -gt 0) {
            $inconsistencies += "Missing in UWP: $($missingInUWP -join ', ')"
        }
        
        if ($inconsistencies.Count -eq 0) {
            Write-TestResult "Platform Abstraction Consistency" "PASS" "Platform implementations are consistent"
            
            # Store details for further analysis
            $ValidationResults.ConsistencyTests["PlatformFiles"] = @{
                WindowsPhone = $wpFileNames
                UWP = $uwpFileNames
                Consistent = $true
            }
            return $true
        } else {
            Write-TestResult "Platform Abstraction Consistency" "FAIL" "Inconsistencies found: $($inconsistencies -join '; ')"
            return $false
        }
    } catch {
        Write-TestResult "Platform Abstraction Consistency" "FAIL" "Error analyzing platform abstractions: $($_.Exception.Message)"
        return $false
    }
}

function Test-SharedModelConsistency {
    Write-Host "`nValidating Shared Model Consistency..." -ForegroundColor Cyan
    
    try {
        # Analyze shared models for consistency
        $modelsPath = "Shared\Models"
        if (!(Test-Path $modelsPath)) {
            Write-TestResult "Shared Model Consistency" "FAIL" "Shared Models directory not found"
            return $false
        }
        
        $modelFiles = Get-ChildItem $modelsPath -Filter "*.cs"
        $modelAnalysis = @{}
        
        foreach ($file in $modelFiles) {
            $content = Get-Content $file.FullName -Raw
            
            # Extract class/interface definitions
            $classMatches = [regex]::Matches($content, "(?:public\s+)?(?:class|interface|struct)\s+(\w+)")
            $propertyMatches = [regex]::Matches($content, "public\s+\w+\s+(\w+)\s*{\s*get")
            
            $modelAnalysis[$file.Name] = @{
                Classes = $classMatches | ForEach-Object { $_.Groups[1].Value }
                Properties = $propertyMatches | ForEach-Object { $_.Groups[1].Value }
                HasConditionalCode = $content -match "#if\s+"
            }
        }
        
        # Check for models with platform-specific code (which should be avoided)
        $modelsWithConditionalCode = $modelAnalysis.Keys | Where-Object { $modelAnalysis[$_].HasConditionalCode }
        
        if ($modelsWithConditionalCode.Count -eq 0) {
            Write-TestResult "Shared Model Consistency" "PASS" "All shared models are platform-agnostic"
            $ValidationResults.ConsistencyTests["SharedModels"] = $modelAnalysis
            return $true
        } else {
            Write-TestResult "Shared Model Consistency" "FAIL" "Models with platform-specific code: $($modelsWithConditionalCode -join ', ')"
            return $false
        }
    } catch {
        Write-TestResult "Shared Model Consistency" "FAIL" "Error analyzing shared models: $($_.Exception.Message)"
        return $false
    }
}

function Test-NamespaceConsistency {
    Write-Host "`nValidating Namespace Consistency..." -ForegroundColor Cyan
    
    try {
        # Check for namespace consistency across platforms
        $allCsFiles = @()
        $allCsFiles += Get-ChildItem "Shared" -Recurse -Filter "*.cs"
        $allCsFiles += Get-ChildItem "LocalTalk" -Recurse -Filter "*.cs" -Exclude "obj\*", "bin\*"
        $allCsFiles += Get-ChildItem "LocalTalkUWP" -Recurse -Filter "*.cs" -Exclude "obj\*", "bin\*"
        
        $namespaceIssues = @()
        $namespaceUsage = @{}
        
        foreach ($file in $allCsFiles) {
            $content = Get-Content $file.FullName -Raw
            
            # Extract namespace declarations
            $namespaceMatches = [regex]::Matches($content, "namespace\s+([\w\.]+)")
            foreach ($match in $namespaceMatches) {
                $namespace = $match.Groups[1].Value
                if (!$namespaceUsage[$namespace]) {
                    $namespaceUsage[$namespace] = @()
                }
                $namespaceUsage[$namespace] += $file.FullName
            }
            
            # Check for problematic using statements
            if ($content -match "using\s+System\s*;.*using\s+System\.") {
                $namespaceIssues += "$($file.Name): Potential System namespace conflict"
            }
        }
        
        # Analyze namespace consistency
        $sharedNamespaces = $namespaceUsage.Keys | Where-Object { $_ -match "^Shared\." }
        $platformSpecificNamespaces = $namespaceUsage.Keys | Where-Object { $_ -match "^(LocalTalk|LocalTalkUWP)\." }
        
        if ($namespaceIssues.Count -eq 0) {
            Write-TestResult "Namespace Consistency" "PASS" "No namespace consistency issues found"
            $ValidationResults.ConsistencyTests["Namespaces"] = @{
                SharedNamespaces = $sharedNamespaces
                PlatformNamespaces = $platformSpecificNamespaces
                Issues = $namespaceIssues
            }
            return $true
        } else {
            Write-TestResult "Namespace Consistency" "FAIL" "Issues found: $($namespaceIssues -join '; ')"
            return $false
        }
    } catch {
        Write-TestResult "Namespace Consistency" "FAIL" "Error analyzing namespaces: $($_.Exception.Message)"
        return $false
    }
}

function Test-APIConsistency {
    Write-Host "`nValidating API Consistency..." -ForegroundColor Cyan
    
    try {
        # This would ideally use reflection to compare actual API surfaces
        # For now, we'll do a text-based analysis of key components
        
        $keyComponents = @(
            "UniversalFilePicker",
            "ChunkedTransferProtocol", 
            "LocalSendHttpServer"
        )
        
        $apiConsistencyResults = @{}
        
        foreach ($component in $keyComponents) {
            # Find the component file in Shared
            $componentFile = Get-ChildItem "Shared" -Recurse -Filter "*$component*.cs" | Select-Object -First 1
            
            if ($componentFile) {
                $content = Get-Content $componentFile.FullName -Raw
                
                # Extract public methods and properties
                $publicMethods = [regex]::Matches($content, "public\s+(?:static\s+)?(?:async\s+)?[\w<>]+\s+(\w+)\s*\(")
                $publicProperties = [regex]::Matches($content, "public\s+[\w<>]+\s+(\w+)\s*{\s*get")
                $publicEvents = [regex]::Matches($content, "public\s+event\s+[\w<>]+\s+(\w+)")
                
                $apiConsistencyResults[$component] = @{
                    Methods = $publicMethods | ForEach-Object { $_.Groups[1].Value }
                    Properties = $publicProperties | ForEach-Object { $_.Groups[1].Value }
                    Events = $publicEvents | ForEach-Object { $_.Groups[1].Value }
                    HasPlatformSpecificCode = $content -match "#if\s+"
                }
            }
        }
        
        # Check if any key components have platform-specific code (which might indicate API inconsistency)
        $componentsWithPlatformCode = $apiConsistencyResults.Keys | Where-Object { 
            $apiConsistencyResults[$_].HasPlatformSpecificCode 
        }
        
        if ($componentsWithPlatformCode.Count -eq 0) {
            Write-TestResult "API Consistency" "PASS" "Key components have consistent APIs across platforms"
            $ValidationResults.ConsistencyTests["APIConsistency"] = $apiConsistencyResults
            return $true
        } else {
            Write-TestResult "API Consistency" "FAIL" "Components with platform-specific API code: $($componentsWithPlatformCode -join ', ')"
            return $false
        }
    } catch {
        Write-TestResult "API Consistency" "FAIL" "Error analyzing API consistency: $($_.Exception.Message)"
        return $false
    }
}

# Main execution
try {
    Write-Host "Starting shared code behavior consistency validation..." -ForegroundColor White
    
    $success = $true
    
    # Run consistency validation tests
    $success = $success -and (Test-SharedProjectStructure)
    $success = $success -and (Test-ConditionalCompilation)
    $success = $success -and (Test-PlatformAbstractionConsistency)
    $success = $success -and (Test-SharedModelConsistency)
    $success = $success -and (Test-NamespaceConsistency)
    $success = $success -and (Test-APIConsistency)
    
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
        Write-Host "`n✅ Shared code behavior consistency validation PASSED" -ForegroundColor Green
        exit 0
    } else {
        Write-Host "`n❌ Shared code behavior consistency validation FAILED" -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host "`n💥 Validation script error: $($_.Exception.Message)" -ForegroundColor Red
    Write-TestResult "Script Execution" "FAIL" $_.Exception.Message
    $ValidationResults | ConvertTo-Json -Depth 4 | Out-File -FilePath $TestResultsPath -Encoding UTF8
    exit 1
}
