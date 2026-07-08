using System;
using Microsoft.Data.Sqlite;
using System.IO;

class Program
{
    static int Main()
    {
        try
        {
            var dbPath = Path.GetFullPath(Path.Combine("..","SmartOps.Web","smartops.db"));
            Console.WriteLine($"Using DB: {dbPath}");
            if (!File.Exists(dbPath)) { Console.WriteLine("DB file not found."); return 2; }
            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, CustomerId, Amount, Currency, Status, GatewayReference, Provider, ErrorMessage, OccurredAt FROM Transactions ORDER BY Id;";
            using var reader = cmd.ExecuteReader();
            Console.WriteLine("Id\tCustomerId\tAmount\tCurrency\tStatus\tGatewayRef\tProvider\tErrorMessage\tOccurredAt");
            while (reader.Read())
            {
                var id = reader.GetInt32(0);
                var cust = reader.IsDBNull(1) ? "NULL" : reader.GetInt32(1).ToString();
                var amt = reader.IsDBNull(2) ? "NULL" : reader.GetDecimal(2).ToString();
                var cur = reader.IsDBNull(3) ? "NULL" : reader.GetString(3);
                var status = reader.IsDBNull(4) ? "NULL" : reader.GetString(4);
                var gref = reader.IsDBNull(5) ? "NULL" : reader.GetString(5);
                var prov = reader.IsDBNull(6) ? "NULL" : reader.GetString(6);
                var err = reader.IsDBNull(7) ? "NULL" : reader.GetString(7);
                var occ = reader.IsDBNull(8) ? "NULL" : reader.GetDateTime(8).ToString("o");
                Console.WriteLine($"{id}\t{cust}\t{amt}\t{cur}\t{status}\t{gref}\t{prov}\t{err}\t{occ}");
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("ERROR: " + ex);
            return 1;
        }
    }
}
