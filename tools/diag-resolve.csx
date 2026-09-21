#r "nuget: Microsoft.Extensions.DependencyInjection, 8.0.0"
#r "nuget: Microsoft.Extensions.Hosting, 8.0.0"
#r "nuget: Microsoft.Extensions.Logging.Console, 8.0.0"

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

Console.WriteLine("Starting diagnostic resolver script...");

var builder = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        // Mirror registrations minimally to attempt resolving the notifier and publisher
        // Note: cannot easily mirror full app here; this is a lightweight probe.
    })
    .ConfigureLogging(l => l.AddConsole());

using var host = builder.Build();

Console.WriteLine("Host built. No registrations added.");

// Attempt to resolve the types by loading assemblies
try
{
    var webAsm = System.Reflection.Assembly.LoadFrom("C:\\Users\\rober\\source\\repos\\smart-ops-agent-saas\\SmartOps.Web\\bin\\Debug\\net9.0\\SmartOps.Web.dll");
    var infraAsm = System.Reflection.Assembly.LoadFrom("C:\\Users\\rober\\source\\repos\\smart-ops-agent-saas\\SmartOps.Infrastructure\\bin\\Debug\\net9.0\\SmartOps.Infrastructure.dll");
    var coreAsm = System.Reflection.Assembly.LoadFrom("C:\\Users\\rober\\source\\repos\\smart-ops-agent-saas\\SmartOps.Core\\bin\\Debug\\net9.0\\SmartOps.Core.dll");

    Console.WriteLine($"Loaded assemblies: {webAsm.FullName}, {infraAsm.FullName}, {coreAsm.FullName}");

    var publisherType = coreAsm.GetType("SmartOps.Core.Interfaces.ITransactionPublisher");
    var notifierType = webAsm.GetType("SmartOps.Web.Services.TransactionNotifier");

    Console.WriteLine($"Types: publisher={publisherType}, notifier={notifierType}");
}
catch (Exception ex)
{
    Console.WriteLine("Assembly load failed: " + ex);
}
