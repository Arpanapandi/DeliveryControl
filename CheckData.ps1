
$connString = "Server=10.14.149.34;Database=ppic_DeliveryControl;User Id=usrvelasto;Password=H1s@na2025!!;TrustServerCertificate=true;MultipleActiveResultSets=true"
$conn = New-Object System.Data.SqlClient.SqlConnection $connString
$conn.Open()

$cmd = $conn.CreateCommand()

# Count Today's Schedules
$cmd.CommandText = "SELECT COUNT(*) FROM DeliverySchedules WHERE CAST(ScheduledDate AS DATE) = '2026-02-25'"
$count = $cmd.ExecuteScalar()
Write-Host "Count: $count"

# Check Admin Role
$cmd.CommandText = "SELECT Username, Role FROM Users WHERE FullName LIKE '%Administrator%'"
$reader = $cmd.ExecuteReader()
while ($reader.Read()) {
    $u = $reader["Username"]
    $r = $reader["Role"]
    Write-Host "User: $u, Role: $r"
}
$reader.Close()

$conn.Close()
