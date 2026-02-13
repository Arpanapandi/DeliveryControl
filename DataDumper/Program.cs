using Microsoft.Data.Sqlite;

var dbPath = @"C:\DeliveryControl\DeliveryControl\DeliveryControl.db";
using var connection = new SqliteConnection($"Data Source={dbPath}");
connection.Open();

using (var cmd = new SqliteCommand("SELECT COUNT(*) FROM Items", connection))
{
    var count = cmd.ExecuteScalar();
    Console.WriteLine($"Total Items in DB: {count}");
}

using (var cmd = new SqliteCommand("SELECT COUNT(*) FROM Items WHERE IsActive = 1", connection))
{
    var count = cmd.ExecuteScalar();
    Console.WriteLine($"Total Active Items in DB: {count}");
}
