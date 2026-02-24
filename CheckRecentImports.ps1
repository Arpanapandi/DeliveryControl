
$connectionString = "Server=10.14.149.34;Database=PPIC_DeliveryControl;User Id=usrvelasto;Password=H1s@na2025!!;TrustServerCertificate=true;MultipleActiveResultSets=true"
$conn = New-Object System.Data.SqlClient.SqlConnection $connectionString
$conn.Open()
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT TOP 5 ds.ScheduleNumber, i.ItemCode, i.VIN, i.CustomerPartNumber, di.Quantity, i.QtyLot 
                    FROM DeliveryItems di 
                    JOIN DeliverySchedules ds ON di.ScheduleId = ds.ScheduleId 
                    JOIN Items i ON di.ItemId = i.ItemId 
                    ORDER BY ds.CreatedDate DESC"
$reader = $cmd.ExecuteReader()
while ($reader.Read()) {
    Write-Output ("SCH: " + $reader["ScheduleNumber"] + " || VIN: " + $reader["VIN"] + " || Part: " + $reader["CustomerPartNumber"] + " || QPC: " + $reader["QtyLot"])
}
$reader.Close()
$conn.Close()
