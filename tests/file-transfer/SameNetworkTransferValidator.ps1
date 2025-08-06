# Same-Network File Transfer Validation Script
# This script validates file transfers between devices on the same WiFi network

param(
    [string]$SenderIP = "",
    [string]$ReceiverIP = "",
    [int]$Port = 53317,
    [string]$TestDataPath = "tests\test-data",
    [switch]$GenerateTestFiles = $false,
    [switch]$Verbose = $false,
    [int]$TimeoutSeconds = 300
)

$ErrorActionPreference = "Stop"
$VerbosePreference = if ($Verbose) { "Continue" } else { "SilentlyContinue" }

Write-Host "LocalTalk Same-Network File Transfer Validation" -ForegroundColor Green
Write-Host "===============================================" -ForegroundColor Green

# Configuration
$TestResultsPath = "tests\results\same-network-transfer-$(Get-Date -Format 'yyyyMMdd-HHmmss').json"
$ValidationResults = @{
    Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    SenderIP = $SenderIP
    ReceiverIP = $ReceiverIP
    Port = $Port
    TimeoutSeconds = $TimeoutSeconds
    Tests = @{}
    TransferResults = @{}
    Summary = @{
        Total = 0
        Passed = 0
        Failed = 0
        Skipped = 0
    }
}

# Ensure directories exist
$ResultsDir = Split-Path $TestResultsPath -Parent
if (!(Test-Path $ResultsDir)) {
    New-Item -ItemType Directory -Path $ResultsDir -Force | Out-Null
}
if (!(Test-Path $TestDataPath)) {
    New-Item -ItemType Directory -Path $TestDataPath -Force | Out-Null
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

function New-TestFile {
    param(
        [string]$FileName,
        [int]$SizeInBytes,
        [string]$Content = $null
    )
    
    $filePath = Join-Path $TestDataPath $FileName
    
    if ($Content) {
        # Text content
        $Content | Out-File -FilePath $filePath -Encoding UTF8
    } else {
        # Binary content
        $bytes = New-Object byte[] $SizeInBytes
        $random = New-Object System.Random
        $random.NextBytes($bytes)
        [System.IO.File]::WriteAllBytes($filePath, $bytes)
    }
    
    return $filePath
}

function Test-NetworkConnectivity {
    Write-Host "`nTesting Network Connectivity..." -ForegroundColor Cyan
    
    if (!$SenderIP -or !$ReceiverIP) {
        Write-TestResult "Network Connectivity" "SKIP" "Sender or Receiver IP not specified"
        return $false
    }
    
    try {
        # Test ping connectivity
        $senderPing = Test-Connection -ComputerName $SenderIP -Count 2 -Quiet
        $receiverPing = Test-Connection -ComputerName $ReceiverIP -Count 2 -Quiet
        
        if ($senderPing -and $receiverPing) {
            Write-TestResult "Network Connectivity" "PASS" "Both devices are reachable"
            
            # Test port connectivity
            $portTest = Test-NetConnection -ComputerName $ReceiverIP -Port $Port -WarningAction SilentlyContinue
            if ($portTest.TcpTestSucceeded) {
                Write-TestResult "Port Connectivity" "PASS" "Port $Port is accessible on receiver"
                return $true
            } else {
                Write-TestResult "Port Connectivity" "FAIL" "Port $Port is not accessible on receiver"
                return $false
            }
        } else {
            $unreachable = @()
            if (!$senderPing) { $unreachable += "Sender ($SenderIP)" }
            if (!$receiverPing) { $unreachable += "Receiver ($ReceiverIP)" }
            
            Write-TestResult "Network Connectivity" "FAIL" "Unreachable devices: $($unreachable -join ', ')"
            return $false
        }
    } catch {
        Write-TestResult "Network Connectivity" "FAIL" "Error testing connectivity: $($_.Exception.Message)"
        return $false
    }
}

function Test-DeviceDiscovery {
    Write-Host "`nTesting Device Discovery..." -ForegroundColor Cyan
    
    try {
        # Test LocalSend device discovery via HTTP
        $discoveryUrl = "http://${ReceiverIP}:${Port}/api/localsend/v2/info"
        
        Write-Verbose "Testing device discovery at $discoveryUrl"
        $response = Invoke-RestMethod -Uri $discoveryUrl -Method GET -TimeoutSec 10 -ErrorAction Stop
        
        if ($response -and $response.alias) {
            Write-TestResult "Device Discovery" "PASS" "Discovered device: $($response.alias)"
            $ValidationResults.TransferResults["ReceiverInfo"] = $response
            return $true
        } else {
            Write-TestResult "Device Discovery" "FAIL" "Invalid response from device discovery"
            return $false
        }
    } catch {
        Write-TestResult "Device Discovery" "FAIL" "Device discovery failed: $($_.Exception.Message)"
        return $false
    }
}

function Test-FileTransfer {
    param(
        [string]$TestFileName,
        [string]$FilePath,
        [string]$Description
    )
    
    Write-Host "`nTesting File Transfer: $Description..." -ForegroundColor Cyan
    
    if (!(Test-Path $FilePath)) {
        Write-TestResult "File Transfer - $Description" "FAIL" "Test file not found: $FilePath"
        return $false
    }
    
    try {
        $fileInfo = Get-Item $FilePath
        $startTime = Get-Date
        
        # Prepare upload request
        $uploadData = @{
            info = @{
                alias = "ValidationTest"
                version = "2.0"
                deviceModel = "TestDevice"
                deviceType = "desktop"
                fingerprint = [System.Guid]::NewGuid().ToString()
            }
            files = @{
                $TestFileName = @{
                    fileName = $fileInfo.Name
                    size = $fileInfo.Length
                    fileType = "application/octet-stream"
                    lastModified = [DateTimeOffset]::FromFileTime($fileInfo.LastWriteTime.ToFileTime()).ToUnixTimeMilliseconds()
                    preview = $null
                }
            }
        }
        
        # Send prepare upload request
        $prepareUrl = "http://${ReceiverIP}:${Port}/api/localsend/v2/prepare-upload"
        Write-Verbose "Sending prepare upload request to $prepareUrl"
        
        $prepareResponse = Invoke-RestMethod -Uri $prepareUrl -Method POST -Body ($uploadData | ConvertTo-Json -Depth 4) -ContentType "application/json" -TimeoutSec 30
        
        if (!$prepareResponse -or !$prepareResponse.sessionId) {
            Write-TestResult "File Transfer - $Description" "FAIL" "Prepare upload failed"
            return $false
        }
        
        $sessionId = $prepareResponse.sessionId
        Write-Verbose "Upload session created: $sessionId"
        
        # Upload file
        $uploadUrl = "http://${ReceiverIP}:${Port}/api/localsend/v2/upload?sessionId=$sessionId&fileId=$TestFileName"
        Write-Verbose "Uploading file to $uploadUrl"
        
        $fileBytes = [System.IO.File]::ReadAllBytes($FilePath)
        $uploadResponse = Invoke-RestMethod -Uri $uploadUrl -Method POST -Body $fileBytes -ContentType "application/octet-stream" -TimeoutSec $TimeoutSeconds
        
        $endTime = Get-Date
        $duration = ($endTime - $startTime).TotalSeconds
        $speedMbps = ($fileInfo.Length * 8) / ($duration * 1024 * 1024)
        
        if ($uploadResponse) {
            Write-TestResult "File Transfer - $Description" "PASS" "Transfer completed in $([math]::Round($duration, 2))s at $([math]::Round($speedMbps, 2)) Mbps"
            
            $ValidationResults.TransferResults[$TestFileName] = @{
                FileName = $fileInfo.Name
                FileSize = $fileInfo.Length
                Duration = $duration
                SpeedMbps = $speedMbps
                SessionId = $sessionId
                Success = $true
            }
            return $true
        } else {
            Write-TestResult "File Transfer - $Description" "FAIL" "Upload failed - no response"
            return $false
        }
    } catch {
        Write-TestResult "File Transfer - $Description" "FAIL" "Transfer error: $($_.Exception.Message)"
        return $false
    }
}

function Test-MultipleFileTypes {
    Write-Host "`nTesting Multiple File Types..." -ForegroundColor Cyan
    
    if ($GenerateTestFiles) {
        Write-Host "Generating test files..." -ForegroundColor Yellow
        
        # Generate various test files
        $testFiles = @(
            @{ Name = "small-text.txt"; Size = 1024; Content = "This is a small text file for testing LocalTalk file transfers.`n" * 20 },
            @{ Name = "medium-binary.bin"; Size = 1024 * 100; Content = $null },  # 100KB
            @{ Name = "large-data.dat"; Size = 1024 * 1024 * 5; Content = $null }, # 5MB
            @{ Name = "unicode-text.txt"; Size = 0; Content = "Unicode test: 🚀 LocalTalk 📁 File Transfer 🌐 Network 💾 Storage" },
            @{ Name = "empty-file.txt"; Size = 0; Content = "" }
        )
        
        foreach ($testFile in $testFiles) {
            if ($testFile.Content -ne $null) {
                $filePath = New-TestFile -FileName $testFile.Name -SizeInBytes $testFile.Size -Content $testFile.Content
            } else {
                $filePath = New-TestFile -FileName $testFile.Name -SizeInBytes $testFile.Size
            }
            Write-Verbose "Generated test file: $filePath"
        }
    }
    
    # Test transfers with different file types
    $testFiles = Get-ChildItem $TestDataPath -File
    $successCount = 0
    
    foreach ($file in $testFiles) {
        $description = "$($file.Name) ($([math]::Round($file.Length / 1024, 2)) KB)"
        $success = Test-FileTransfer -TestFileName $file.Name -FilePath $file.FullName -Description $description
        if ($success) { $successCount++ }
    }
    
    if ($successCount -eq $testFiles.Count) {
        Write-TestResult "Multiple File Types" "PASS" "All $($testFiles.Count) file types transferred successfully"
        return $true
    } elseif ($successCount -gt 0) {
        Write-TestResult "Multiple File Types" "FAIL" "Only $successCount of $($testFiles.Count) file types transferred successfully"
        return $false
    } else {
        Write-TestResult "Multiple File Types" "FAIL" "No file transfers succeeded"
        return $false
    }
}

function Test-TransferReliability {
    Write-Host "`nTesting Transfer Reliability..." -ForegroundColor Cyan
    
    # Test the same file multiple times to check consistency
    $testFile = Get-ChildItem $TestDataPath -File | Select-Object -First 1
    if (!$testFile) {
        Write-TestResult "Transfer Reliability" "SKIP" "No test files available"
        return $false
    }
    
    $attempts = 3
    $successCount = 0
    $transferTimes = @()
    
    for ($i = 1; $i -le $attempts; $i++) {
        Write-Verbose "Reliability test attempt $i of $attempts"
        $startTime = Get-Date
        $success = Test-FileTransfer -TestFileName "reliability-test-$i-$($testFile.Name)" -FilePath $testFile.FullName -Description "Reliability Test $i"
        $endTime = Get-Date
        
        if ($success) {
            $successCount++
            $transferTimes += ($endTime - $startTime).TotalSeconds
        }
        
        # Small delay between attempts
        Start-Sleep -Seconds 2
    }
    
    if ($successCount -eq $attempts) {
        $avgTime = ($transferTimes | Measure-Object -Average).Average
        $maxTime = ($transferTimes | Measure-Object -Maximum).Maximum
        $minTime = ($transferTimes | Measure-Object -Minimum).Minimum
        
        Write-TestResult "Transfer Reliability" "PASS" "All $attempts attempts succeeded. Avg: $([math]::Round($avgTime, 2))s, Min: $([math]::Round($minTime, 2))s, Max: $([math]::Round($maxTime, 2))s"
        
        $ValidationResults.TransferResults["ReliabilityTest"] = @{
            Attempts = $attempts
            Successes = $successCount
            AverageTime = $avgTime
            MinTime = $minTime
            MaxTime = $maxTime
        }
        return $true
    } else {
        Write-TestResult "Transfer Reliability" "FAIL" "Only $successCount of $attempts attempts succeeded"
        return $false
    }
}

# Main execution
try {
    Write-Host "Starting same-network file transfer validation..." -ForegroundColor White
    
    $success = $true
    
    # Run validation tests
    $success = $success -and (Test-NetworkConnectivity)
    $success = $success -and (Test-DeviceDiscovery)
    $success = $success -and (Test-MultipleFileTypes)
    $success = $success -and (Test-TransferReliability)
    
    # Save results
    $ValidationResults | ConvertTo-Json -Depth 4 | Out-File -FilePath $TestResultsPath -Encoding UTF8
    
    # Summary
    Write-Host "`nValidation Summary:" -ForegroundColor White
    Write-Host "==================" -ForegroundColor White
    Write-Host "Total Tests: $($ValidationResults.Summary.Total)" -ForegroundColor White
    Write-Host "Passed: $($ValidationResults.Summary.Passed)" -ForegroundColor Green
    Write-Host "Failed: $($ValidationResults.Summary.Failed)" -ForegroundColor Red
    Write-Host "Skipped: $($ValidationResults.Summary.Skipped)" -ForegroundColor Yellow
    
    # Transfer statistics
    $transferResults = $ValidationResults.TransferResults.Values | Where-Object { $_.Success -eq $true }
    if ($transferResults.Count -gt 0) {
        $avgSpeed = ($transferResults | Measure-Object -Property SpeedMbps -Average).Average
        $totalData = ($transferResults | Measure-Object -Property FileSize -Sum).Sum
        
        Write-Host "`nTransfer Statistics:" -ForegroundColor White
        Write-Host "Successful Transfers: $($transferResults.Count)" -ForegroundColor Green
        Write-Host "Total Data Transferred: $([math]::Round($totalData / 1024 / 1024, 2)) MB" -ForegroundColor White
        Write-Host "Average Speed: $([math]::Round($avgSpeed, 2)) Mbps" -ForegroundColor White
    }
    
    Write-Host "Results saved to: $TestResultsPath" -ForegroundColor Gray
    
    if ($success -and $ValidationResults.Summary.Failed -eq 0) {
        Write-Host "`n✅ Same-network file transfer validation PASSED" -ForegroundColor Green
        exit 0
    } else {
        Write-Host "`n❌ Same-network file transfer validation FAILED" -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host "`n💥 Validation script error: $($_.Exception.Message)" -ForegroundColor Red
    Write-TestResult "Script Execution" "FAIL" $_.Exception.Message
    $ValidationResults | ConvertTo-Json -Depth 4 | Out-File -FilePath $TestResultsPath -Encoding UTF8
    exit 1
}
