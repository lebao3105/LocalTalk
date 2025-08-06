# LocalTalk Quality Dashboard Generator
# Creates an HTML dashboard for visualizing code quality metrics

param(
    [string]$ReportPath = "quality-reports",
    [string]$OutputPath = "quality-dashboard.html",
    [int]$HistoryDays = 30,
    [switch]$AutoRefresh = $false
)

function Generate-QualityDashboard {
    Write-Host "Generating LocalTalk Quality Dashboard..." -ForegroundColor Cyan
    
    # Get latest quality report
    $latestReport = Get-ChildItem -Path $ReportPath -Filter "quality-report-*.json" | 
                   Sort-Object LastWriteTime -Descending | 
                   Select-Object -First 1
    
    if (-not $latestReport) {
        Write-Host "No quality reports found in $ReportPath" -ForegroundColor Red
        return
    }
    
    $reportData = Get-Content $latestReport.FullName | ConvertFrom-Json
    
    # Generate HTML dashboard
    $html = @"
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>LocalTalk Quality Dashboard</title>
    <style>
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body { 
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; 
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            min-height: 100vh;
            padding: 20px;
        }
        .container { 
            max-width: 1200px; 
            margin: 0 auto; 
            background: white; 
            border-radius: 15px; 
            box-shadow: 0 20px 40px rgba(0,0,0,0.1);
            overflow: hidden;
        }
        .header { 
            background: linear-gradient(135deg, #2c3e50 0%, #34495e 100%);
            color: white; 
            padding: 30px; 
            text-align: center; 
        }
        .header h1 { font-size: 2.5em; margin-bottom: 10px; }
        .header p { font-size: 1.1em; opacity: 0.9; }
        .metrics-grid { 
            display: grid; 
            grid-template-columns: repeat(auto-fit, minmax(250px, 1fr)); 
            gap: 20px; 
            padding: 30px; 
        }
        .metric-card { 
            background: white; 
            border-radius: 10px; 
            padding: 25px; 
            text-align: center; 
            box-shadow: 0 5px 15px rgba(0,0,0,0.08);
            border-left: 5px solid;
            transition: transform 0.3s ease;
        }
        .metric-card:hover { transform: translateY(-5px); }
        .metric-card.excellent { border-left-color: #27ae60; }
        .metric-card.good { border-left-color: #f39c12; }
        .metric-card.warning { border-left-color: #e74c3c; }
        .metric-value { 
            font-size: 3em; 
            font-weight: bold; 
            margin-bottom: 10px; 
        }
        .metric-label { 
            font-size: 1.1em; 
            color: #7f8c8d; 
            margin-bottom: 5px; 
        }
        .metric-target { 
            font-size: 0.9em; 
            color: #95a5a6; 
        }
        .excellent .metric-value { color: #27ae60; }
        .good .metric-value { color: #f39c12; }
        .warning .metric-value { color: #e74c3c; }
        .section { 
            padding: 30px; 
            border-top: 1px solid #ecf0f1; 
        }
        .section h2 { 
            color: #2c3e50; 
            margin-bottom: 20px; 
            font-size: 1.8em; 
        }
        .violations-list { 
            background: #f8f9fa; 
            border-radius: 8px; 
            padding: 20px; 
        }
        .violation-item { 
            background: white; 
            border-radius: 5px; 
            padding: 15px; 
            margin-bottom: 10px; 
            border-left: 4px solid;
            display: flex;
            justify-content: space-between;
            align-items: center;
        }
        .violation-item.critical { border-left-color: #e74c3c; }
        .violation-item.high { border-left-color: #f39c12; }
        .violation-item.medium { border-left-color: #3498db; }
        .violation-item.minor { border-left-color: #95a5a6; }
        .severity-badge { 
            padding: 5px 10px; 
            border-radius: 15px; 
            color: white; 
            font-size: 0.8em; 
            font-weight: bold; 
        }
        .severity-critical { background: #e74c3c; }
        .severity-high { background: #f39c12; }
        .severity-medium { background: #3498db; }
        .severity-minor { background: #95a5a6; }
        .recommendations { 
            background: #e8f5e8; 
            border-radius: 8px; 
            padding: 20px; 
        }
        .recommendation-item { 
            background: white; 
            border-radius: 5px; 
            padding: 15px; 
            margin-bottom: 10px; 
            border-left: 4px solid #27ae60; 
        }
        .footer { 
            background: #34495e; 
            color: white; 
            text-align: center; 
            padding: 20px; 
            font-size: 0.9em; 
        }
        .progress-bar { 
            width: 100%; 
            height: 10px; 
            background: #ecf0f1; 
            border-radius: 5px; 
            overflow: hidden; 
            margin-top: 10px; 
        }
        .progress-fill { 
            height: 100%; 
            transition: width 0.3s ease; 
        }
        .progress-excellent { background: #27ae60; }
        .progress-good { background: #f39c12; }
        .progress-warning { background: #e74c3c; }
    </style>
    $(if ($AutoRefresh) { '<meta http-equiv="refresh" content="300">' })
</head>
<body>
    <div class="container">
        <div class="header">
            <h1>🚀 LocalTalk Quality Dashboard</h1>
            <p>Real-time code quality monitoring and metrics</p>
            <p>Last Updated: $($reportData.Timestamp)</p>
        </div>
        
        <div class="metrics-grid">
            <div class="metric-card $(Get-MetricClass $reportData.Summary.CodeQualityScore 95 85)">
                <div class="metric-value">$($reportData.Summary.CodeQualityScore)</div>
                <div class="metric-label">Code Quality Score</div>
                <div class="metric-target">Target: $($reportData.QualityTargets.CodeQualityScore)</div>
                <div class="progress-bar">
                    <div class="progress-fill $(Get-ProgressClass $reportData.Summary.CodeQualityScore 95 85)" 
                         style="width: $($reportData.Summary.CodeQualityScore)%"></div>
                </div>
            </div>
            
            <div class="metric-card $(Get-MetricClass $reportData.Summary.DocumentationCoverage 100 95)">
                <div class="metric-value">$($reportData.Summary.DocumentationCoverage)%</div>
                <div class="metric-label">Documentation Coverage</div>
                <div class="metric-target">Target: $($reportData.QualityTargets.DocumentationCoverage)%</div>
                <div class="progress-bar">
                    <div class="progress-fill $(Get-ProgressClass $reportData.Summary.DocumentationCoverage 100 95)" 
                         style="width: $($reportData.Summary.DocumentationCoverage)%"></div>
                </div>
            </div>
            
            <div class="metric-card $(Get-MetricClass $reportData.Summary.SecurityScore 95 85)">
                <div class="metric-value">$($reportData.Summary.SecurityScore)</div>
                <div class="metric-label">Security Score</div>
                <div class="metric-target">Target: 100</div>
                <div class="progress-bar">
                    <div class="progress-fill $(Get-ProgressClass $reportData.Summary.SecurityScore 95 85)" 
                         style="width: $($reportData.Summary.SecurityScore)%"></div>
                </div>
            </div>
            
            <div class="metric-card $(if ($reportData.Summary.VulnerabilitiesFound -eq 0) { 'excellent' } else { 'warning' })">
                <div class="metric-value">$($reportData.Summary.VulnerabilitiesFound)</div>
                <div class="metric-label">Security Vulnerabilities</div>
                <div class="metric-target">Target: 0</div>
            </div>
            
            <div class="metric-card excellent">
                <div class="metric-value">$($reportData.Summary.FilesAnalyzed)</div>
                <div class="metric-label">Files Analyzed</div>
                <div class="metric-target">C# Source Files</div>
            </div>
            
            <div class="metric-card excellent">
                <div class="metric-value">$([math]::Round($reportData.Summary.LinesOfCode / 1000, 1))K</div>
                <div class="metric-label">Lines of Code</div>
                <div class="metric-target">Total Codebase</div>
            </div>
        </div>
        
        $(if ($reportData.Violations -and $reportData.Violations.Count -gt 0) {
            "<div class='section'>
                <h2>🔍 Quality Violations</h2>
                <div class='violations-list'>"
            
            foreach ($violation in $reportData.Violations) {
                "<div class='violation-item $($violation.Severity.ToLower())'>
                    <div>
                        <strong>$($violation.File)</strong> - $($violation.Type)
                        $(if ($violation.Count) { " ($($violation.Count) issues)" })
                    </div>
                    <span class='severity-badge severity-$($violation.Severity.ToLower())'>$($violation.Severity)</span>
                </div>"
            }
            
            "</div>
            </div>"
        })
        
        $(if ($reportData.SecurityIssues -and $reportData.SecurityIssues.Count -gt 0) {
            "<div class='section'>
                <h2>🛡️ Security Issues</h2>
                <div class='violations-list'>"
            
            foreach ($issue in $reportData.SecurityIssues) {
                "<div class='violation-item $($issue.Severity.ToLower())'>
                    <div>
                        <strong>$($issue.File)</strong> - $($issue.Type)
                    </div>
                    <span class='severity-badge severity-$($issue.Severity.ToLower())'>$($issue.Severity)</span>
                </div>"
            }
            
            "</div>
            </div>"
        })
        
        $(if ($reportData.Recommendations -and $reportData.Recommendations.Count -gt 0) {
            "<div class='section'>
                <h2>💡 Recommendations</h2>
                <div class='recommendations'>"
            
            foreach ($recommendation in $reportData.Recommendations) {
                "<div class='recommendation-item'>$recommendation</div>"
            }
            
            "</div>
            </div>"
        })
        
        <div class="footer">
            <p>LocalTalk Quality Dashboard | Generated on $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')</p>
            $(if ($AutoRefresh) { '<p>Auto-refresh enabled (5 minutes)</p>' })
        </div>
    </div>
</body>
</html>
"@
    
    # Save dashboard
    $html | Out-File -FilePath $OutputPath -Encoding UTF8
    Write-Host "Quality dashboard generated: $OutputPath" -ForegroundColor Green
    
    # Open in browser if requested
    if ($AutoRefresh) {
        Start-Process $OutputPath
    }
}

function Get-MetricClass {
    param($Value, $ExcellentThreshold, $GoodThreshold)
    
    if ($Value -ge $ExcellentThreshold) { return "excellent" }
    elseif ($Value -ge $GoodThreshold) { return "good" }
    else { return "warning" }
}

function Get-ProgressClass {
    param($Value, $ExcellentThreshold, $GoodThreshold)
    
    if ($Value -ge $ExcellentThreshold) { return "progress-excellent" }
    elseif ($Value -ge $GoodThreshold) { return "progress-good" }
    else { return "progress-warning" }
}

# Main execution
try {
    if (-not (Test-Path $ReportPath)) {
        Write-Host "Report path not found: $ReportPath" -ForegroundColor Red
        Write-Host "Run quality-monitor.ps1 first to generate reports" -ForegroundColor Yellow
        exit 1
    }
    
    Generate-QualityDashboard
    Write-Host "Dashboard generation completed successfully!" -ForegroundColor Green
    
} catch {
    Write-Host "Error generating dashboard: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
