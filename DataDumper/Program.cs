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

// Schedule 1: DOCK 43 (Id=1)
var enterDock1 = DateTime.Today.AddHours(8).ToString("yyyy-MM-dd HH:mm:ss");
var pickup1 = DateTime.Today.AddHours(10).ToString("yyyy-MM-dd HH:mm:ss");
var sch1Num = $"TEST-SCH-{DateTime.Now:yyyyMMdd}-001";

using (var cmd = new SqliteCommand(@"
    INSERT INTO DeliverySchedules 
    (ScheduleNumber, CustomerId, ScheduledDate, EnterDockTime, PickupTime, Route, Status, TotalTargetQuantity, TotalActualQuantity, CreatedDate, CreatedBy) 
    VALUES (@num, @custId, @date, @enter, @pickup, 'ROUT-01', 'Scheduled', 2, 0, @now, 'TestScript')", connection))
{
    cmd.Parameters.AddWithValue("@num", sch1Num);
    cmd.Parameters.AddWithValue("@custId", 1);
    cmd.Parameters.AddWithValue("@date", today);
    cmd.Parameters.AddWithValue("@enter", enterDock1);
    cmd.Parameters.AddWithValue("@pickup", pickup1);
    cmd.Parameters.AddWithValue("@now", now);
    cmd.ExecuteNonQuery();
}

long sch1Id = 0;
using (var cmd = new SqliteCommand("SELECT last_insert_rowid()", connection))
{
    sch1Id = (long)cmd.ExecuteScalar();
}

// Items for Sch1
using (var cmd = new SqliteCommand(@"
    INSERT INTO DeliveryItems (ScheduleId, ItemId, Quantity, ActualQuantity, IsCompleted, CreatedDate) 
    VALUES (@schId, @itemId, 5, 0, 0, @now)", connection))
{
    cmd.Parameters.AddWithValue("@schId", sch1Id);
    cmd.Parameters.AddWithValue("@itemId", 13890);
    cmd.Parameters.AddWithValue("@now", now);
    cmd.ExecuteNonQuery();
}

using (var cmd = new SqliteCommand(@"
    INSERT INTO DeliveryItems (ScheduleId, ItemId, Quantity, ActualQuantity, IsCompleted, CreatedDate) 
    VALUES (@schId, @itemId, 3, 0, 0, @now)", connection))
{
    cmd.Parameters.AddWithValue("@schId", sch1Id);
    cmd.Parameters.AddWithValue("@itemId", 13891);
    cmd.Parameters.AddWithValue("@now", now);
    cmd.ExecuteNonQuery();
}

// Schedule 2: SUZUKI (Id=6)
var enterDock2 = DateTime.Today.AddHours(9).ToString("yyyy-MM-dd HH:mm:ss");
var pickup2 = DateTime.Today.AddHours(11).ToString("yyyy-MM-dd HH:mm:ss");
var sch2Num = $"TEST-SCH-{DateTime.Now:yyyyMMdd}-002";

using (var cmd = new SqliteCommand(@"
    INSERT INTO DeliverySchedules 
    (ScheduleNumber, CustomerId, ScheduledDate, EnterDockTime, PickupTime, Route, Status, TotalTargetQuantity, TotalActualQuantity, CreatedDate, CreatedBy) 
    VALUES (@num, @custId, @date, @enter, @pickup, 'ROUT-02', 'Scheduled', 1, 0, @now, 'TestScript')", connection))
{
    cmd.Parameters.AddWithValue("@num", sch2Num);
    cmd.Parameters.AddWithValue("@custId", 6);
    cmd.Parameters.AddWithValue("@date", today);
    cmd.Parameters.AddWithValue("@enter", enterDock2);
    cmd.Parameters.AddWithValue("@pickup", pickup2);
    cmd.Parameters.AddWithValue("@now", now);
    cmd.ExecuteNonQuery();
}

long sch2Id = 0;
using (var cmd = new SqliteCommand("SELECT last_insert_rowid()", connection))
{
    sch2Id = (long)cmd.ExecuteScalar();
}

using (var cmd = new SqliteCommand(@"
    INSERT INTO DeliveryItems (ScheduleId, ItemId, Quantity, ActualQuantity, IsCompleted, CreatedDate) 
    VALUES (@schId, @itemId, 10, 0, 0, @now)", connection))
{
    cmd.Parameters.AddWithValue("@schId", sch2Id);
    cmd.Parameters.AddWithValue("@itemId", 13892);
    cmd.Parameters.AddWithValue("@now", now);
    cmd.ExecuteNonQuery();
}

Console.WriteLine("SUCCESS!");
Console.WriteLine($"Schedules ready in Preparation Portal (Enter Dock Today).");
