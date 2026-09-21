// See https://aka.ms/new-console-template for more information
using System;
using Microsoft.Data.Sqlite;

var cwd = Directory.GetCurrentDirectory();
Console.WriteLine($"CWD: {cwd}");
var dbPath = Path.Combine("C:", "Users", "rober", "source", "repos", "smart-ops-agent-saas", "SmartOps.Web", "smartops.db");
Console.WriteLine($"Using DB path: {dbPath}");
if (!File.Exists(dbPath))
{
    Console.WriteLine($"Database not found at {dbPath}");
    return;
}

var connString = $"Data Source={dbPath}";
using var conn = new SqliteConnection(connString);
conn.Open();
using var tx = conn.BeginTransaction();

var createNotifications = @"CREATE TABLE IF NOT EXISTS Notifications (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    TransactionId INTEGER,
    Title TEXT,
    Message TEXT NOT NULL,
    Type TEXT NOT NULL,
    IsRead INTEGER NOT NULL,
    CreatedAt TEXT NOT NULL
);";

using (var cmd = conn.CreateCommand())
{
    cmd.CommandText = createNotifications;
    cmd.ExecuteNonQuery();
}

// Ensure migrations history table exists
using (var cmd = conn.CreateCommand())
{
    cmd.CommandText = @"CREATE TABLE IF NOT EXISTS __EFMigrationsHistory (
        MigrationId TEXT NOT NULL PRIMARY KEY,
        ProductVersion TEXT NOT NULL
    );";
    cmd.ExecuteNonQuery();
}

// Insert our migration id if not present
var migrationId = "20260814142457_AddNotifications";
using (var cmd = conn.CreateCommand())
{
    cmd.CommandText = "INSERT OR IGNORE INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ($id, $ver);";
    cmd.Parameters.AddWithValue("$id", migrationId);
    cmd.Parameters.AddWithValue("$ver", "9.0.0");
    cmd.ExecuteNonQuery();
}

tx.Commit();
Console.WriteLine("Notifications table ensured and migration recorded.");
