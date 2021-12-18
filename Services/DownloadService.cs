using System.Collections.Concurrent;

using Microsoft.EntityFrameworkCore;
using Serilog.Context;

using SpeedyCdn.Server.Entities.Edge;

public interface IDownloadService
{
    Task<S3ImageCacheElementEntity> GetS3ImageAsync(string s3Bucket, string s3Key, string cachePath);
    Task<ImageCacheElementEntity> GetImageAsync(string imagePath, string cachePath);
    Task<StaticCacheElementEntity> GetStaticAsync(string staticPath, string cachePath);
}

public class DownloadService : IDownloadService
{
    WebEdgeDbContext WebEdgeDb { get; init; }

    ICacheElementService CacheElementService { get; init; }

    IHttpClientFactory HttpClientFactory { get; init; }

    static ConcurrentDictionary<string, bool> InFlightS3ImagePaths = new();
    static ConcurrentDictionary<string, bool> InFlightImagePaths = new();
    static ConcurrentDictionary<string, bool> InFlightStaticPaths = new();

    public DownloadService(
            WebEdgeDbContext webEdgeDb,
            ICacheElementService cacheElementService,
            IHttpClientFactory httpClientFactory)
    {
        CacheElementService = cacheElementService;

        WebEdgeDb = webEdgeDb;

        HttpClientFactory = httpClientFactory;
    }

    async public Task<S3ImageCacheElementEntity> GetS3ImageAsync(string s3Bucket, string s3Key, string cachePathRelativeNoId)
    {
        using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"{nameof(DownloadService)}.{nameof(GetS3ImageAsync)}");

        string url = $"{ConfigCtx.Options.EdgeOriginUrl}/v1/s3/images/{s3Bucket}/{s3Key}";

        string cachePathAbsNoId = Path.Combine(ConfigCtx.Options.EdgeCacheS3ImagesDirectory, cachePathRelativeNoId);
        S3ImageCacheElementEntity cacheElement = null;

        try
        {
            SpinWait sw = new SpinWait();

            while (InFlightS3ImagePaths.TryAdd($"{s3Bucket}/{s3Key}", true) is false)
            {
                sw.SpinOnce();
            }

            Directory.CreateDirectory(Path.GetDirectoryName(cachePathAbsNoId));

            string cachePathWithId = Directory.EnumerateFiles(
                Path.GetDirectoryName(cachePathAbsNoId),
                    $"{Path.GetFileName(cachePathAbsNoId)}.*", SearchOption.TopDirectoryOnly)
                .SingleOrDefault();

            if (cachePathWithId is not null && File.Exists(cachePathWithId)) {
                if (new FileInfo(cachePathWithId).Length > 0) {
                    Log.Debug($"Cache Hit: {url}");

                    long s3ImageCacheElementId = long.Parse(Path.GetExtension(Path.GetFileName(cachePathWithId)).Replace(".", String.Empty));

                    cacheElement = await WebEdgeDb.S3ImageCacheElements
                        .Where(v => v.S3ImageCacheElementId == s3ImageCacheElementId)
                        .SingleAsync();

                    return cacheElement;
                } else {
                    Log.Debug($"Zero Byte Cache File: {url}");
                }
            } else {
                Log.Debug($"No Cache File Found: {url}");
            }

            await DownloadAsync(url, cachePathAbsNoId);

            cacheElement = await CacheElementService.InsertS3ImageAsync(cachePathAbsNoId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Issue Processing {url}");
        }
        finally
        {
            if (InFlightS3ImagePaths.TryRemove($"{s3Bucket}/{s3Key}", out bool whence) is false) {
                Log.Error($"Issue Removing {s3Bucket}/{s3Key}");
            }
        }

        return cacheElement;
    }

    async public Task<ImageCacheElementEntity> GetImageAsync(string imagePath, string cachePathRelativeNoId)
    {
        using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"{nameof(DownloadService)}.{nameof(GetImageAsync)}");

        string url = $"{ConfigCtx.Options.EdgeOriginUrl}/v1/images/{imagePath}";

