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

        BarcodeCacheElementEntity barcodeCacheElement = null;

        if (await WebEdgeDb.BarcodeCacheElements
            .Where(v => v.UrlPath == String.Empty)
            .Where(v => v.QueryString == queryString)
            .SingleOrDefaultAsync() is BarcodeCacheElementEntity cacheElement && cacheElement is not null)
        {
            cacheElement.LastAccessedUtc = lastAccessedutc;

            barcodeCacheElement = cacheElement;
        } else {
            barcodeCacheElement = new BarcodeCacheElementEntity
            {
                BarcodeCacheElementId = Int32.Parse(Path.GetFileName(barcodeCachePath)),
                CachePath = barcodeCachePath,
                UrlPath = String.Empty,
                QueryString = queryString,
                FileSizeBytes = new FileInfo(barcodeCachePath).Length,
                LastAccessedUtc = lastAccessedutc
            };

            WebEdgeDb.Add(barcodeCacheElement);
        }

        await WebEdgeDb.SaveChangesAsync();

        return barcodeCacheElement;
    }

    public async Task<ImageCacheElementEntity> InsertImageAsync(string imageCachePath, string imageUrlPath, string queryString = "")
    {
        long lastAccessedutc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        ImageCacheElementEntity imageCacheElement = null;

        if (await WebEdgeDb.ImageCacheElements
            .Where(v => v.UrlPath == imageUrlPath)
            .Where(v => v.QueryString == queryString)
            .SingleOrDefaultAsync() is ImageCacheElementEntity cacheElement && cacheElement is not null)
        {
            cacheElement.LastAccessedUtc = lastAccessedutc;

            imageCacheElement = cacheElement;
        } else {
            imageCacheElement = new ImageCacheElementEntity
            {
                ImageCacheElementId = Int32.Parse(Path.GetFileName(imageCachePath)),
                CachePath = imageCachePath,
                UrlPath = imageUrlPath,
                QueryString = queryString,
                FileSizeBytes = new FileInfo(imageCachePath).Length,
                LastAccessedUtc = lastAccessedutc
            };

            WebEdgeDb.Add(imageCacheElement);
        }
        
        await WebEdgeDb.SaveChangesAsync();

        return imageCacheElement;
    }

    public async Task<S3ImageCacheElementEntity> InsertS3ImageAsync(string s3ImageCachePath, string s3ImageUrlPath, string queryString = "")
    {
        long lastAccessedutc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        S3ImageCacheElementEntity s3ImageCacheElement = null;

        if (await WebEdgeDb.S3ImageCacheElements
            .Where(v => v.UrlPath == s3ImageUrlPath)
            .Where(v => v.QueryString == queryString)
            .SingleOrDefaultAsync() is S3ImageCacheElementEntity cacheElement && cacheElement is not null)
        {
            cacheElement.LastAccessedUtc = lastAccessedutc;

            s3ImageCacheElement = cacheElement;
        } else {
            s3ImageCacheElement = new S3ImageCacheElementEntity
            {
                S3ImageCacheElementId = Int32.Parse(Path.GetFileName(s3ImageCachePath)),
                CachePath = s3ImageCachePath,
                UrlPath = s3ImageUrlPath,
                QueryString = queryString,
                FileSizeBytes = new FileInfo(s3ImageCachePath).Length,
                LastAccessedUtc = lastAccessedutc
            };

            WebEdgeDb.Add(s3ImageCacheElement);
        }

        await WebEdgeDb.SaveChangesAsync();

        return s3ImageCacheElement;
    }

    public async Task<StaticCacheElementEntity> InsertStaticAsync(string staticCachePath, string staticUrlPath, string queryString = "")
    {
        long lastAccessedutc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        StaticCacheElementEntity staticCacheElement = new StaticCacheElementEntity
        {
            StaticCacheElementId = Int32.Parse(Path.GetFileName(staticCachePath)),
            CachePath = staticCachePath,
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
