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

using SpeedyCdn.Dto;

partial class WebApp
{
    // public static SemaphoreSlim CreateOneUseMutex = new SemaphoreSlim(1, 1);

    public static Dictionary<string, HashSet<long>> OneUseNumbers = new();

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

        builder.Services.AddHttpContextAccessor();

        builder.Services.AddTransient<IDbUpEdgeService, DbUpEdgeService>();
        builder.Services.AddScoped<IBarcodeService, BarcodeService>();
        builder.Services.AddScoped<ICacheElementService, CacheElementService>();
        builder.Services.AddScoped<ICachePathService, CachePathService>();
        builder.Services.AddScoped<IDownloadService, DownloadService>();
        builder.Services.AddScoped<IHmacService, HmacService>();
        builder.Services.AddScoped<IImageOperationService, ImageOperationService>();
        builder.Services.AddScoped<IQueryStringService, QueryStringService>();

        // Build

        builder.Services.AddHostedService<EdgePruneCacheHostedService>();

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

        app.MapGet("/v1/s3/images/{bucketName}/{*imageKey}", async (
            [FromServices]ICacheElementService cacheElementService,
            [FromServices]ICachePathService cachePathService,
            [FromServices]IDownloadService download,
            [FromServices]IImageOperationService imageOperation,
            [FromServices]IHmacService hmacService,
            [FromServices]IQueryStringService queryStringService,
            HttpRequest httpRequest,
            string bucketName, string imageKey) => 
        {
            using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", "Edge::v1/s3/images");

            QueryString queryString = queryStringService.CreateExcept(httpRequest.QueryString, "signature");

            if (hmacService.IsValid($"s3/images/{bucketName}/{imageKey}", httpRequest.QueryString) is false) {
                return Results.StatusCode(404);
            }

            string fileName = Path.GetFileName(imageKey);
            string contentType = "application/octect-stream";

            FileExtensionContentTypeProvider provider = new();

            if (provider.TryGetContentType(fileName, out contentType) is false)
            {
                Log.Warning($"No content type found for {fileName}");
            }

            string cacheImagePathJson = JsonSerializer.Serialize(new[] { $"{bucketName}/{imageKey}" });
            string cacheImagePath = cachePathService.RelativeWithBucket(cacheImagePathJson, fileName);
            await download.GetS3ImageAsync(bucketName, imageKey, cacheImagePath);

            string cacheImagePathAndQueryStringJson = JsonSerializer.Serialize(new[] { $"{bucketName}/{imageKey}", queryString.ToString() });
            string cacheImagePathAndQueryString = cachePathService.RelativeWithBucket(cacheImagePathAndQueryStringJson, fileName);
            await imageOperation.RunAllFromQueryAsync(cacheImagePath, queryString, cacheImagePathAndQueryString, $"{bucketName}/{imageKey}", AppCtx.ConfigCtx.Options.EdgeCacheS3ImagesDirectory);
            
            S3ImageCacheElementEntity s3ImageCacheElementEntity = await cacheElementService
                .UpsertS3ImageAsync(cacheImagePath, cacheImagePathAndQueryString);

            Log.Debug($"Sending: {s3ImageCacheElementEntity.S3ImageCacheElementId} - {fileName} as {contentType}");
            return Results.File(Path.Combine(ConfigCtx.Options.EdgeCacheS3ImagesDirectory, cacheImagePathAndQueryString), contentType);
        });