        string cachePathAbsNoId = Path.Combine(ConfigCtx.Options.EdgeCacheImagesDirectory, cachePathRelativeNoId);
        ImageCacheElementEntity cacheElement = null;

        try
        {
            SpinWait sw = new SpinWait();

            while (InFlightImagePaths.TryAdd(imagePath, true) is false)
            {
                sw.SpinOnce();
            }

            Directory.CreateDirectory(Path.GetDirectoryName(cachePathAbsNoId));

            string cachePathWithId = Directory.EnumerateFiles(
                Path.GetDirectoryName(cachePathAbsNoId),
                    $"{Path.GetFileName(cachePathAbsNoId)}.*", SearchOption.TopDirectoryOnly)
                .SingleOrDefault();

            if (cachePathWithId is not null && File.Exists(cachePathWithId)) {
                if (new FileInfo(cachePathWithId).Length > 0) {
                    Log.Debug($"Cache Hit: {url}");
                    
                    long imageCacheElementId = long.Parse(Path.GetExtension(Path.GetFileName(cachePathWithId)).Replace(".", String.Empty));

                    cacheElement = await WebEdgeDb.ImageCacheElements
                        .Where(v => v.ImageCacheElementId == imageCacheElementId)
                        .SingleAsync();

                    return cacheElement;
                } else {
                    Log.Debug($"Zero Byte Cache File: {url}");
                }
            } else {
                Log.Debug($"No Cache File Found: {url}");
            }

            await DownloadAsync(url, cachePathAbsNoId);

            cacheElement = await CacheElementService.InsertImageAsync(cachePathAbsNoId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Issue Processing {url}");
        }
        finally
        {
            if (InFlightImagePaths.TryRemove(imagePath, out bool whence) is false) {
                Log.Error($"Issue Removing {imagePath}");
            }
        }

        return cacheElement;
    }

    async public Task<StaticCacheElementEntity> GetStaticAsync(string staticPath, string cachePathRelativeNoId)
    {
        using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"{nameof(DownloadService)}.{nameof(GetStaticAsync)}");

        string url = $"{ConfigCtx.Options.EdgeOriginUrl}/v1/static/{staticPath}";

        string cachePathAbsNoId = Path.Combine(ConfigCtx.Options.EdgeCacheStaticDirectory, cachePathRelativeNoId);
        StaticCacheElementEntity cacheElement = null;

        try
        {
            SpinWait sw = new SpinWait();

            while (InFlightStaticPaths.TryAdd(staticPath, true) is false)
            {
                sw.SpinOnce();
            }

            Directory.CreateDirectory(Path.GetDirectoryName(cachePathAbsNoId));

            string cachePathWithId = Directory.EnumerateFiles(
                Path.GetDirectoryName(cachePathAbsNoId),
                    $"{Path.GetFileName(cachePathAbsNoId)}.*", SearchOption.TopDirectoryOnly)
                .SingleOrDefault();

            if (cachePathWithId is not null && File.Exists(cachePathWithId)) {
                if (new FileInfo(cachePathWithId).Length > 0) {
                    Log.Debug($"Cache Hit: {url}");

                    long staticCacheElementId = long.Parse(Path.GetExtension(Path.GetFileName(cachePathWithId)).Replace(".", String.Empty));

                    cacheElement = await WebEdgeDb.StaticCacheElements
                        .Where(v => v.StaticCacheElementId == staticCacheElementId)
                        .SingleAsync();

                    return cacheElement;
                } else {
                    Log.Debug($"Zero Byte Cache File: {url}");
                }
            } else {
                Log.Debug($"No Cache File Found: {url}");
            }

            await DownloadAsync(url, cachePathAbsNoId);

            cacheElement = await CacheElementService.InsertStaticAsync(cachePathAbsNoId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Issue Processing {url}");
        }
        finally
        {
            if (InFlightStaticPaths.TryRemove(staticPath, out bool whence) is false) {
                Log.Error($"Issue Removing {staticPath}");
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
