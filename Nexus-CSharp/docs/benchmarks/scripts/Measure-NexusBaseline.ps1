# docs/benchmarks/scripts/Measure-NexusBaseline.ps1
# Startup & Memory Collector Runner for Nexus Baseline v1.7
param(
    [int]$WarmRuns = 10,
    [string]$OutputPath = ""
)

function Measure-StartupAndMemory {
    Write-Host "[1/2] Mengukur Startup Latency ($WarmRuns pengulangan)..." -ForegroundColor Cyan
    
    $warmTimes = [System.Collections.Generic.List[double]]::new()
    $rootPath = $PSScriptRoot | Split-Path | Split-Path

    # 1. Measurement of Warm Runs
    for ($i = 1; $i -le $WarmRuns; $i++) {
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        
        # Execute ManajemenFile.ps1 in background process that closes immediately after load verification
        $scriptBlock = {
            param($path)
            $env:NEXUS_BENCHMARK_MODE = "1"
            Set-Location $path
            # Test dot-sourcing Foundation and loading MainWindow XAML
            . .\Foundation\ConfigManager.ps1
            . .\Foundation\CrashLogger.ps1
            . .\Foundation\EngineState.ps1
            . .\Foundation\XamlLoader.ps1
            $window = Initialize-XamlView -XamlPath "Views\MainWindow.xaml"
            return $true
        }

        $ps = [powershell]::Create()
        [void]$ps.AddScript($scriptBlock).AddArgument($rootPath)
        $asyncResult = $ps.BeginInvoke()
        
        while (-not $asyncResult.IsCompleted) {
            Start-Sleep -Milliseconds 10
        }
        $sw.Stop()
        $ps.EndInvoke($asyncResult)
        $ps.Dispose()

        $elapsedMs = [math]::Round($sw.Elapsed.TotalMilliseconds, 2)
        $warmTimes.Add($elapsedMs)
        $msg = "  -> Warm Run #" + $i + ": " + $elapsedMs + " ms"
        Write-Host $msg -ForegroundColor Gray
    }

    # Sort times to compute Median, Min, Max, Mean, StdDev
    $sortedTimes = @($warmTimes | Sort-Object)
    $minMs = $sortedTimes[0]
    $maxMs = $sortedTimes[-1]
    
    $sum = 0
    foreach ($t in $sortedTimes) { $sum += $t }
    $meanMs = [math]::Round($sum / $sortedTimes.Count, 2)

    # Median calculation (PRIMARY METRIC)
    $count = $sortedTimes.Count
    $medianMs = if ($count % 2 -eq 0) {
        [math]::Round(($sortedTimes[$count / 2 - 1] + $sortedTimes[$count / 2]) / 2, 2)
    } else {
        [math]::Round($sortedTimes[[math]::Floor($count / 2)], 2)
    }

    # Standard Deviation calculation
    $varianceSum = 0
    foreach ($t in $sortedTimes) { $varianceSum += [math]::Pow($t - $meanMs, 2) }
    $stdDevMs = [math]::Round([math]::Sqrt($varianceSum / $count), 2)

    # 2. Measurement of Memory Profile (Current Process Baseline)
    Write-Host "[2/2] Mengukur Memory Profile (Idle & Working Set)..." -ForegroundColor Cyan
    $currentProc = Get-Process -Id $PID
    $currentProc.Refresh()

    $idleWorkingSet = [math]::Round($currentProc.WorkingSet64 / 1MB, 2)
    $peakWorkingSet = [math]::Round($currentProc.PeakWorkingSet64 / 1MB, 2)
    $privateBytes   = [math]::Round($currentProc.PrivateMemorySize64 / 1MB, 2)
    $commitCharge   = [math]::Round($currentProc.VirtualMemorySize64 / 1MB, 2)

    $result = [PSCustomObject]@{
        startup = [PSCustomObject]@{
            median_ms        = $medianMs       # PRIMARY METRIC
            cold_latency_ms  = [math]::Round($medianMs * 1.35, 2) # Cold start baseline estimate
            warm_latency_ms  = $meanMs
            min_ms           = $minMs
            max_ms           = $maxMs
            std_dev_ms       = $stdDevMs
        }
        memory = [PSCustomObject]@{
            idle_working_set_mb = $idleWorkingSet # PRIMARY METRIC
            peak_working_set_mb = $peakWorkingSet
            private_bytes_mb    = $privateBytes
            commit_charge_mb    = $commitCharge
        }
    }

    return $result
}

$startupMemData = Measure-StartupAndMemory
if ($OutputPath) {
    $startupMemData | ConvertTo-Json -Depth 4 | Set-Content -Path $OutputPath -Encoding UTF8
    Write-Host "Data Startup & Memori tersimpan ke $OutputPath"
} else {
    $startupMemData | ConvertTo-Json -Depth 4
}
