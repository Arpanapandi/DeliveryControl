# SeedCriticalStockTest.ps1
# ──────────────────────────────────────────────────────────────────
# Membuat data snapshot palsu (test) untuk N hari ke belakang,
# berdasarkan snapshot hari ini sebagai template.
# Jalankan saat dotnet run SEDANG TIDAK BERJALAN.
# ──────────────────────────────────────────────────────────────────

$DbPath    = "$PSScriptRoot\DeliveryControl.db"
$DaysBack  = 14   # Berapa hari ke belakang yang akan di-seed

# --- Cek apakah sqlite3 tersedia ---
$sqlite3 = Get-Command sqlite3 -ErrorAction SilentlyContinue
if (-not $sqlite3) {
    Write-Host "sqlite3 tidak ditemukan. Install via: winget install --id SQLite.SQLite" -ForegroundColor Red
    Write-Host "Atau download dari: https://www.sqlite.org/download.html -> sqlite-tools-win-x86.zip" -ForegroundColor Yellow
    exit 1
}

# --- Ambil snapshot hari ini sebagai template ---
$today    = (Get-Date).ToString("yyyy-MM-dd")
$template = & sqlite3 $DbPath "SELECT ItemCode, ItemName, Plant, StockQty, DaysCoverage, StockLevel, RackMin FROM StockSnapshots WHERE date(SnapshotDate) = '$today' LIMIT 1;" 2>&1

if (-not $template) {
    Write-Host "Tidak ada snapshot hari ini! Klik 'Snapshot Manual' dulu di aplikasi, lalu jalankan script ini." -ForegroundColor Red
    exit 1
}

# Ambil semua baris template (ALL items dari snapshot hari ini)
$rows = @(& sqlite3 -separator "|" $DbPath `
    "SELECT Id, ItemCode, ItemName, Plant, StockQty, DaysCoverage, StockLevel FROM StockSnapshots WHERE date(SnapshotDate) = '$today' AND IsManual = 0 OR date(SnapshotDate) = '$today';")

if ($rows.Count -eq 0) {
    Write-Host "Tidak ada data snapshot hari ini di database." -ForegroundColor Red
    exit 1
}

Write-Host "Template: $($rows.Count) item dari hari ini ($today)" -ForegroundColor Cyan

# Hapus snapshot test lama (sebelum hari ini, IsManual=1 buatan script)
& sqlite3 $DbPath "DELETE FROM StockSnapshots WHERE date(SnapshotDate) < '$today' AND IsManual = 1;"
Write-Host "Snapshot test lama dihapus." -ForegroundColor Yellow

# Fungsi: hitung StockLevel dari DaysCoverage
function Get-StockLevel([double]$dc) {
    if ($dc -lt 1.0) { return "<1D" }
    elseif ($dc -lt 1.5) { return "<1.5D" }
    elseif ($dc -lt 2.0) { return "1.5-2D" }
    elseif ($dc -lt 3.0) { return "2-3D" }
    else { return ">3D" }
}

$total = 0
$rng = [System.Random]::new()

for ($d = $DaysBack; $d -ge 1; $d--) {
    $date    = (Get-Date).AddDays(-$d).ToString("yyyy-MM-dd")
    $snapTS  = "$date 08:00:00"

    $inserts = @()
    foreach ($row in $rows) {
        $parts      = $row -split "\|"
        if ($parts.Count -lt 7) { continue }
        $itemCode   = $parts[1].Trim()
        $itemName   = $parts[2].Trim().Replace("'","''")
        $plant      = $parts[3].Trim()
        $baseStock  = [double]$parts[4]
        $baseDC     = [double]$parts[5]
        $level      = $parts[6].Trim()

        # Variasi realistis: ±30% fluktuasi per hari
        $factor     = 0.7 + ($rng.NextDouble() * 0.6)   # 0.7 .. 1.3
        $newStock   = [Math]::Max(0, [Math]::Round($baseStock * $factor))
        $newDC      = if ($baseDC -gt 0) { [Math]::Round($baseDC * $factor, 4) } else { 0 }
        $newLevel   = Get-StockLevel $newDC

        $inserts += "('$date','08:00:00','$itemCode','$itemName','$plant',$newStock,$newDC,'$newLevel',1,'$snapTS')"
    }

    if ($inserts.Count -gt 0) {
        $sql = "INSERT INTO StockSnapshots (SnapshotDate, SnapshotTime, ItemCode, ItemName, Plant, StockQty, DaysCoverage, StockLevel, IsManual, CreatedAt) VALUES " + ($inserts -join ",") + ";"
        & sqlite3 $DbPath $sql
        $total += $inserts.Count
        Write-Host "  [$date] +$($inserts.Count) snapshots" -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "✅ Selesai! Total $total baris test snapshot dimasukkan ($DaysBack hari)." -ForegroundColor Green
Write-Host "Buka Critical Stock → grafik trend harus tampil sekarang." -ForegroundColor Cyan
