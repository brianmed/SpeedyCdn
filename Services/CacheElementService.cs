using Microsoft.EntityFrameworkCore;

using SpeedyCdn.Server.Entities.Edge;

public interface ICacheElementService
{
    Task<BarcodeCacheElementEntity> InsertBarcodeAsync(string barcodeCachePath, string queryString);
    Task<ImageCacheElementEntity> InsertImageAsync(string imageCachePath, string imageUrlPath, string queryString = "");
    Task<S3ImageCacheElementEntity> InsertS3ImageAsync(string s3ImageCachePath, string s3ImagePath, string queryString = "");
    Task<StaticCacheElementEntity> InsertStaticAsync(string staticCachePath, string staticUrlPath, string queryString = "");
}

public class CacheElementService : ICacheElementService
{
    public WebEdgeDbContext WebEdgeDb { get; init; }

    public CacheElementService(WebEdgeDbContext webEdgeDb)
    {
        WebEdgeDb = webEdgeDb;
    }

    public async Task<BarcodeCacheElementEntity> InsertBarcodeAsync(string barcodeCachePath, string queryString)
    {
        long lastAccessedutc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long expireUtc = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeSeconds();

        BarcodeCacheElementEntity barcodeCacheElement = new BarcodeCacheElementEntity
        {
            UrlPath = String.Empty,
            QueryString = queryString,
            FileSizeBytes = new FileInfo(barcodeCachePath).Length,
            LastAccessedUtc = lastAccessedutc
        };

        WebEdgeDb.Add(barcodeCacheElement);
        await WebEdgeDb.SaveChangesAsync();

        return barcodeCacheElement;
    }

    public async Task<ImageCacheElementEntity> InsertImageAsync(string imageCachePath, string imageUrlPath, string queryString = "")
    {
        long lastAccessedutc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long expireUtc = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeSeconds();
        
        ImageCacheElementEntity imageCacheElement = new ImageCacheElementEntity
        {
            UrlPath = imageUrlPath,
            QueryString = queryString,
            FileSizeBytes = new FileInfo(imageCachePath).Length,
            LastAccessedUtc = lastAccessedutc
        };

        WebEdgeDb.Add(imageCacheElement);
        await WebEdgeDb.SaveChangesAsync();

        return imageCacheElement;
    }

    public async Task<S3ImageCacheElementEntity> InsertS3ImageAsync(string s3ImageCachePath, string s3ImageUrlPath, string queryString = "")
    {
        long lastAccessedutc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long expireUtc = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeSeconds();

        S3ImageCacheElementEntity s3ImageCacheElement = new S3ImageCacheElementEntity
        {
            UrlPath = s3ImageUrlPath,
            QueryString = queryString,
            FileSizeBytes = new FileInfo(s3ImageCachePath).Length,
            LastAccessedUtc = lastAccessedutc
        };

        WebEdgeDb.Add(s3ImageCacheElement);
        await WebEdgeDb.SaveChangesAsync();

        return s3ImageCacheElement;
    }

    public async Task<StaticCacheElementEntity> InsertStaticAsync(string staticCachePath, string staticUrlPath, string queryString = "")
    {
        long lastAccessedutc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long expireUtc = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeSeconds();

        StaticCacheElementEntity staticCacheElement = new StaticCacheElementEntity
        {
            UrlPath = staticUrlPath,
            QueryString = queryString,
            FileSizeBytes = new FileInfo(staticCachePath).Length,
            LastAccessedUtc = lastAccessedutc
        };

        WebEdgeDb.Add(staticCacheElement);
        await WebEdgeDb.SaveChangesAsync();

        return staticCacheElement;
    }
}
