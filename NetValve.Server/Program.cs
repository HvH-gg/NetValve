using NetValve.Server.Hosting;
using Serilog;

try
{
    var app = NetValveServerApplication.Create(args);
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Information()
        .WriteTo.Console()
        .CreateLogger();

    Log.Fatal(ex, "NetValve.Server failed to start");
}
finally
{
    Log.CloseAndFlush();
}
