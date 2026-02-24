
$connectionString = "Server=10.14.149.34;Database=PPIC_DeliveryControl;User Id=usrvelasto;Password=H1s@na2025!!;TrustServerCertificate=true;MultipleActiveResultSets=true"
$conn = New-Object System.Data.SqlClient.SqlConnection $connectionString
$conn.Open()
$cmd = $conn.CreateCommand()

# 1. DELETE REDUNDANT ITEMS (Created Today, QPC 1, VIN contains KK or BZ etc)
# Adjust the filter to be safe. We'll target items created today with QPC 1 that match the Part Number format.
$today = Get-Date -Format "yyyy-MM-dd"
$cmd.CommandText = "DELETE FROM Items WHERE QtyLot = 1 AND CreatedDate >= '$today' AND (VIN LIKE '77249%' OR VIN LIKE '63249%' OR VIN LIKE '12261%')"
$rows = $cmd.ExecuteNonQuery()
Write-Output "Deleted $rows redundant items."

# 2. DELETE RECENT SCHEDULES (to allow retry)
$cmd.CommandText = "DELETE FROM DeliveryItems WHERE ScheduleId IN (SELECT ScheduleId FROM DeliverySchedules WHERE CreatedDate >= '$today' AND CreatedBy = 'ImportExcel')"
$cmd.ExecuteNonQuery()
$cmd.CommandText = "DELETE FROM DeliverySchedules WHERE CreatedDate >= '$today' AND CreatedBy = 'ImportExcel'"
$rows = $cmd.ExecuteNonQuery()
Write-Output "Deleted $rows recent delivery schedules."

$conn.Close()
