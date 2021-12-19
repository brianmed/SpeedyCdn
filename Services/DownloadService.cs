using System.Collections.Concurrent;

using Microsoft.EntityFrameworkCore;
using Serilog.Context;

using SpeedyCdn.Server.Entities.Edge;

public interface IDownloadService
{
    Task<S3ImageCacheElementEntity> GetOriginS3ImageAsync(string s3Bucket, string s3Key);
    Task<ImageCacheElementEntity> GetOriginImageAsync(string imagePath);
    Task<StaticCacheElementEntity> GetOriginStaticAsync(string staticUrlPath);
}

public class DownloadService : IDownloadService
{
    WebEdgeDbContext WebEdgeDb { get; init; }

    ICacheElementService CacheElementService { get; init; }

    ICachePathService CachePathService { get; init; }

    IHttpClientFactory HttpClientFactory { get; init; }

    static ConcurrentDictionary<string, bool> InFlightS3ImagePaths = new();
    static ConcurrentDictionary<string, bool> InFlightImagePaths = new();
    static ConcurrentDictionary<string, bool> InFlightStaticPaths = new();

    public DownloadService(
            WebEdgeDbContext webEdgeDb,
            ICachePathService cachePathService,
            ICacheElementService cacheElementService,
            IHttpClientFactory httpClientFactory)
    {
        CacheElementService = cacheElementService;

        CachePathService = cachePathService;

        WebEdgeDb = webEdgeDb;

        HttpClientFactory = httpClientFactory;
    }

    async public Task<S3ImageCacheElementEntity> GetOriginS3ImageAsync(string s3Bucket, string s3Key)
    {
        using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"{nameof(DownloadService)}.{nameof(GetOriginS3ImageAsync)}");

        string url = $"{ConfigCtx.Options.EdgeOriginUrl}/v1/s3/images/{s3Bucket}/{s3Key}";

        S3ImageCacheElementEntity cacheElement = null;

        try
        {
            SpinWait sw = new SpinWait();

            while (InFlightS3ImagePaths.TryAdd($"{s3Bucket}/{s3Key}", true) is false)
            {
                sw.SpinOnce();
            }

            string cachePathAbsolute = CachePathService.CachePath(
                ConfigCtx.Options.EdgeCacheS3ImagesDirectory,
                new[] { $"{s3Bucket}/{s3Key}", String.Empty });

            Directory.CreateDirectory(Path.GetDirectoryName(cachePathAbsolute));

            cacheElement = await WebEdgeDb.S3ImageCacheElements
                .Where(v => v.UrlPath == $"{s3Bucket}/{s3Key}")
                .Where(v => v.QueryString == String.Empty)
                .SingleOrDefaultAsync();

            if (cacheElement is not null && File.Exists(cachePathAbsolute)) {
                if (new FileInfo(cachePathAbsolute).Length > 0) {
                    Log.Debug($"Cache Hit: {s3Bucket}/{s3Key} - {cacheElement.S3ImageCacheElementId}");

                    cacheElement.LastAccessedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    await WebEdgeDb.SaveChangesAsync();

                    return cacheElement;
                } else {
                    Log.Debug($"Zero Byte Cache File: {s3Bucket}/{s3Key}");
                }
            } else {
                Log.Debug($"No Cache File Found: {s3Bucket}/{s3Key}");
            }

            await DownloadAsync(url, cachePathAbsolute);

            cacheElement = await CacheElementService.InsertS3ImageAsync(cachePathAbsolute, $"{s3Bucket}/{s3Key}");
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Issue Processing {s3Bucket}/{s3Key}");
        }
        finally
        {
            if (InFlightS3ImagePaths.TryRemove($"{s3Bucket}/{s3Key}", out bool whence) is false) {
                Log.Error($"Issue Removing {s3Bucket}/{s3Key}");
            }
        }

