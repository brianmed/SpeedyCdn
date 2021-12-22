using System.Net.NetworkInformation;
using System.Net.Sockets;

using Serilog.Context;
using Serilog.Events;
using Serilog.Extensions.Hosting;

RegisterGlobalExceptionHandling();

ConfigCtx.ParseOptions(args);
DirectoriesCtx.Provision();
LoggingCtx.Initialize();

Console.WriteLine($"Initializing App Log: Console and {Path.Combine(ConfigCtx.Options.LogDirectory, $"{nameof(SpeedyCdn)}-App.txt")}");
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning) 
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .MinimumLevel.Override("System", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("WebAppPrefix", nameof(SpeedyCdn))
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {WebAppPrefix} {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        Path.Combine(ConfigCtx.Options.LogDirectory, $"{nameof(SpeedyCdn)}-App.txt"),
        fileSizeLimitBytes: 1024 * 1024 * 256,
        rollOnFileSizeLimit: true,
        retainedFileCountLimit: 4,
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {WebAppPrefix} {Message:lj}{NewLine}{Exception}")
    .CreateBootstrapLogger();

Log.Information($"{nameof(SpeedyCdn)} Copyright (C) 2021 Sparks and Magic LLC");
Log.Information($"{nameof(SpeedyCdn)} Built On [{WhenBuilt.ItWas.ToString("s")}]");
  
Log.Information($"AppDirectory: {ConfigCtx.Options.AppDirectory}");
Log.Information($"LogDirectory: {ConfigCtx.Options.LogDirectory}");
if (ConfigCtx.HasEdgeServer) {
    Log.Information($"Edge CacheDirectory: {ConfigCtx.Options.EdgeCacheDirectory}");
    Log.Information($"Edge CacheImagesDirectory: {ConfigCtx.Options.EdgeCacheImagesDirectory}");
    Log.Information($"Edge CacheStaticDirectory: {ConfigCtx.Options.EdgeCacheStaticDirectory}");
    Log.Information($"Edge OriginUrl: {ConfigCtx.Options.EdgeOriginUrl}");
    // 
    if (ConfigCtx.Options.EdgeOriginApiKey is not null) {
        Log.Information($"Edge Origin Api Key was Specified");
    } else {
        Log.Information($"Edge Origin Api Key is not Configured: Pass in --edgeOriginApiKey");
    }
}
if (ConfigCtx.HasOriginServer) {
    Log.Information($"Origin SourceDirectory: {ConfigCtx.Options.OriginSourceDirectory}");
    Log.Information($"Origin SourceImagesDirectory: {ConfigCtx.Options.OriginSourceImagesDirectory}");
    Log.Information($"Origin SourceStaticDirectory: {ConfigCtx.Options.OriginSourceStaticDirectory}");
    if (String.IsNullOrWhiteSpace(ConfigCtx.Options.OriginS3ServiceUrl) is false) {
        Log.Information($"Origin S3 Service URL: {ConfigCtx.Options.OriginS3ServiceUrl}");
    }
    // 
}
if (ConfigCtx.HasEdgeServer is false && ConfigCtx.HasOriginServer is false) {
    Log.Fatal("Neither Edge or Origin Server Urls Given");

    Environment.Exit(1);
}

foreach (string ip in GetAllLocalIPv4(NetworkInterfaceType.Ethernet).Union(GetAllLocalIPv4(NetworkInterfaceType.Wireless80211)))
{
    Log.Information($"Local Ethernet or Wireless Ip4v Address: {ip}");
}

List<Task> webApps = new();

List<string> edgeArgs = new();
List<string> originArgs = new();

int totalApps = 0;

if (ConfigCtx.HasEdgeServer) {
    ++totalApps;
}

if (ConfigCtx.HasOriginServer) {
    ++totalApps;
}

SpeedyCdn.Server.WebApp.WebAppsInitialized = new SemaphoreSlim(0, totalApps);

if (ConfigCtx.HasEdgeServer) {
    edgeArgs.Add($"--urls={ConfigCtx.Options.EdgeUrls}");
    webApps.Add(Task.Run(() => WebApp.RunEdgeAsync(edgeArgs.ToArray())));
}

if (ConfigCtx.HasOriginServer) {
    originArgs.Add($"--urls={ConfigCtx.Options.OriginUrls}");
    webApps.Add(Task.Run(() => WebApp.RunOriginAsync(originArgs.ToArray())));
}

SpinWait sw = new SpinWait();

while (SpeedyCdn.Server.WebApp.WebAppsInitialized.CurrentCount != totalApps)
{
    foreach (Task webApp in webApps)
    {
        if (webApp.Exception is not null) {
            throw webApp.Exception;
        }
    }

    sw.SpinOnce();
}

Log.Information("Switching to File Only Logging");

((ReloadableLogger)Log.Logger).Reload((loggerConfiguration) =>
{
    if (Environment.GetEnvironmentVariable("LOGGING__LEVEL") is string level && level.Equals("Debug", StringComparison.OrdinalIgnoreCase)) {
        loggerConfiguration
            .MinimumLevel.Debug();
    } else {
        loggerConfiguration
            .MinimumLevel.Information();
    }

    loggerConfiguration
        .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning) 
        .MinimumLevel.Override("Microsoft.AspNetCore.Kestrel.BadRequests", LogEventLevel.Debug) 
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .MinimumLevel.Override("System", LogEventLevel.Information)
        .Enrich.FromLogContext()
        .WriteTo.File(
            Path.Combine(ConfigCtx.Options.LogDirectory, $"{nameof(SpeedyCdn)}-App.txt"),
            fileSizeLimitBytes: 1024 * 1024 * 256,
            rollOnFileSizeLimit: true,
            retainedFileCountLimit: 4,
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {WebAppPrefix} {Message:lj}{NewLine}{Exception}");

    return loggerConfiguration;
});

Log.Logger = ((ReloadableLogger)(Log.Logger)).Freeze();

using (LogContext.PushProperty("WebAppPrefix", nameof(SpeedyCdn)))
{
    Log.Information("File Only Logging Now Enabled");
}

await Task.WhenAll(webApps);

void RegisterGlobalExceptionHandling()
{
    AppDomain.CurrentDomain.UnhandledException += 
        (sender, args) => CurrentDomainOnUnhandledException(args);
}

void CurrentDomainOnUnhandledException(UnhandledExceptionEventArgs args)
{
    Exception exception = args.ExceptionObject as Exception;

    string terminatingMessage = args.IsTerminating ? " The application is terminating." : string.Empty;
    string exceptionMessage = exception?.Message ?? "An unmanaged exception occured.";
    string message = string.Concat(exceptionMessage, terminatingMessage);

    Log.Error(exception, message);
}

List<string> GetAllLocalIPv4(NetworkInterfaceType _type)
{
    List<string> ipAddrList = new List<string>();

    foreach (NetworkInterface item in NetworkInterface.GetAllNetworkInterfaces())
    {
        if (item.NetworkInterfaceType == _type && item.OperationalStatus == OperationalStatus.Up) {
            foreach (UnicastIPAddressInformation ip in item.GetIPProperties().UnicastAddresses)
            {
                if (ip.Address.AddressFamily == AddressFamily.InterNetwork) {
                    ipAddrList.Add(ip.Address.ToString());
                }
            }
        }
    }

    return ipAddrList;
}

namespace SpeedyCdn.Server
{
    partial class WebApp
    {
        public static SemaphoreSlim WebAppsInitialized;
    }
}
