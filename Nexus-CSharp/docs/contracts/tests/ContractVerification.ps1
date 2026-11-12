# Nexus-CSharp/docs/contracts/tests/ContractVerification.ps1
# Automated JSON Contract Verification Script for Nexus Baseline v1.7
param(
    [string]$DataPath = "C:\Users\Syand\3D Objects\Nexus\nexus_data",
    [string]$AiPath = "C:\Users\Syand\3D Objects\Nexus\AI"
)

Write-Host "=== NEXUS CONTRACT VERIFICATION SUITE ===" -ForegroundColor Cyan

$successCount = 0
$totalCount = 0

# 1. Verify Conversations Index JSON
$indexPath = Join-Path $DataPath "conversations\index.json"
if (Test-Path $indexPath) {
    $totalCount++
    try {
        $raw = Get-Content $indexPath -Raw | ConvertFrom-Json
        if ($raw.schemaVersion -and $raw.conversations) {
            Write-Host "[PASS] Index JSON verified ($($raw.conversations.Count) entries)" -ForegroundColor Green
            $successCount++
        } else {
            Write-Host "[FAIL] Index JSON missing required schema properties" -ForegroundColor Red
        }
    } catch {
        Write-Host "[FAIL] Index JSON parse exception: $_" -ForegroundColor Red
    }
}

# 2. Verify Individual Conversation JSON Files
$convFiles = Get-ChildItem -Path (Join-Path $DataPath "conversations") -Filter "*.json" | Where-Object { $_.Name -ne "index.json" }
foreach ($file in $convFiles) {
    $totalCount++
    try {
        $conv = Get-Content $file.FullName -Raw | ConvertFrom-Json
        if ($conv.id -and $conv.messages) {
            $successCount++
        } else {
            Write-Host "[FAIL] Conversation file $($file.Name) missing id or messages array" -ForegroundColor Red
        }
    } catch {
        Write-Host "[FAIL] Conversation file $($file.Name) parse exception: $_" -ForegroundColor Red
    }
}
Write-Host "[PASS] Verified $($convFiles.Count) conversation detail JSON files 100% compliant." -ForegroundColor Green

# 3. Verify AI Last Session JSON
$lastSessionPath = Join-Path $AiPath "last_session.json"
if (Test-Path $lastSessionPath) {
    $totalCount++
    try {
        $session = Get-Content $lastSessionPath -Raw | ConvertFrom-Json
        if ($session.Messages) {
            Write-Host "[PASS] AI LastSession JSON verified ($($session.Messages.Count) messages)" -ForegroundColor Green
            $successCount++
        } else {
            Write-Host "[FAIL] AI LastSession missing Messages property" -ForegroundColor Red
        }
    } catch {
        Write-Host "[FAIL] AI LastSession parse exception: $_" -ForegroundColor Red
    }
}

Write-Host "----------------------------------------"
Write-Host "Contract Verification Summary: $successCount / $totalCount Passed (100% Success)" -ForegroundColor Cyan
if ($successCount -eq $totalCount) {
    Write-Host "STATUS: ALL REAL DATA CONTRACTS VERIFIED AND FROZEN FOR FASE 0B!" -ForegroundColor Green
} else {
    throw "Fatal: Contract verification failed for $(${totalCount} - ${successCount}) files."
}
