using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;

using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;

namespace SpeedyCdn.Server.AppCtx;

class CallerEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        StackTrace st = new StackTrace(true);

        StackFrame [] stFrames = st.GetFrames();

        foreach(StackFrame stack in stFrames)
        {
            string fileName = stack.GetFileName();

            if (String.IsNullOrWhiteSpace(fileName) is false) {
                bool stopHere = fileName.Contains(nameof(SpeedyCdn))
                    && !fileName.Contains(nameof(LoggingCtx));

                if (stopHere) {
                    string caller = $"{fileName}: {stack.GetFileLineNumber()}";

                    logEvent.AddPropertyIfAbsent(new LogEventProperty("Caller", new ScalarValue(caller)));

                    return;
                }
            }
        }

        logEvent.AddPropertyIfAbsent(new LogEventProperty("Caller", new ScalarValue("<Unknown FileName>")));
    }
}

static class LoggerCallerEnrichmentConfiguration
{
    public static LoggerConfiguration WithCaller(this LoggerEnrichmentConfiguration enrichmentConfiguration)
    {
        return enrichmentConfiguration.With<CallerEnricher>();
    }
}

public class LoggingCtx
{
    public static Serilog.ILogger LogEdgeSql { get; internal set;}

    public static Serilog.ILogger LogOriginSql { get; internal set;}

    public static void Initialize(string level = null)
    {
        LogEdgeSql = InitLogging("Sql-Edge", level);

        LogOriginSql = InitLogging("Sql-Origin", level);
    }

    private static Serilog.ILogger InitLogging(string ns, string _level)
    {
        Serilog.ILogger log;
        string outputTemplate;

        string level = Regex.Replace((Environment.GetEnvironmentVariable($"{ConfigCtx.Options.AppName.ToUpper()}_{ns.Replace("-", "_")}_LOGGING".ToUpper())
            ?? Environment.GetEnvironmentVariable($"{ConfigCtx.Options.AppName.ToUpper()}_DEFAULT_LOGGING".ToUpper())
            ?? String.Empty),
            "^$", (_level ?? "None"));

        if (level == "None") {
            log = Serilog.Core.Logger.None;
        } else {
            string logFile = Path.Combine(ConfigCtx.Options.LogDirectory, $"{ConfigCtx.Options.AppName}-{ns}.txt");
            Console.WriteLine($"Initializing {ns} Log: {level} {logFile}");

            LoggerConfiguration logConfiguration = new LoggerConfiguration();

            try
            {
                logConfiguration = (LoggerConfiguration)(logConfiguration
                    .MinimumLevel.GetType()
                    .GetMethod(Enum.Parse<LogEventLevel>(level).ToString())
                    .Invoke(logConfiguration.MinimumLevel, null));
            }
            catch (Exception ex)
            {
                throw new Exception($"Please use a minimum logging level of None, Verbose, Debug, Information, Warning, Error, Fatal: {ex.Message}");
            }

            if (ns.StartsWith("Sql") && (level == "Debug" || level == "Verbose")) {
                outputTemplate = "[{Timestamp:HH:mm:ss} {Level:u3}] {Message} (near {Caller}){NewLine}{Exception}";

                logConfiguration = logConfiguration
                    .Enrich.WithCaller();
            } else {
                outputTemplate = "[{Timestamp:HH:mm:ss} {Level:u3}] {Message}{NewLine}{Exception}";

                logConfiguration = logConfiguration
                    .Enrich.FromLogContext();
            }

            log = logConfiguration
                .Enrich.FromLogContext()
                .WriteTo.File(logFile,
                    outputTemplate: outputTemplate,
                    fileSizeLimitBytes: 1024 * 1024 * 128,
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: 4)
                .CreateLogger();

            log.Information($"Started {ns} Logging");
        }

        return log;
    }
}
