# docs/benchmarks/scripts/Measure-SystemMetrics.ps1
# Environmental Hardware & OS Metrics Collector for Nexus Baseline v1.7
param(
    [string]$OutputPath = ""
)

function Get-NexusEnvironmentBaseline {
    try {
        $os = Get-CimInstance Win32_OperatingSystem -ErrorAction Stop
        $cpu = Get-CimInstance Win32_Processor -ErrorAction Stop | Select-Object -First 1
        $ram = Get-CimInstance Win32_PhysicalMemory -ErrorAction SilentlyContinue | Measure-Object -Property Capacity -Sum
        $gpu = Get-CimInstance Win32_VideoController -ErrorAction SilentlyContinue | Select-Object -First 1
        $disk = Get-CimInstance Win32_DiskDrive -ErrorAction SilentlyContinue | Select-Object -First 1
        
        $ramGb = if ($ram -and $ram.Sum) { [math]::Round($ram.Sum / 1GB, 2) } else { [math]::Round(($os.TotalVisibleMemorySize * 1KB) / 1GB, 2) }
        $gpuName = if ($gpu -and $gpu.Name) { $gpu.Name } else { "Generic Display Adapter" }
        $gpuVram = if ($gpu -and $gpu.AdapterRAM) { [math]::Round($gpu.AdapterRAM / 1MB, 0) } else { 0 }
        $diskName = if ($disk -and $disk.Model) { "$($disk.Model) ($($disk.MediaType))" } else { "System Drive" }

        Add-Type -AssemblyName System.Windows.Forms -ErrorAction SilentlyContinue
        $screen = [System.Windows.Forms.Screen]::PrimaryScreen
        $dispRes = if ($screen) { "$($screen.Bounds.Width)x$($screen.Bounds.Height)" } else { "1920x1080" }

        $envData = [PSCustomObject]@{
            os_build            = "$($os.Caption) (Build $($os.BuildNumber))"
            powershell_version  = $PSVersionTable.PSVersion.ToString()
            dotnet_runtime      = [System.Runtime.InteropServices.RuntimeInformation]::FrameworkDescription
            cpu_model           = "$($cpu.Name.Trim()) ($($cpu.NumberOfCores) Cores, $($cpu.NumberOfLogicalProcessors) Threads)"
            ram_total_gb        = $ramGb
            storage_type        = $diskName
            gpu_model           = "$gpuName ($gpuVram MB VRAM)"
            display_resolution  = $dispRes
        }

        return $envData
    }
    catch {
        Write-Error "Gagal mengumpulkan data hardware: $_"
        return $null
    }
}

$metrics = Get-NexusEnvironmentBaseline
if ($OutputPath) {
    $metrics | ConvertTo-Json -Depth 4 | Set-Content -Path $OutputPath -Encoding UTF8
    Write-Host "Data lingkungan hardware tersimpan ke $OutputPath"
} else {
    $metrics | ConvertTo-Json -Depth 4
}