        return cacheElement;
    }

    async public Task<ImageCacheElementEntity> GetOriginImageAsync(string imageUrlPath)
    {
        using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"{nameof(DownloadService)}.{nameof(GetOriginImageAsync)}");

        string url = $"{ConfigCtx.Options.EdgeOriginUrl}/v1/images/{imageUrlPath}";

        ImageCacheElementEntity cacheElement = null;

        try
        {
            SpinWait sw = new SpinWait();

            while (InFlightImagePaths.TryAdd(imageUrlPath, true) is false)
            {
                sw.SpinOnce();
            }

            string cachePathAbsolute = CachePathService.CachePath(
                ConfigCtx.Options.EdgeCacheImagesDirectory,
                new[] { imageUrlPath, String.Empty });

            Directory.CreateDirectory(Path.GetDirectoryName(cachePathAbsolute));

            cacheElement = await WebEdgeDb.ImageCacheElements
                .Where(v => v.UrlPath == imageUrlPath)
                .Where(v => v.QueryString == String.Empty)
                .SingleOrDefaultAsync();

            if (cacheElement is not null && File.Exists(cachePathAbsolute)) {
                if (new FileInfo(cachePathAbsolute).Length > 0) {
                    Log.Debug($"Cache Hit: {imageUrlPath} - {cacheElement.ImageCacheElementId}");

                    cacheElement.LastAccessedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    await WebEdgeDb.SaveChangesAsync();

                    return cacheElement;
                } else {
                    Log.Debug($"Zero Byte Cache File: {imageUrlPath}");
                }
            } else {
                Log.Debug($"No Cache File Found: {imageUrlPath}");
            }

            await DownloadAsync(url, cachePathAbsolute);

            cacheElement = await CacheElementService.InsertImageAsync(cachePathAbsolute, imageUrlPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Issue Processing {url}");
        }
        finally
        {
            if (InFlightImagePaths.TryRemove(imageUrlPath, out bool whence) is false) {
                Log.Error($"Issue Removing {imageUrlPath}");
            }
        }

        return cacheElement;
    }

    async public Task<StaticCacheElementEntity> GetOriginStaticAsync(string staticUrlPath)
    {
        using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"{nameof(DownloadService)}.{nameof(GetOriginStaticAsync)}");

        string url = $"{ConfigCtx.Options.EdgeOriginUrl}/v1/static/{staticUrlPath}";

        StaticCacheElementEntity cacheElement = null;

        try
        {
            SpinWait sw = new SpinWait();

            while (InFlightStaticPaths.TryAdd(staticUrlPath, true) is false)
            {
                sw.SpinOnce();
            }

            string cachePathAbsolute = CachePathService.CachePath(
                ConfigCtx.Options.EdgeCacheImagesDirectory,
                new[] { staticUrlPath, String.Empty });

            Directory.CreateDirectory(Path.GetDirectoryName(cachePathAbsolute));

            cacheElement = await WebEdgeDb.StaticCacheElements
                .Where(v => v.UrlPath == staticUrlPath)
                .Where(v => v.QueryString == String.Empty)
                .SingleOrDefaultAsync();

            if (cacheElement is not null && File.Exists(cachePathAbsolute)) {
                if (new FileInfo(cachePathAbsolute).Length > 0) {
                    Log.Debug($"Cache Hit: {staticUrlPath}");

                    cacheElement.LastAccessedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    await WebEdgeDb.SaveChangesAsync();

                    return cacheElement;
                } else {
                    Log.Debug($"Zero Byte Cache File: {staticUrlPath}");
                }
            } else {
                Log.Debug($"No Cache File Found: {staticUrlPath}");
            }

            await DownloadAsync(url, cachePathAbsolute);

            cacheElement = await CacheElementService.InsertStaticAsync(cachePathAbsolute, staticUrlPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Issue Processing {staticUrlPath}");
        }
        finally
        {
            if (InFlightStaticPaths.TryRemove(staticUrlPath, out bool whence) is false) {
                Log.Error($"Issue Removing {staticUrlPath}");
            }
        }

        return cacheElement;
    }

    async private Task DownloadAsync(string url, string absolutePath)
    {
        Log.Debug($"Downloading: {url}");

        HttpClient httpClient = HttpClientFactory.CreateClient();

        if (ConfigCtx.Options.EdgeOriginApiKey is not null) {
            Log.Debug($"Adding ApiKey to Headers");
            httpClient.DefaultRequestHeaders.Add("ApiKey", ConfigCtx.Options.EdgeOriginApiKey);
        }

        using (FileStream fs = new FileStream(absolutePath, FileMode.OpenOrCreate))
        using (HttpResponseMessage response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
        using (Stream streamToReadFrom = await response.Content.ReadAsStreamAsync())
        {
            if (response.IsSuccessStatusCode) {
                Log.Debug($"Saving: {fs.Name}");

                await streamToReadFrom.CopyToAsync(fs);
            } else {
                Log.Error($"Issue: {await response.Content.ReadAsStreamAsync()}");
            }
        }
    }
}
