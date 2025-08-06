# LocalTalk Cross-Platform Build Validation Script
# This script validates that both Windows Phone and UWP projects compile successfully

param(
    [switch]$WindowsPhone,
    [switch]$UWP,
    [switch]$All,
    [switch]$Clean,
    [string]$Configuration = "Debug"
)

Write-Host "LocalTalk Cross-Platform Build Validation" -ForegroundColor Green
Write-Host "=========================================" -ForegroundColor Green

# Function to test build
function Test-Build {
    param(
        [string]$ProjectPath,
        [string]$ProjectName,
        [string]$Platform = "Any CPU"
    )
    
    Write-Host "`nBuilding $ProjectName..." -ForegroundColor Yellow
    Write-Host "Project: $ProjectPath" -ForegroundColor Gray
    Write-Host "Platform: $Platform" -ForegroundColor Gray
    Write-Host "Configuration: $Configuration" -ForegroundColor Gray
    
    if ($Clean) {
        Write-Host "Cleaning project..." -ForegroundColor Cyan
        & msbuild $ProjectPath /t:Clean /p:Configuration=$Configuration /p:Platform="$Platform" /verbosity:minimal
    }
    
    # Build the project
    $buildResult = & msbuild $ProjectPath /t:Build /p:Configuration=$Configuration /p:Platform="$Platform" /verbosity:minimal 2>&1
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✅ $ProjectName build SUCCESSFUL" -ForegroundColor Green
        return $true
    } else {
        Write-Host "❌ $ProjectName build FAILED" -ForegroundColor Red
        Write-Host "Build output:" -ForegroundColor Red
        $buildResult | Write-Host
        return $false
    }
}

# Function to validate project files
function Test-ProjectFiles {
    param([string]$ProjectPath)
    
    Write-Host "`nValidating project files for $ProjectPath..." -ForegroundColor Cyan
    
    if (!(Test-Path $ProjectPath)) {
        Write-Host "❌ Project file not found: $ProjectPath" -ForegroundColor Red
        return $false
    }
    
    # Check for required files
    $projectDir = Split-Path $ProjectPath -Parent
    $requiredFiles = @()
    
    if ($ProjectPath -like "*LocalTalk.csproj") {
        $requiredFiles = @(
            "App.xaml",
            "MainPage.xaml",
            "Resources\PlatformResources.xaml",
            "Views\SendPage.xaml",
            "Views\ReceivePage.xaml"
        )
    } elseif ($ProjectPath -like "*LocalTalkUWP.csproj") {
        $requiredFiles = @(
            "App.xaml",
            "MainPage.xaml",
            "Resources\PlatformResources.xaml"
        )
    }
    
    $allFilesExist = $true
    foreach ($file in $requiredFiles) {
        $fullPath = Join-Path $projectDir $file
        if (!(Test-Path $fullPath)) {
            Write-Host "❌ Missing required file: $file" -ForegroundColor Red
            $allFilesExist = $false
        } else {
            Write-Host "✅ Found: $file" -ForegroundColor Green
        }
    }
    
    return $allFilesExist
}

# Function to validate XAML files
function Test-XamlFiles {
    param([string]$ProjectDir)
    
    Write-Host "`nValidating XAML files in $ProjectDir..." -ForegroundColor Cyan
    
    $xamlFiles = Get-ChildItem -Path $ProjectDir -Filter "*.xaml" -Recurse
    $xamlValid = $true
    
    foreach ($xaml in $xamlFiles) {
        try {
            [xml]$xmlContent = Get-Content $xaml.FullName
            Write-Host "✅ Valid XAML: $($xaml.Name)" -ForegroundColor Green
        } catch {
            Write-Host "❌ Invalid XAML: $($xaml.Name) - $($_.Exception.Message)" -ForegroundColor Red
            $xamlValid = $false
        }
    }
    
    return $xamlValid
}

# Main execution
$buildResults = @()

if ($All -or $WindowsPhone) {
    Write-Host "`n=== Windows Phone 8.x Project ===" -ForegroundColor Magenta
    
    $wpProjectPath = "LocalTalk\LocalTalk.csproj"
    $wpProjectDir = "LocalTalk"
    
    # Validate project files
    $wpFilesValid = Test-ProjectFiles $wpProjectPath
    $wpXamlValid = Test-XamlFiles $wpProjectDir
    
    if ($wpFilesValid -and $wpXamlValid) {
        $wpBuildResult = Test-Build $wpProjectPath "Windows Phone 8.x" "Any CPU"
        $buildResults += @{
            Project = "Windows Phone 8.x"
            Success = $wpBuildResult
            FilesValid = $wpFilesValid
            XamlValid = $wpXamlValid
        }
    } else {
        Write-Host "❌ Windows Phone project validation failed" -ForegroundColor Red
        $buildResults += @{
            Project = "Windows Phone 8.x"
            Success = $false
            FilesValid = $wpFilesValid
            XamlValid = $wpXamlValid
        }
    }
}

if ($All -or $UWP) {
    Write-Host "`n=== UWP Project ===" -ForegroundColor Magenta
    
    $uwpProjectPath = "LocalTalkUWP\LocalTalkUWP.csproj"
    $uwpProjectDir = "LocalTalkUWP"
    
    # Validate project files
    $uwpFilesValid = Test-ProjectFiles $uwpProjectPath
    $uwpXamlValid = Test-XamlFiles $uwpProjectDir
    
    if ($uwpFilesValid -and $uwpXamlValid) {
        # UWP supports multiple platforms
        $platforms = @("x86", "x64", "ARM")
        $uwpBuildSuccess = $true
        
        foreach ($platform in $platforms) {
            $platformResult = Test-Build $uwpProjectPath "UWP ($platform)" $platform
            if (!$platformResult) {
                $uwpBuildSuccess = $false
            }
        }
        
        $buildResults += @{
            Project = "UWP"
            Success = $uwpBuildSuccess
            FilesValid = $uwpFilesValid
            XamlValid = $uwpXamlValid
        }
    } else {
        Write-Host "❌ UWP project validation failed" -ForegroundColor Red
        $buildResults += @{
            Project = "UWP"
            Success = $false
            FilesValid = $uwpFilesValid
            XamlValid = $uwpXamlValid
        }
    }
}

# Summary
Write-Host "`n=== Build Validation Summary ===" -ForegroundColor Green
Write-Host "=================================" -ForegroundColor Green

$overallSuccess = $true
foreach ($result in $buildResults) {
    $status = if ($result.Success) { "✅ PASS" } else { "❌ FAIL" }
    Write-Host "$($result.Project): $status" -ForegroundColor $(if ($result.Success) { "Green" } else { "Red" })
    
    if (!$result.Success) {
        $overallSuccess = $false
    }
}

if ($overallSuccess) {
    Write-Host "`n🎉 All builds SUCCESSFUL!" -ForegroundColor Green
    exit 0
} else {
    Write-Host "`n💥 Some builds FAILED!" -ForegroundColor Red
    exit 1
}
