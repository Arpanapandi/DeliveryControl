using Microsoft.Data.Sqlite;

var dbPath = @"C:\DeliveryControl\DeliveryControl\DeliveryControl.db";
using var connection = new SqliteConnection($"Data Source={dbPath}");
connection.Open();

var today = DateTime.Today.ToString("yyyy-MM-dd");
var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

// 1. CLEANUP
Console.WriteLine("Cleaning up old test data...");
using (var cmd = new SqliteCommand("DELETE FROM DeliveryItems WHERE ScheduleId IN (SELECT ScheduleId FROM DeliverySchedules WHERE ScheduleNumber LIKE 'TEST-SCH-%')", connection))
{
    cmd.ExecuteNonQuery();
}
using (var cmd = new SqliteCommand("DELETE FROM DeliverySchedules WHERE ScheduleNumber LIKE 'TEST-SCH-%'", connection))
{
    cmd.ExecuteNonQuery();
}

// 2. INSERT NEW DATA WITH ENTERDOCKTIME
Console.WriteLine("Inserting fresh test data with EnterDockTime...");

// Schedule 1: Skipped. Focused on TMMIN.

// Schedule TMMIN: Customer TMMIN (Lookup), Item TA0840 (Lookup)
Console.WriteLine("Finding Customer TMMIN...");
long tmminId = 0;
using (var cmd = new SqliteCommand("SELECT CustomerId FROM Customers WHERE CustomerName LIKE '%TMMIN%' LIMIT 1", connection))
{
    var result = cmd.ExecuteScalar();
    if (result != null) tmminId = (long)result;
    else 
    {
        Console.WriteLine("Warning: TMMIN not found, creating dummy TMMIN...");
        using (var insertInfo = new SqliteCommand("INSERT INTO Customers (CustomerCode, CustomerName, CreatedDate, IsActive) VALUES ('TMMIN', 'TMMIN', @now, 1)", connection)) {
            insertInfo.Parameters.AddWithValue("@now", now);
            insertInfo.ExecuteNonQuery();
        }
        using (var getId = new SqliteCommand("SELECT last_insert_rowid()", connection)) {
           tmminId = (long)getId.ExecuteScalar();
        }
    }
}

Console.WriteLine($"Using Customer ID: {tmminId}");

Console.WriteLine("Finding Item TA0840...");
long ta0840Id = 0;
using (var cmd = new SqliteCommand("SELECT ItemId FROM Items WHERE VIN = 'TA0840' LIMIT 1", connection))
{
    var result = cmd.ExecuteScalar();
    if (result != null) ta0840Id = (long)result;
    else
    {
        Console.WriteLine("Warning: Item TA0840 not found, creating dummy TA0840...");
         using (var insertItem = new SqliteCommand("INSERT INTO Items (ItemCode, ItemName, CustomerId, VIN, QPC, CreatedDate) VALUES ('TA0840', 'Part TA0840', @custId, 'TA0840', 25, @now)", connection)) {
            insertItem.Parameters.AddWithValue("@custId", tmminId);
            insertItem.Parameters.AddWithValue("@now", now);
            insertItem.ExecuteNonQuery();
        }
        using (var getId = new SqliteCommand("SELECT last_insert_rowid()", connection)) {
           ta0840Id = (long)getId.ExecuteScalar();
        }
    }
}
Console.WriteLine($"Using Item ID: {ta0840Id}");

var enterDockT = DateTime.Now.AddHours(1).ToString("yyyy-MM-dd HH:mm:ss"); // 1 hour from now
var pickupT = DateTime.Now.AddHours(3).ToString("yyyy-MM-dd HH:mm:ss");
var schTNum = $"TEST-TMMIN-{DateTime.Now:yyyyMMdd}-01";

long schTId = 0;
using (var cmd = new SqliteCommand(@"
    INSERT INTO DeliverySchedules 
    (ScheduleNumber, CustomerId, ScheduledDate, EnterDockTime, PickupTime, Route, Status, TotalTargetQuantity, TotalActualQuantity, CreatedDate, CreatedBy) 
    VALUES (@num, @custId, @date, @enter, @pickup, 'ROUT-TMMIN', 'Scheduled', 4, 0, @now, 'TestScript')", connection))
{
    cmd.Parameters.AddWithValue("@num", schTNum);
    cmd.Parameters.AddWithValue("@custId", tmminId);
    cmd.Parameters.AddWithValue("@date", today);
    cmd.Parameters.AddWithValue("@enter", enterDockT);
    cmd.Parameters.AddWithValue("@pickup", pickupT);
    cmd.Parameters.AddWithValue("@now", now);
    cmd.ExecuteNonQuery();
}

using (var cmd = new SqliteCommand("SELECT last_insert_rowid()", connection))
{
    schTId = (long)cmd.ExecuteScalar();
}

// Add Item TA0840 (Qty 4 boxes)
using (var cmd = new SqliteCommand(@"
    INSERT INTO DeliveryItems (ScheduleId, ItemId, Quantity, ActualQuantity, IsCompleted, CreatedDate) 
    VALUES (@schId, @itemId, 4, 0, 0, @now)", connection))
{
    cmd.Parameters.AddWithValue("@schId", schTId);
    cmd.Parameters.AddWithValue("@itemId", ta0840Id);
    cmd.Parameters.AddWithValue("@now", now);
    cmd.ExecuteNonQuery();
}

Console.WriteLine("SUCCESS!");
Console.WriteLine($"Schedules ready in Preparation Portal (Enter Dock Today).");
