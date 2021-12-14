using System.Collections.Concurrent;

using Serilog.Context;

public interface IDownloadService
{
    Task GetS3ImageAsync(string s3Bucket, string s3Key, string cachePath);
    Task GetImageAsync(string imagePath, string cachePath);
    Task GetStaticAsync(string staticPath, string cachePath);
}

public class DownloadService : IDownloadService
{
    IHttpClientFactory HttpClientFactory { get; init; }

    static ConcurrentDictionary<string, bool> InFlightS3ImagePaths = new();
    static ConcurrentDictionary<string, bool> InFlightImagePaths = new();
    static ConcurrentDictionary<string, bool> InFlightStaticPaths = new();

    public DownloadService(IHttpClientFactory httpClientFactory)
    {
        HttpClientFactory = httpClientFactory;
    }

    async public Task GetS3ImageAsync(string s3Bucket, string s3Key, string _cachePath)
    {
        using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"{nameof(DownloadService)}.{nameof(GetS3ImageAsync)}");

        string url = $"{ConfigCtx.Options.EdgeOriginUrl}/v1/s3/images/{s3Bucket}/{s3Key}";

        SpinWait sw = new SpinWait();

        while (InFlightS3ImagePaths.TryAdd($"{s3Bucket}/{s3Key}", true) is false)
        {
            sw.SpinOnce();
        }

        try
        {
            string cachePath = Path.Combine(ConfigCtx.Options.EdgeCacheS3ImagesDirectory, _cachePath);

            if (File.Exists(cachePath)) {
                if (new FileInfo(cachePath).Length > 0) {
                    Log.Debug($"Cache Hit: {url}");

                    return;
                } else {
                    Log.Debug($"Zero Byte Cache File: {url}");
                }
            } else {
                Log.Debug($"No Cache File Found: {url}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(cachePath));

            await DownloadAsync(url, cachePath);
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
    }

    async public Task GetImageAsync(string imagePath, string _cachePath)
    {
        using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"{nameof(DownloadService)}.{nameof(GetImageAsync)}");

        string url = $"{ConfigCtx.Options.EdgeOriginUrl}/v1/images/{imagePath}";

        SpinWait sw = new SpinWait();

        while (InFlightImagePaths.TryAdd(imagePath, true) is false)
        {
            sw.SpinOnce();
        }

        try
        {
            string cachePath = Path.Combine(ConfigCtx.Options.EdgeCacheImagesDirectory, _cachePath);

            if (File.Exists(cachePath)) {
                if (new FileInfo(cachePath).Length > 0) {
                    Log.Debug($"Cache Hit: {url}");

                    return;
                } else {
                    Log.Debug($"Zero Byte Cache File: {url}");
                }
            } else {
                Log.Debug($"No Cache File Found: {url}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(cachePath));

            await DownloadAsync(url, cachePath);
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
    }

    async public Task GetStaticAsync(string staticPath, string _cachePath)
    {
        using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"{nameof(DownloadService)}.{nameof(GetStaticAsync)}");

        string url = $"{ConfigCtx.Options.EdgeOriginUrl}/v1/static/{staticPath}";

        SpinWait sw = new SpinWait();

        while (InFlightStaticPaths.TryAdd(staticPath, true) is false)
        {
            sw.SpinOnce();
        }

        try
        {
            string cachePath = Path.Combine(ConfigCtx.Options.EdgeCacheStaticDirectory, _cachePath);

            if (File.Exists(cachePath)) {
                if (new FileInfo(cachePath).Length > 0) {
                    Log.Debug($"Cache Hit: {url}");

                    return;
                } else {
                    Log.Debug($"Zero Byte Cache File: {url}");
                }
            } else {
                Log.Debug($"No Cache File Found: {url}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(cachePath));

            await DownloadAsync(url, cachePath);
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
