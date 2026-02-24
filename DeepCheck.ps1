
$connectionString = "Server=10.14.149.34;Database=PPIC_DeliveryControl;User Id=usrvelasto;Password=H1s@na2025!!;TrustServerCertificate=true;MultipleActiveResultSets=true"
$conn = New-Object System.Data.SqlClient.SqlConnection $connectionString
$conn.Open()
$cmd = $conn.CreateCommand()

Write-Output "--- ItemMappings (Table dump) ---"
$cmd.CommandText = "SELECT * FROM ItemMappings"
$reader = $cmd.ExecuteReader()
while ($reader.Read()) {
    Write-Output ($reader["MappingId"].ToString() + " | " + $reader["Customer"] + " | " + $reader["CustomerPartNumber"] + " | " + $reader["VIN"])
}
$reader.Close()

Write-Output "`n--- Items matching 77249KK01000 (Exact or Partial) ---"
$cmd.CommandText = "SELECT ItemId, ItemCode, VIN, CustomerPartNumber, QtyLot FROM Items WHERE VIN LIKE '%77249%' OR CustomerPartNumber LIKE '%77249%' OR ItemCode LIKE '%77249%'"
$reader = $cmd.ExecuteReader()
while ($reader.Read()) {
    Write-Output ("ID: " + $reader["ItemId"] + " | Code: " + $reader["ItemCode"] + " | VIN: " + $reader["VIN"] + " | Part: " + $reader["CustomerPartNumber"] + " | QPC: " + $reader["QtyLot"])
}
$reader.Close()

$conn.Close()
