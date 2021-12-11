using System.Reflection;

using DbUp;
using DbUp.Engine.Output;
using Serilog;

public interface IDbUpEdgeService
{
    void MigrateDb(Serilog.ILogger logger);
}

public class DbUpEdgeService : IDbUpEdgeService
{
    IConfiguration Configuration { get; init; }

    public DbUpEdgeService(IConfiguration configuration)
    {
        Configuration = configuration;
    }

    public void MigrateDb(Serilog.ILogger logger)
    {
        Func<string, bool> sqlList = delegate (string s) {
            List<string> contains = new() {
                $".Sql.{nameof(SpeedyCdn)}EdgeDb",
            };

            foreach (string start in contains) {
                if (s.Contains(start)) {
                    return true;
                }
            }

            return false;
        };

        string connectionString = ConfigCtx.Options.EdgeAppDbConnectionString;

        var upgrader = DeployChanges.To
            .SQLiteDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(Assembly.GetExecutingAssembly(), sqlList)
            .LogTo(new SerilogUpgradeLogger() { Logger = logger })
            .Build();

        var result = upgrader.PerformUpgrade();

        if (result.Successful is false)
        {
            throw result.Error;
        }
    }
}

public class SerilogUpgradeLogger : IUpgradeLog
{
    public Serilog.ILogger Logger { get; init; }

    public void WriteInformation(String format, params Object[] args)
    {
        Logger.Information(String.Format(format, args));
    }

    public void WriteError(String format, params Object[] args)
    {
        Logger.Error(format, args);
    }

    public void WriteWarning(String format, params Object[] args)
    {
        Logger.Warning(format, args);
    }
}
