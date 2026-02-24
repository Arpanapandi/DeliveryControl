
$connectionString = "Data Source=DeliveryControl.db"
$conn = New-Object System.Data.SQLite.SQLiteConnection $connectionString
$conn.Open()
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT VIN, ItemCode, COUNT(*) as Count FROM Items GROUP BY VIN, ItemCode HAVING COUNT(*) > 1"
$reader = $cmd.ExecuteReader()
while ($reader.Read()) {
    Write-Output ("DUPLICATE: VIN=" + $reader["VIN"] + " || Code=" + $reader["ItemCode"] + " || Count=" + $reader["Count"])
}
$reader.Close()
$conn.Close()
