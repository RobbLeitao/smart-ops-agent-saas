using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;

class Program {
    static async Task Main() {
        var url = "http://127.0.0.1:5000/hubs/transactions";
        Console.WriteLine($"Connecting to {url}");
        var connection = new HubConnectionBuilder()
            .WithUrl(url)
            .Build();

        connection.On<string>("ReceiveTransaction", payload => {
            Console.WriteLine("RECEIVED (string): " + payload);
        });
        connection.On<object>("ReceiveTransaction", payload => {
            Console.WriteLine("RECEIVED (object): " + System.Text.Json.JsonSerializer.Serialize(payload));
        });

        connection.Closed += async (ex) => {
            Console.WriteLine("Connection closed: " + ex?.Message);
            await Task.Delay(1000);
            try { await connection.StartAsync(); } catch { }
        };

        await connection.StartAsync();
        Console.WriteLine("Connected. State: " + connection.State);

        Console.WriteLine("Waiting for messages (Ctrl+C to exit)...");
        await Task.Delay(-1);
    }
}

