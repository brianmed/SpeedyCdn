namespace SpeedyCdn.Server;

using System.Net;
using System.Reflection;
using System.Text;

// 
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
// 

using SpeedyCdn.Enums;
using SpeedyCdn.Server.Entities.Origin;
// 

using BrianMed.AspNetCore.SerilogW3cMiddleware;
using Serilog.Events;

partial class WebApp
{
    async static public Task RunOriginAsync(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        builder.Host.UseSerilog((hostingContext, loggerConfiguration) =>
        {
            loggerConfiguration
                .ReadFrom.Configuration(hostingContext.Configuration)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("WebAppPrefix", "Origin")
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning) 
                .MinimumLevel.Override("Microsoft.AspNetCore.Kestrel.BadRequests", LogEventLevel.Debug) 
                .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information) 
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Information)
                .WriteTo.Logger(Log.Logger);
        }, preserveStaticLogger: true);

        string webLogFile = Path.Combine(ConfigCtx.Options.LogDirectory, $"{ConfigCtx.Options.AppName}-Web-Origin.txt");

        Serilog.ILogger logger = new LoggerConfiguration()
            .ReadFrom.Configuration(builder.Configuration)
            .Enrich.FromLogContext()
            .WriteTo.File(webLogFile, fileSizeLimitBytes: 1024 * 1024 * 64, rollOnFileSizeLimit: true, retainedFileCountLimit: 4)
            .CreateLogger();

        builder.Services.AddSerilogW3cMiddleware(options => {
            options.DisplayBefore = false;
            options.DisplayExceptions = false;
            options.Logger = logger;
        });

        builder.Services
            .AddDbContext<WebOriginDbContext>();

        builder.Services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(ConfigCtx.Options.AppDirectory, "DataProtection")));

        // DbUp 
        builder.Services.AddTransient<IDbUpOriginService, DbUpOriginService>();
        builder.Services.AddTransient<IHmacService, HmacService>();

        WebApplication app = builder.Build();

        Serilog.ILogger initLogger = new LoggerConfiguration()
            .ReadFrom.Configuration(builder.Configuration)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("WebAppPrefix", "Origin")
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning) 
            .MinimumLevel.Override("Microsoft.AspNetCore.Kestrel.BadRequests", LogEventLevel.Debug) 
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information) 
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Information)
            .WriteTo.Logger(Log.Logger)
            .CreateLogger();

        IDbUpOriginService dbUpService = app.Services.GetRequiredService<IDbUpOriginService>();
        dbUpService.MigrateDb(initLogger);

        using (var serviceScope = app.Services.CreateScope())
        {
            WebOriginDbContext webOriginDb = serviceScope.ServiceProvider.GetRequiredService<WebOriginDbContext>();

            webOriginDb.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");

            // 

            AppEntity appEntity = null;

            if (await webOriginDb.App.AnyAsync() is false) {
                appEntity = new()
                {
                    JwtSecret = Guid.NewGuid().ToString(),
                    ApiKey = Guid.NewGuid().ToString()
                };

                webOriginDb.Add(appEntity);

                await webOriginDb.SaveChangesAsync();
            }

            appEntity = await webOriginDb.App
                .SingleAsync();

            if (String.IsNullOrWhiteSpace(appEntity.SignatureKey)) {
                appEntity.SignatureKey = Guid.NewGuid().ToString();

                await webOriginDb.SaveChangesAsync();
            }

            if (ConfigCtx.Options.OriginShowKeys) {
                Log.Information($"Origin ApiKey: {appEntity.ApiKey}");
                Log.Information($"Origin SignatureKey: {appEntity.SignatureKey}");

                Environment.Exit(0);
            }
        }

        // 
        
        IHostApplicationLifetime lifetime = app.Lifetime;

        lifetime.ApplicationStarted.Register(() =>
            WebAppsInitialized.Release());

        if (app.Environment.IsDevelopment()) {
            // 

            // 
            app.UseDeveloperExceptionPage();
            // 
        } else {
            app.UseExceptionHandler("/Error");
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            // app.UseHsts();
        }

        // app.UseHttpsRedirection();
        
        app.UseSerilogW3cMiddleware();

        // 

        app.MapGet("/v1/images/{*imagePath}",
            [ApiKeyAuthorization]
            async (string imagePath) => 
        {
            string fileName = Path.GetFileName(imagePath);
            string contentType = "application/octect-stream";

            FileExtensionContentTypeProvider provider = new();

            if (provider.TryGetContentType(fileName, out contentType) is false)
            {
                Log.Warning($"No content type found for {fileName}");
            }

            return Results.File(Path.Combine(ConfigCtx.Options.OriginSourceImagesDirectory) + Path.DirectorySeparatorChar + Path.Combine(imagePath.Split('/')), contentType);
        });

        app.MapGet("/v1/static/{*filePath}",
            [ApiKeyAuthorization]
            async (string filePath) => 
        {
            string fileName = Path.GetFileName(filePath);
            string contentType = "application/octect-stream";

            FileExtensionContentTypeProvider provider = new();

            if (provider.TryGetContentType(fileName, out contentType) is false)
            {
                Log.Warning($"No content type found for {fileName}");
            }

            return Results.File(Path.Combine(ConfigCtx.Options.OriginSourceStaticDirectory) + Path.DirectorySeparatorChar + Path.Combine(filePath.Split('/')), contentType);
        });

        app.MapGet("/v1/signature/create/{*filePath}",
            [ApiKeyAuthorization]
            async (
                [FromServices]WebOriginDbContext webOriginDb,
                [FromServices]IHmacService hmacService,
                HttpRequest httpRequest,
                string filePath) => 
        {
            using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", "Origin::v1/signature/create");

            string queryString = httpRequest.QueryString.ToString();

            string key = (await webOriginDb.App
                .SingleAsync())
                .SignatureKey;

            Log.Debug($"HmacService Hash: {filePath}{queryString}");

            return new { Signature = hmacService.Hash(key, $"{filePath}{queryString}") };
        });

        // Start the Server 
        await app.RunAsync();
    }

    // 
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class ApiKeyAuthorization : Attribute, IAsyncAuthorizationFilter
{ 
    WebOriginDbContext WebOriginDb { get; init; }

    public ApiKeyAuthorization()
    {
    }

    public ApiKeyAuthorization(WebOriginDbContext webOriginDb)
    {
        WebOriginDb = webOriginDb;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext filterContext)
    {
        if (filterContext is not null)
        {
            Microsoft.Extensions.Primitives.StringValues apikey;

            var key = filterContext.HttpContext.Request.Headers.TryGetValue("ApiKey", out apikey);
            string givenApiKey = apikey.FirstOrDefault();

            if (givenApiKey is not null)
            {
                if ((await WebOriginDb.App.SingleAsync()).ApiKey.Equals(givenApiKey, StringComparison.OrdinalIgnoreCase)) {
                    return;
                }
            } else {
                filterContext.HttpContext.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                filterContext.HttpContext.Response.HttpContext.Features.Get<IHttpResponseFeature>().ReasonPhrase = "Please Provide ApiKey";
            }
        }
    }
}
