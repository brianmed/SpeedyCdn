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
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;
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

            Log.Debug($"s3: {bucketName} {imageKey}");

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

            S3ImageCacheElementEntity originalElement = await download
                .GetOriginS3ImageAsync(bucketName, imageKey);

            S3ImageCacheElementEntity modifiedElement = await imageOperation
                .S3ImageFromQueryAsync(originalElement, queryString);

            Log.Debug($"Sending: {fileName} as {contentType}");
            return Results.File(cachePathService.CachePath(modifiedElement), contentType);
        });

        app.MapGet("/v1/images/{*imageUrlPath}", async (
            [FromServices]WebEdgeDbContext webEdgeDb,
            [FromServices]ICacheElementService cacheElementService,
            [FromServices]ICachePathService cachePathService,
            [FromServices]IDownloadService download,
            [FromServices]IImageOperationService imageOperation,
            [FromServices]IHmacService hmacService,
            [FromServices]IQueryStringService queryStringService,
            HttpRequest httpRequest,
            string imageUrlPath) => 
        {
            using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", "Edge::v1/images");

            Log.Debug($"Starting");

            QueryString queryString = queryStringService.CreateExcept(httpRequest.QueryString, "signature");

            if (hmacService.IsValid($"images/{imageUrlPath}", httpRequest.QueryString) is false) {
                return Results.StatusCode(404);
            }

            string fileName = Path.GetFileName(imageUrlPath);
            string contentType = "application/octect-stream";

            FileExtensionContentTypeProvider provider = new();

            if (provider.TryGetContentType(fileName, out contentType) is false)
            {
                Log.Warning($"No content type found for {fileName}");
            }

            ImageCacheElementEntity imageCacheElement = await download
                .GetOriginImageAsync(imageUrlPath);

            ImageCacheElementEntity imageCacheModifiedElement = await imageOperation
                .ImageFromQueryAsync(imageCacheElement, queryString);

            Log.Debug($"Sending: {fileName} as {contentType}");
            return Results.File(cachePathService.CachePath(imageCacheModifiedElement), contentType);
        });

        app.MapGet("/v1/static/{*staticUrlPath}", async (
            [FromServices]ICacheElementService cacheElementService,
            [FromServices]ICachePathService cachePathService,
            [FromServices]IDownloadService download,
            [FromServices]IHmacService hmacService,
            [FromServices]IQueryStringService queryStringService,
            [FromServices]WebEdgeDbContext webEdgeDb,
            HttpRequest httpRequest,
            string staticUrlPath) => 
        {
            using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", "Edge::v1/static");

            if (hmacService.IsValid($"static/{staticUrlPath}", httpRequest.QueryString) is false) {
                return Results.StatusCode(404);
            }

            string fileName = Path.GetFileName(staticUrlPath);
            string contentType = "application/octect-stream";

            FileExtensionContentTypeProvider provider = new();

            if (provider.TryGetContentType(fileName, out contentType) is false)
            {
                Log.Warning($"No content type found for {fileName}");
            }

            StaticCacheElementEntity cacheElement = await download.GetOriginStaticAsync(staticUrlPath);

            Log.Debug($"Sending: {cacheElement.StaticCacheElementId} as {contentType}");
            return Results.File(cachePathService.CachePath(cacheElement), contentType);
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

            BarcodeCacheElementEntity barcodeCacheElement =
                await barcodeService.GenerateFromQueryString(queryString);

            Log.Debug($"Sending: {barcodeCacheElement.BarcodeCacheElementId} as image/png");
            return Results.File(cachePathService.CachePath(barcodeCacheElement), "image/png");
        });

        app.MapGet("/v1/display/{*display}", async (
            [FromServices]IHmacService hmacService,
            [FromServices]IQueryStringService queryStringService,
            [FromServices]IHttpClientFactory httpClientFactory,
            [FromServices]IHttpContextFactory httpContextFactory,
            [FromServices]IServiceProvider serviceProvider,
            [FromServices]IEnumerable<EndpointDataSource> endpoints,
            HttpContext httpContext,
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

            HttpClient httpClient = httpClientFactory.CreateClient();
            DisplayUrlDto displayUrlDto = null;

            if (ConfigCtx.Options.EdgeOriginApiKey is not null) {
                Log.Debug($"Adding ApiKey to Headers");
                httpClient.DefaultRequestHeaders.Add("ApiKey", ConfigCtx.Options.EdgeOriginApiKey);
            }

            using (HttpResponseMessage response = await httpClient.GetAsync(displayGet))
            {
                Log.Debug($"GET DisplayUrl: {await response.Content.ReadAsStringAsync()}");
                                
                displayUrlDto = JsonSerializer.Deserialize<DisplayUrlDto>(await response.Content.ReadAsStringAsync());
            }

            if (displayUrlDto is null) {
                return Results.BadRequest();
            } else if (displayUrlDto.RedirectPath is null && displayUrlDto.QueryString is null) {
                return Results.StatusCode(404);
            } else {
                Log.Debug($"Internal Redirect: {displayUrlDto.RedirectPath}{displayUrlDto.QueryString}");

                var joyEndpoints = endpoints
                    .SelectMany(es => es.Endpoints)
                    .OfType<RouteEndpoint>();

                RouteValueDictionary routeValues = new();

                RouteEndpoint matchedEndpoint = joyEndpoints.Where(e => new TemplateMatcher(
                            TemplateParser.Parse(e.RoutePattern.RawText),
                            new RouteValueDictionary())
                        .TryMatch(displayUrlDto.RedirectPath, routeValues))
                    .OrderBy(c => c.Order)
                    .FirstOrDefault();

                if (matchedEndpoint is not null) {
                    using (IServiceScope requestServices = serviceProvider.CreateScope())
                    {
                        HttpContext context = new DefaultHttpContext { RequestServices = requestServices.ServiceProvider };
                        
                        context.Request.Method = "GET";
                        context.Request.Path = displayUrlDto.RedirectPath;
                        context.Request.QueryString = new QueryString(displayUrlDto.QueryString);
                        context.Request.RouteValues = routeValues;

                        String tempFilePath = Path.GetTempFileName();

                        using (StreamWriter sw = new(tempFilePath))
                        {
                            context.Response.Body = sw.BaseStream;

                            await matchedEndpoint.RequestDelegate(context);
                        }

                        return Results.Stream(new StreamReader(tempFilePath).BaseStream, context.Response.ContentType);
                    }
                } else {
                    return Results.StatusCode(404);
                }
            }
        });

        app.MapGet("/", async () => 
        {
            return $"SpeedyCdn: https://github.com/brianmed/SpeedyCdn";
        });

        await app.RunAsync();
    }
}
