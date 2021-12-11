namespace SpeedyCdn.Server;

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

using SpeedyCdn.Server.Entities.Edge;

using BrianMed.AspNetCore.SerilogW3cMiddleware;
using Serilog.Context;
using Serilog.Events;

partial class WebApp
{
    async static public Task RunEdgeAsync(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        var joy = builder.Host.UseSerilog((hostingContext, loggerConfiguration) =>
        {
            loggerConfiguration
                .ReadFrom.Configuration(hostingContext.Configuration)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("WebAppPrefix", "Edge")
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning) 
                .MinimumLevel.Override("Microsoft.AspNetCore.Kestrel.BadRequests", LogEventLevel.Debug) 
                .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information) 
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Information)
                .WriteTo.Logger(Log.Logger);
        },
        preserveStaticLogger: true);

        string webLogFile = Path.Combine(ConfigCtx.Options.LogDirectory, $"{ConfigCtx.Options.AppName}-Web-Edge.txt");

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
            .AddDbContext<WebEdgeDbContext>();

        builder.Services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(ConfigCtx.Options.AppDirectory, "DataProtection")));

        builder.Services.AddHttpClient();

        builder.Services.AddTransient<IDbUpEdgeService, DbUpEdgeService>();
        builder.Services.AddTransient<IHmacService, HmacService>();
        builder.Services.AddScoped<ICachePathService, CachePathService>();
        builder.Services.AddScoped<IDownloadService, DownloadService>();
        builder.Services.AddScoped<IImageOperationService, ImageOperationService>();

        // Build

        WebApplication app = builder.Build();

        Serilog.ILogger initLogger = new LoggerConfiguration()
            .ReadFrom.Configuration(builder.Configuration)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("WebAppPrefix", "Edge")
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning) 
            .MinimumLevel.Override("Microsoft.AspNetCore.Kestrel.BadRequests", LogEventLevel.Debug) 
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information) 
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Information)
            .WriteTo.Logger(Log.Logger)
            .CreateLogger();

        IDbUpEdgeService dbUpService = app.Services.GetRequiredService<IDbUpEdgeService>();
        dbUpService.MigrateDb(initLogger);

        using (var serviceScope = app.Services.CreateScope())
        {
            WebEdgeDbContext webEdgeDb = serviceScope.ServiceProvider.GetService<WebEdgeDbContext>();

            webEdgeDb.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
        }

        IHostApplicationLifetime lifetime = app.Lifetime;
        lifetime.ApplicationStarted.Register(() => WebAppsInitialized.Release());

        if (app.Environment.IsDevelopment()) {
            app.UseDeveloperExceptionPage();
        } else {
            app.UseExceptionHandler("/Error");
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            // app.UseHsts();
        }

        // app.UseHttpsRedirection();
        
        app.UseSerilogW3cMiddleware();

        app.MapGet("/v1/images/{*imagePath}", async (
            [FromServices]WebEdgeDbContext webEdgeDb,
            [FromServices]ICachePathService cachePathService,
            [FromServices]IDownloadService download,
            [FromServices]IImageOperationService imageOperation,
            [FromServices]IHmacService hmacService,
            HttpRequest httpRequest,
            IHttpClientFactory httpClientFactory,
            string imagePath) => 
        {
            using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", "Edge::v1/images");

            string queryStringUnmodified = httpRequest.QueryString.ToString();

            QueryStringEnumerable.Enumerator queryEnumerator = new QueryStringEnumerable(queryStringUnmodified).GetEnumerator();
            List<KeyValuePair<string, string>> allExceptSignature = new();

            string signature = null;

            while (queryEnumerator.MoveNext())
            {
                string name = queryEnumerator.Current
                        .DecodeName()
                        .ToString()
                        .ToLower();

                if (name == "signature") {
                    signature = queryEnumerator.Current.DecodeValue().ToString();
                } else {
                    allExceptSignature.Add(
                        new KeyValuePair<string, string>(
                            queryEnumerator.Current.DecodeName().ToString(),
                            queryEnumerator.Current.DecodeValue().ToString()));
                }
            }

            string queryString = QueryString.Create(allExceptSignature).ToString();

            bool haveQueryStringSignatureKey = String.IsNullOrWhiteSpace(signature)
                is false;
            bool haveCliSignatureKey = String.IsNullOrWhiteSpace(ConfigCtx.Options.EdgeOriginSignatureKey)
                is false;

            if (haveCliSignatureKey) {
                Log.Debug($"Signature IsValid: {imagePath}{queryString}");

                if (hmacService.IsValid(ConfigCtx.Options.EdgeOriginSignatureKey, $"{imagePath}{queryString}", signature) is false) {
                    Log.Debug($"Signature Mismatch");

                    return Results.StatusCode(404);
                }
            } else if (haveQueryStringSignatureKey) {
                Log.Debug($"Signature Given in Query and No Signature Configured");

                return Results.StatusCode(404);
            }

            string fileName = Path.GetFileName(imagePath);
            string contentType = "application/octect-stream";

            FileExtensionContentTypeProvider provider = new();

            if (provider.TryGetContentType(fileName, out contentType) is false)
            {
                Log.Warning($"No content type found for {fileName}");
            }

            string cacheImagePathJson = JsonSerializer.Serialize(new[] { imagePath });
            string cacheImagePath = cachePathService.RelativeWithBucket(cacheImagePathJson, fileName);
            await download.GetImageAsync(imagePath, cacheImagePath);

            string cacheImagePathAndQueryStringJson = JsonSerializer.Serialize(new[] { imagePath, queryString });
            string cacheImagePathAndQueryString = cachePathService.RelativeWithBucket(cacheImagePathAndQueryStringJson, fileName);
            await imageOperation.RunAllFromQueryAsync(cacheImagePath, queryString, cacheImagePathAndQueryString, imagePath);
            
            FileStream imageOperationCacheFS = new FileStream(Path.Combine(ConfigCtx.Options.EdgeCacheImagesDirectory, cacheImagePathAndQueryString), FileMode.Open);

            TimeSpan oneHour = TimeSpan.FromHours(1);
            TimeSpan oneWeek = TimeSpan.FromDays(7);

            ImageCacheElementEntity imageCacheElementEntity = (await webEdgeDb.ImageCacheElements.FromSqlInterpolated($@"
                    INSERT OR IGNORE INTO ImageCacheElements ( 
                        CachePath,
                        FileSizeBytes,
                        LastAccessedUtc,
                        ExpireUtc
                    ) VALUES (
                        {cacheImagePath},
                        {new FileInfo(Path.Combine(ConfigCtx.Options.EdgeCacheImagesDirectory, cacheImagePath)).Length},
                        strftime('%s', 'now') - (strftime('%s', 'now') % {oneHour.TotalSeconds}),
                        strftime('%s', 'now') + {oneWeek.TotalSeconds}
                    );
                    
                    INSERT INTO ImageCacheElements ( 
                        CachePath,
                        FileSizeBytes,
                        LastAccessedUtc,
                        ExpireUtc
                    ) VALUES (
                        {cacheImagePathAndQueryString},
                        {imageOperationCacheFS.Length},
                        strftime('%s', 'now') - (strftime('%s', 'now') % {oneHour.TotalSeconds}),
                        strftime('%s', 'now') + {oneWeek.TotalSeconds}
                    )
                    
                    ON CONFLICT (CachePath)
                    DO UPDATE
                        SET LastAccessedUtc = strftime('%s', 'now') - (strftime('%s', 'now') % {oneHour.TotalSeconds}),
                            ExpireUtc = strftime('%s', 'now') + {oneWeek.TotalSeconds},
                            Updated = CURRENT_TIMESTAMP
                        WHERE CachePath = {cacheImagePathAndQueryString}
                    
                    RETURNING *;
                ")
                .ToListAsync())
                .Single();

            // Sending

            Log.Debug($"Sending: {imageCacheElementEntity.ImageCacheElementId} - {fileName} as {contentType}");

            return Results.File(imageOperationCacheFS, contentType);
        });

        app.MapGet("/v1/static/{*staticPath}", async (
            [FromServices]ICachePathService cachePathService,
            [FromServices]IDownloadService download,
            [FromServices]WebEdgeDbContext webEdgeDb,
            IHttpClientFactory httpClientFactory,
            string staticPath) => 
        {
            using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", "Edge::v1/static");

            string url = $"{ConfigCtx.Options.EdgeOriginUrl}/v1/static/{staticPath}";

            string fileName = Path.GetFileName(staticPath);
            string contentType = "application/octect-stream";

            FileExtensionContentTypeProvider provider = new();

            if (provider.TryGetContentType(fileName, out contentType) is false)
            {
                Log.Warning($"No content type found for {fileName}");
            }

            string cacheStaticPathJson = JsonSerializer.Serialize(new[] { staticPath });
            string cacheStaticPath = cachePathService.RelativeWithBucket(cacheStaticPathJson, fileName);
            await download.GetStaticAsync(staticPath, cacheStaticPath);

            FileStream staticCacheFS = new FileStream(Path.Combine(ConfigCtx.Options.EdgeCacheStaticDirectory, cacheStaticPath), FileMode.Open);

            TimeSpan oneHour = TimeSpan.FromHours(1);
            TimeSpan oneWeek = TimeSpan.FromDays(7);

            StaticCacheElementEntity staticCacheElementEntity = (await webEdgeDb.StaticCacheElements.FromSqlInterpolated($@"
                    INSERT INTO StaticCacheElements ( 
                        CachePath,
                        FileSizeBytes,
                        LastAccessedUtc,
                        ExpireUtc
                    ) VALUES (
                        {cacheStaticPath},
                        {new FileInfo(Path.Combine(ConfigCtx.Options.EdgeCacheStaticDirectory, cacheStaticPath)).Length},
                        strftime('%s', 'now') - (strftime('%s', 'now') % {oneHour.TotalSeconds}),
                        strftime('%s', 'now') + {oneWeek.TotalSeconds}
                    )
                    
                    ON CONFLICT (CachePath)
                    DO UPDATE
                        SET LastAccessedUtc = strftime('%s', 'now') - (strftime('%s', 'now') % {oneHour.TotalSeconds}),
                            ExpireUtc = strftime('%s', 'now') + {oneWeek.TotalSeconds},
                            Updated = CURRENT_TIMESTAMP
                        WHERE CachePath = {cacheStaticPath}
                    
                    RETURNING *;
                ")
                .ToListAsync())
                .Single();

            Log.Debug($"Sending: {staticCacheFS.Name} as {contentType}");
            return Results.File(staticCacheFS, contentType);
        });

        app.MapGet("/", async () => 
        {
            return $"SpeedyCdn: https://github.com/brianmed/SpeedyCdn";
        });

        await app.RunAsync();
    }
}