        app.MapGet("/v1/images/{*imagePath}", async (
            [FromServices]WebEdgeDbContext webEdgeDb,
            [FromServices]ICacheElementService cacheElementService,
            [FromServices]ICachePathService cachePathService,
            [FromServices]IDownloadService download,
            [FromServices]IImageOperationService imageOperation,
            [FromServices]IHmacService hmacService,
            [FromServices]IQueryStringService queryStringService,
            HttpRequest httpRequest,
            string imagePath) => 
        {
            using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", "Edge::v1/images");

            QueryString queryString = queryStringService.CreateExcept(httpRequest.QueryString, "signature");

            if (hmacService.IsValid($"images/{imagePath}", httpRequest.QueryString) is false) {
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

            string cacheImagePathAndQueryStringJson = JsonSerializer.Serialize(new[] { imagePath, queryString.ToString() });
            string cacheImagePathAndQueryString = cachePathService.RelativeWithBucket(cacheImagePathAndQueryStringJson, fileName);
            await imageOperation.RunAllFromQueryAsync(cacheImagePath, queryString, cacheImagePathAndQueryString, imagePath, AppCtx.ConfigCtx.Options.EdgeCacheImagesDirectory);
            
            ImageCacheElementEntity imageCacheElementEntity = await cacheElementService
                .UpsertImageAsync(cacheImagePath, cacheImagePathAndQueryString);

            // Sending

            Log.Debug($"Sending: {imageCacheElementEntity.ImageCacheElementId} - {fileName} as {contentType}");
            return Results.File(Path.Combine(ConfigCtx.Options.EdgeCacheImagesDirectory, cacheImagePathAndQueryString), contentType);
        });

        app.MapGet("/v1/static/{*staticPath}", async (
            [FromServices]ICacheElementService cacheElementService,
            [FromServices]ICachePathService cachePathService,
            [FromServices]IDownloadService download,
            [FromServices]IHmacService hmacService,
            [FromServices]IQueryStringService queryStringService,
            [FromServices]WebEdgeDbContext webEdgeDb,
            HttpRequest httpRequest,
            string staticPath) => 
        {
            using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", "Edge::v1/static");

            if (hmacService.IsValid($"static/{staticPath}", httpRequest.QueryString) is false) {
                return Results.StatusCode(404);
            }

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

            StaticCacheElementEntity staticCacheElementEnitty = await cacheElementService
                .UpsertStaticAsync(cacheStaticPath);

            Log.Debug($"Sending: {staticCacheElementEnitty.StaticCacheElementId} as {contentType}");

            return Results.File(Path.Combine(ConfigCtx.Options.EdgeCacheStaticDirectory, cacheStaticPath), contentType);
        });

        app.MapGet("/v1/barcode", async (
            [FromServices]IBarcodeService barcodeService,
            [FromServices]ICacheElementService cacheElementService,
            [FromServices]ICachePathService cachePathService,
            [FromServices]IHmacService hmacService,
            [FromServices]IQueryStringService queryStringService,
            [FromServices]WebEdgeDbContext webEdgeDb,
            HttpRequest httpRequest) => 
        {
            using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", "Edge::v1/barcode");

            QueryString queryString = queryStringService.CreateExcept(httpRequest.QueryString, "signature");

            if (hmacService.IsValid("barcode", httpRequest.QueryString) is false) {
                return Results.StatusCode(404);
            }

            string cacheBarcodeQueryStringJson = JsonSerializer.Serialize(new[] { queryString.ToString() });
            string cacheBarcodeQueryString = cachePathService.RelativeWithBucket(cacheBarcodeQueryStringJson, String.Empty);

            barcodeService.GenerateFromQueryString(cacheBarcodeQueryString, queryString);
            
            FileStream barcodeCacheStream = new FileStream(Path.Combine(ConfigCtx.Options.EdgeCacheBarcodesDirectory, cacheBarcodeQueryString), FileMode.Open);

            BarcodeCacheElementEntity barcodeCacheElementEntity = await cacheElementService
                .UpsertBarcodeAsync(cacheBarcodeQueryString);

            Log.Debug($"Sending: {barcodeCacheElementEntity.BarcodeCacheElementId} as image/png");
            return Results.File(barcodeCacheStream, "image/png");
        });

        app.MapGet("/v1/display/{*display}", async (
            [FromServices]IHmacService hmacService,
            [FromServices]IQueryStringService queryStringService,
            [FromServices]IHttpClientFactory HttpClientFactory,
            HttpRequest httpRequest,
            string display
            ) => 
        {
            using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", "Edge::v1/display");

            QueryString queryString = queryStringService.CreateExcept(httpRequest.QueryString, "signature");

            if (hmacService.IsValid($"display/{display}", httpRequest.QueryString) is false) {
                return Results.StatusCode(404);
            }

            string displayGet = $"{ConfigCtx.Options.EdgeOriginUrl}/v1/display/{display}";

            HttpClient httpClient = HttpClientFactory.CreateClient();
            DisplayUrlDto displayUrlDto = null;

            if (ConfigCtx.Options.EdgeOriginApiKey is not null) {
                Log.Debug($"Adding ApiKey to Headers");
                httpClient.DefaultRequestHeaders.Add("ApiKey", ConfigCtx.Options.EdgeOriginApiKey);
            }

            using (HttpResponseMessage response = await httpClient.GetAsync(displayGet))
            {
                Log.Debug($"Redirect: {await response.Content.ReadAsStringAsync()}");

                JsonSerializerOptions options = new()
                {
                    PropertyNameCaseInsensitive = true
                };
                                
                displayUrlDto = JsonSerializer.Deserialize<DisplayUrlDto>(await response.Content.ReadAsStringAsync(), options);
            }

            if (displayUrlDto is null) {
                return Results.BadRequest();
            } else {
                Log.Debug($"Redirect: {displayUrlDto.RedirectPath}{displayUrlDto.QueryString}");

                return Results.Redirect($"{displayUrlDto.RedirectPath}{displayUrlDto.QueryString}");
            }
        });

        app.MapGet("/v1/uuid/{uuidUrl}", async (
            [FromServices]IHmacService hmacService,
            [FromServices]IQueryStringService queryStringService,
            [FromServices]IHttpClientFactory HttpClientFactory,
            HttpRequest httpRequest,
            string uuidUrl) => 
        {
            using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", "Edge::v1/uuid");

            QueryString queryString = queryStringService.CreateExcept(httpRequest.QueryString, "signature");

            if (hmacService.IsValid($"uuid/{uuidUrl}", httpRequest.QueryString) is false) {
                return Results.StatusCode(404);
            }

            string uuidGet = $"{ConfigCtx.Options.EdgeOriginUrl}/v1/uuid/{uuidUrl}";

            HttpClient httpClient = HttpClientFactory.CreateClient();
            UuidUrlDto uuidUrlDto = null;

            if (ConfigCtx.Options.EdgeOriginApiKey is not null) {
                Log.Debug($"Adding ApiKey to Headers");
                httpClient.DefaultRequestHeaders.Add("ApiKey", ConfigCtx.Options.EdgeOriginApiKey);
            }

            using (HttpResponseMessage response = await httpClient.GetAsync(uuidGet))
            {
                Log.Debug($"Redirect: {await response.Content.ReadAsStringAsync()}");

                JsonSerializerOptions options = new()
                {
                    PropertyNameCaseInsensitive = true
                };
                                
                uuidUrlDto = JsonSerializer.Deserialize<UuidUrlDto>(await response.Content.ReadAsStringAsync(), options);
            }

            if (uuidUrlDto is null) {
                return Results.BadRequest();
            } else {
                Log.Debug($"Redirect: {uuidUrlDto.RedirectPath}{uuidUrlDto.QueryString}");
                return Results.Redirect($"{uuidUrlDto.RedirectPath}{uuidUrlDto.QueryString}");
            }
        });

        app.MapGet("/", async () => 
        {
            return $"SpeedyCdn: https://github.com/brianmed/SpeedyCdn";
        });

        await app.RunAsync();
    }
}
