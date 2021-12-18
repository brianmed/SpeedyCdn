using Microsoft.EntityFrameworkCore;

using SpeedyCdn.Server.Entities.Edge;

public interface ICacheElementService
{
    Task<BarcodeCacheElementEntity> InsertBarcodeAsync(string cachePath);
    Task<ImageCacheElementEntity> InsertImageAsync(string cachePath);
    Task<S3ImageCacheElementEntity> InsertS3ImageAsync(string cachePath);
    Task<StaticCacheElementEntity> InsertStaticAsync(string cachePath);
}

public class CacheElementService : ICacheElementService
{
    public WebEdgeDbContext WebEdgeDb { get; init; }

    public CacheElementService(WebEdgeDbContext webEdgeDb)
    {
        WebEdgeDb = webEdgeDb;
    }

    public async Task<BarcodeCacheElementEntity> InsertBarcodeAsync(string cachePath)
    {
        long lastAccessedutc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long expireUtc = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeSeconds();

        BarcodeCacheElementEntity barcodeCacheElement = new BarcodeCacheElementEntity
        {
            FileSizeBytes = new FileInfo(cachePath).Length,
            LastAccessedUtc = lastAccessedutc,
            ExpireUtc = expireUtc
        };

        WebEdgeDb.Add(barcodeCacheElement);
        await WebEdgeDb.SaveChangesAsync();

        barcodeCacheElement.CachePath = $"{cachePath}.{barcodeCacheElement.BarcodeCacheElementId}";
        await WebEdgeDb.SaveChangesAsync();

        Log.Debug($"Moving: {cachePath} -> {cachePath}.{barcodeCacheElement.BarcodeCacheElementId}");
        File.Move(cachePath, $"{cachePath}.{barcodeCacheElement.BarcodeCacheElementId}");

        return barcodeCacheElement;
    }

    public async Task<ImageCacheElementEntity> InsertImageAsync(string imageCachePath)
    {
        long lastAccessedutc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long expireUtc = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeSeconds();

        ImageCacheElementEntity imageCacheElement = new ImageCacheElementEntity
        {
            FileSizeBytes = new FileInfo(imageCachePath).Length,
            LastAccessedUtc = lastAccessedutc,
            ExpireUtc = expireUtc
        };

        WebEdgeDb.Add(imageCacheElement);
        await WebEdgeDb.SaveChangesAsync();

        imageCacheElement.CachePath = $"{imageCachePath}.{imageCacheElement.ImageCacheElementId}";
        await WebEdgeDb.SaveChangesAsync();

        Log.Debug($"Moving: {imageCachePath} -> {imageCachePath}.{imageCacheElement.ImageCacheElementId}");
        File.Move(imageCachePath, $"{imageCachePath}.{imageCacheElement.ImageCacheElementId}");

        return imageCacheElement;
    }

    public async Task<S3ImageCacheElementEntity> InsertS3ImageAsync(string s3ImageCachePath)
    {
        long lastAccessedutc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long expireUtc = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeSeconds();

        S3ImageCacheElementEntity s3ImageCacheElement = new S3ImageCacheElementEntity
        {
            FileSizeBytes = new FileInfo(s3ImageCachePath).Length,
            LastAccessedUtc = lastAccessedutc,
            ExpireUtc = expireUtc
        };

        WebEdgeDb.Add(s3ImageCacheElement);
        await WebEdgeDb.SaveChangesAsync();

        s3ImageCacheElement.CachePath = $"{s3ImageCachePath}.{s3ImageCacheElement.S3ImageCacheElementId}";
        await WebEdgeDb.SaveChangesAsync();

        Log.Debug($"Moving: {s3ImageCachePath} -> {s3ImageCachePath}.{s3ImageCacheElement.S3ImageCacheElementId}");
        File.Move(s3ImageCachePath, $"{s3ImageCachePath}.{s3ImageCacheElement.S3ImageCacheElementId}");

        return s3ImageCacheElement;
    }

    public async Task<StaticCacheElementEntity> InsertStaticAsync(string staticCachePath)
    {
        long lastAccessedutc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long expireUtc = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeSeconds();

        StaticCacheElementEntity staticCacheElement = new StaticCacheElementEntity
        {
            FileSizeBytes = new FileInfo(staticCachePath).Length,
            LastAccessedUtc = lastAccessedutc,
            ExpireUtc = expireUtc
        };

        WebEdgeDb.Add(staticCacheElement);
        await WebEdgeDb.SaveChangesAsync();

        staticCacheElement.CachePath = $"{staticCachePath}.{staticCacheElement.StaticCacheElementId}";
        await WebEdgeDb.SaveChangesAsync();

        Log.Debug($"Moving: {staticCachePath} -> {staticCachePath}.{staticCacheElement.StaticCacheElementId}");
        File.Move(staticCachePath, $"{staticCachePath}.{staticCacheElement.StaticCacheElementId}");

        return staticCacheElement;
    }
}
