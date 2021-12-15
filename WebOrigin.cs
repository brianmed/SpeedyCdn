namespace SpeedyCdn.Server;

using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;

// 
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
// 

using SpeedyCdn.Enums;
using SpeedyCdn.Server.Entities.Origin;
// 

using Amazon.S3;
using Amazon.S3.Model;
using BrianMed.AspNetCore.SerilogW3cMiddleware;
using Serilog.Events;

using SpeedyCdn.Dto;

partial class WebApp
{
    public static SemaphoreSlim OneUuidUrlAtAtime = new SemaphoreSlim(1, 1);

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

        // 

        // 
        builder.Services.AddHttpContextAccessor();

        builder.Services.AddAuthentication(options => options.DefaultScheme = "ApiKey")
            .AddScheme<ApiKeyAuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
                "ApiKey",
                options => { });

        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("ApiKey", policy =>
                policy.Requirements.Add(new ApiKeyAuthorizationRequirement()));
    
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });
        // 

        builder.Services.AddScoped<IAuthorizationHandler, ApiKeyAuthorizationHandler>();
        builder.Services.AddTransient<IDbUpOriginService, DbUpOriginService>();
        builder.Services.AddTransient<IHmacService, HmacService>();
        builder.Services.AddTransient<IQueryStringService, QueryStringService>();
        builder.Services.AddTransient<IDbUpOriginService, DbUpOriginService>();

        builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = null;
        });

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
                Console.WriteLine($"Origin ApiKey: {appEntity.ApiKey}");
                Console.WriteLine($"Origin SignatureKey: {appEntity.SignatureKey}");

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

        // 
        app.UseAuthentication();
        app.UseAuthorization();
        // 

        app.MapGet("/v1/images/{*imagePath}",
            [Authorize(Policy = "ApiKey")]
            async (string imagePath) => 
        {
            using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"Origin::v1/images");

            Log.Debug($"images: {imagePath}");

            string fileName = Path.GetFileName(imagePath);
            string contentType = "application/octect-stream";

            FileExtensionContentTypeProvider provider = new();

            if (provider.TryGetContentType(fileName, out contentType) is false)
            {
                Log.Warning($"No content type found for {fileName}");
            }

            return Results.File(Path.Combine(ConfigCtx.Options.OriginSourceImagesDirectory) + Path.DirectorySeparatorChar + Path.Combine(imagePath.Split('/')), contentType);
        });

        app.MapGet("/v1/s3/{fileType}/{bucketName}/{*imageKey}",
            [Authorize(Policy = "ApiKey")]
            async (string fileType, string bucketName, string imageKey) => 
        {
            using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"Origin::v1/s3/{fileType}");

            Log.Debug($"s3: {AppCtx.ConfigCtx.Options.OriginS3ServiceUrl} {bucketName} {imageKey}");

            string fileName = Path.GetFileName(imageKey);
            string contentType = "application/octect-stream";

            FileExtensionContentTypeProvider provider = new();

            if (provider.TryGetContentType(fileName, out contentType) is false)
            {
                Log.Warning($"No content type found for {fileName}");
            }

            AmazonS3Config config = new AmazonS3Config();
            config.ServiceURL = AppCtx.ConfigCtx.Options.OriginS3ServiceUrl;
            
            using AmazonS3Client s3Client = new AmazonS3Client(
                AppCtx.ConfigCtx.Options.OriginS3AccessKey,
                AppCtx.ConfigCtx.Options.OriginS3SecretKey,
                config);

            GetObjectRequest request = new GetObjectRequest();
            request.BucketName = bucketName;
            request.Key        = imageKey;

            Log.Debug($"s3: {AppCtx.ConfigCtx.Options.OriginS3ServiceUrl} [download] [{bucketName}] [{imageKey}]");
            GetObjectResponse response = await s3Client.GetObjectAsync(request);

            return Results.File(response.ResponseStream, contentType);
        });

        app.MapGet("/v1/static/{*filePath}",
            [Authorize(Policy = "ApiKey")]
            async (string filePath) => 
        {
            using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"Origin::v1/static");

            string fileName = Path.GetFileName(filePath);
            string contentType = "application/octect-stream";

            FileExtensionContentTypeProvider provider = new();

            if (provider.TryGetContentType(fileName, out contentType) is false)
            {
                Log.Warning($"No content type found for {fileName}");
            }

            return Results.File(Path.Combine(ConfigCtx.Options.OriginSourceStaticDirectory) + Path.DirectorySeparatorChar + Path.Combine(filePath.Split('/')), contentType);
        });

        app.MapGet("/v1/display",
            [Authorize(Policy = "ApiKey")]
            async (
                [FromServices]WebOriginDbContext webOriginDb,
                HttpRequest httpRequest) => 
        {
            using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"Origin::v1/display");

            return (await webOriginDb.DisplayUrl
                .OrderBy(v => v.DisplayUrlId)
                .ToListAsync())
                .Adapt<IEnumerable<DisplayUrlDto>>();
        });

        app.MapDelete("/v1/display/{*display}",
            [Authorize(Policy = "ApiKey")]
            async (
                [FromServices]WebOriginDbContext webOriginDb,
                string display) => 
        {
            using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"Origin::v1/display/display");

            try
            {
                DisplayUrlEntity displayUrlEntity = await webOriginDb.DisplayUrl
                    .Where(v => v.Display == display)
                    .SingleAsync();

                webOriginDb.Remove(displayUrlEntity);

                await webOriginDb.SaveChangesAsync();
            }
            catch
            {
                return Results.StatusCode(404);
            }

            return Results.Ok();
        });

        app.MapGet("/v1/display/{*display}",
            [Authorize(Policy = "ApiKey")]
            async (
                [FromServices]WebOriginDbContext webOriginDb,
                HttpRequest httpRequest,
                string display) => 
        {
            using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"Origin::v1/display/display");

            DisplayUrlEntity displayUrlEntity = null;

            try
            {
                displayUrlEntity = await webOriginDb.DisplayUrl
                    .Where(v => v.Display == display)
                    .SingleOrDefaultAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Issue Retrieving Display Url {display}");
            }

            return new DisplayUrlDto()
            {
                Display = display,
                RedirectPath = displayUrlEntity?.RedirectPath,
                QueryString = displayUrlEntity?.QueryString
            };
        });

        app.MapPost("/v1/display/{*redirectPath}",
            [Authorize(Policy = "ApiKey")]
            async (
                [FromServices] WebOriginDbContext webOriginDb,
                HttpRequest httpRequest,
                [FromServices] IHmacService hmacService,
                [FromServices] IQueryStringService queryStringService,
                [FromQuery] string display,
                string redirectPath,
                UrlGenerationDto urlGenerationDto) => 
        {
            using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"Origin::v1/display/redirectPath");

            Log.Debug($"display: {redirectPath}");

            string key = (await webOriginDb.App
                .SingleAsync())
                .SignatureKey;

            string redirectQueryString;

            if (urlGenerationDto.HasSignature) {
                QueryString sans = queryStringService
                    .CreateExcept(httpRequest.QueryString, "signature", "display");

                string redirectSignature = hmacService.Hash(key, $"{redirectPath}{sans}");

                redirectQueryString = sans
                    .Add("signature", redirectSignature)
                    .ToString();
            } else {
                redirectQueryString = httpRequest.QueryString
                    .ToString();
            }
            try
            {
                DisplayUrlEntity displayUrlEntity = new()
                {
                    Display = display,
                    RedirectPath = $"/v1/{redirectPath}",
                    QueryString = redirectQueryString
                };

                webOriginDb.Add(displayUrlEntity);

                await webOriginDb.SaveChangesAsync();
            }
            catch
            {
                display = null;
            }

            return new
            {
                Display = display,
                DisplaySignature = (display is null) ? display : hmacService.Hash(key, $"display/{display}")
            };
        });

        app.MapGet("/v1/uuid",
            [Authorize(Policy = "ApiKey")]
            async (
                [FromServices]WebOriginDbContext webOriginDb,
                HttpRequest httpRequest) => 
        {
            using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"Origin::v1/uuid");

            return (await webOriginDb.UuidUrl
                .OrderBy(v => v.UuidUrlId)
                .ToListAsync())
                .Adapt<IEnumerable<UuidUrlDto>>();
        });

        app.MapDelete("/v1/uuid/{uuid}",
            [Authorize(Policy = "ApiKey")]
            async (
                [FromServices]WebOriginDbContext webOriginDb,
                string uuid) => 
        {
            using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"Origin::v1/uuid/uuid");

            try
            {
                UuidUrlEntity uuidUrlEntity = await webOriginDb.UuidUrl
                    .Where(v => v.Uuid == uuid)
                    .SingleAsync();

                webOriginDb.Remove(uuidUrlEntity);

                await webOriginDb.SaveChangesAsync();
            }
            catch
            {
                return Results.StatusCode(404);
            }

            return Results.Ok();
        });

        app.MapGet("/v1/uuid/{*uuidUrl}",
            [Authorize(Policy = "ApiKey")]
            async (
                [FromServices]WebOriginDbContext webOriginDb,
                HttpRequest httpRequest,
                string uuidUrl) => 
        {
            using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"Origin::v1/uuid/uuidUrl");

            UuidUrlEntity uuidUrlEntity = null;

            try
            {
                uuidUrlEntity = await webOriginDb.UuidUrl
                    .Where(v => v.Uuid == uuidUrl)
                    .SingleOrDefaultAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Issue Retrieving UuidUrl {uuidUrl}");
            }

            return new UuidUrlDto()
            {
                Uuid = uuidUrl,
                RedirectPath = uuidUrlEntity?.RedirectPath,
                QueryString = uuidUrlEntity?.QueryString
            };
        });

        app.MapPost("/v1/uuid/{*redirectPath}",
            [Authorize(Policy = "ApiKey")]
            async (
                [FromServices]WebOriginDbContext webOriginDb,
                HttpRequest httpRequest,
                [FromServices]IHmacService hmacService,
                [FromServices]IQueryStringService queryStringService,
                string redirectPath, UrlGenerationDto urlGenerationDto) => 
        {
            using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"Origin::v1/uuid/redirectPath");

            Log.Debug($"uuid: {redirectPath}");

            int uuidSeed = 0;

            await OneUuidUrlAtAtime.WaitAsync();

            try
            {
                if (DateTime.UtcNow.Year >= 2020) {
                    uuidSeed = (int)((DateTime.UtcNow - new DateTime(2020, 1, 1, 0, 0, 0)).TotalMilliseconds / 100L);		

                    await Task.Delay(10);
                } else {
                    uuidSeed = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                    await Task.Delay(1_000);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Issue Generating Uuid {redirectPath}");
            }
            finally
            {
                OneUuidUrlAtAtime.Release();
            }
                
            Random r = new Random(uuidSeed);
            byte[] guid = new byte[16];
            r.NextBytes(guid);

            string uuid = new Guid(guid).ToString();

            string key = (await webOriginDb.App
                .SingleAsync())
                .SignatureKey;

            string redirectQueryString;

            if (urlGenerationDto.HasSignature) {
                string redirectSignature = hmacService.Hash(key, $"{redirectPath}{httpRequest.QueryString}");

                redirectQueryString = httpRequest.QueryString
                    .Add("signature", redirectSignature)
                    .ToString();
            } else {
                redirectQueryString = httpRequest.QueryString
                    .ToString();
            }

            UuidUrlEntity uuidUrlEntity = new()
            {
                Uuid = uuid,
                RedirectPath = $"/v1/{redirectPath}",
                QueryString = redirectQueryString
            };

            webOriginDb.Add(uuidUrlEntity);

            await webOriginDb.SaveChangesAsync();

            return new
            {
                Uuid = uuid,
                UuidSignature = hmacService.Hash(key, $"uuid/{uuid}")
            };
        });

        app.MapPost("/v1/signature/{signatureType}",
            [Authorize(Policy = "ApiKey")]
            async (
                [FromServices]WebOriginDbContext webOriginDb,
                [FromServices]IHmacService hmacService,
                HttpRequest httpRequest,
                string signatureType) => 
        {
            using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"Origin::v1/signature/{signatureType}");

            string queryString = httpRequest.QueryString.ToString();

            string key = (await webOriginDb.App
                .SingleAsync())
                .SignatureKey;

            Log.Debug($"HmacService Hash: {queryString}");

            return new { Signature = hmacService.Hash(key, $"{signatureType}{queryString}") };
        });

        app.MapPost("/v1/signature/{signatureType}/{*filePath}",
            [Authorize(Policy = "ApiKey")]
            async (
                [FromServices]WebOriginDbContext webOriginDb,
                [FromServices]IHmacService hmacService,
                HttpRequest httpRequest,
                string signatureType,
                string filePath) => 
        {
            using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"Origin::v1/signature/{signatureType}/*");

            string queryString = httpRequest.QueryString.ToString();

            string key = (await webOriginDb.App
                .SingleAsync())
                .SignatureKey;

            Log.Debug($"HmacService Hash: {filePath}{queryString}");

            return new { Signature = hmacService.Hash(key, $"{signatureType}/{filePath}{queryString}") };
        });

        // Start the Server 
        await app.RunAsync();
    }

    // 
}

