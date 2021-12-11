using System.Reflection;

using DbUp;
using DbUp.Engine.Output;
using Serilog;

public interface IDbUpOriginService
{
    void MigrateDb(Serilog.ILogger logger);
}

public class DbUpOriginService : IDbUpOriginService
{
    IConfiguration Configuration { get; init; }

    public DbUpOriginService(IConfiguration configuration)
    {
        Configuration = configuration;
    }

    public void MigrateDb(Serilog.ILogger logger)
    {
        Func<string, bool> sqlList = delegate (string s) {
            List<string> contains = new() {
                $".Sql.{nameof(SpeedyCdn)}OriginDb",
            };

            foreach (string start in contains) {
                if (s.Contains(start)) {
                    return true;
                }
            }

            return false;
        };

        string connectionString = ConfigCtx.Options.OriginAppDbConnectionString;

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