public class ApiKeyAuthenticationSchemeOptions : AuthenticationSchemeOptions
{
}

public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationSchemeOptions>
{
    WebOriginDbContext WebOriginDb { get; init; }

    IHttpContextAccessor HttpContextAccessor { get; init; }

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder,
        IHttpContextAccessor httpContextAccessor,
        ISystemClock clock) : base(options, logger, encoder, clock)
    {
       HttpContextAccessor = httpContextAccessor;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        HttpContext httpContext = HttpContextAccessor.HttpContext;

        string givenApiKey = httpContext.Request.Headers["ApiKey"];

        if (givenApiKey is null) {
            return Task.FromResult(AuthenticateResult.Fail("Header Not Found."));
        } else {
            Claim[] claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "ApiKey"),
                new Claim(ClaimTypes.Name, "ApiKeyUser")
            };

            ClaimsIdentity claimsIdentity = new(claims, nameof(ApiKeyAuthorizationHandler));

            AuthenticationTicket ticket = new(new ClaimsPrincipal(claimsIdentity), this.Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}

public class ApiKeyAuthorizationRequirement : IAuthorizationRequirement
{
}

public class ApiKeyAuthorizationHandler : AuthorizationHandler<ApiKeyAuthorizationRequirement>
{
   WebOriginDbContext WebOriginDb { get; init; }

   IHttpContextAccessor HttpContextAccessor { get; init; }

   public ApiKeyAuthorizationHandler(
       IHttpContextAccessor httpContextAccessor,
       WebOriginDbContext webOriginDb)
   {
       HttpContextAccessor = httpContextAccessor;

       WebOriginDb = webOriginDb;
   }

    async protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ApiKeyAuthorizationRequirement requirement)
    {
        HttpContext httpContext = HttpContextAccessor.HttpContext;

        string givenApiKey = httpContext.Request.Headers["ApiKey"];

        if (givenApiKey is null) {
            context.Fail();
        } else {
            if ((await WebOriginDb.App.SingleAsync()).ApiKey.Equals(givenApiKey, StringComparison.OrdinalIgnoreCase)) {
                context.Succeed(requirement);
            } else {
                context.Fail();
            }
        }
    }
}
