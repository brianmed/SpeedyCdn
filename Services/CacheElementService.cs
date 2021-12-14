using Microsoft.EntityFrameworkCore;

using SpeedyCdn.Server.Entities.Edge;

public interface ICacheElementService
{
    Task<BarcodeCacheElementEntity> UpsertBarcodeAsync(string originalPath);
    Task<ImageCacheElementEntity> UpsertImageAsync(string originalImagePath, string modifiedImagePath);
    Task<S3ImageCacheElementEntity> UpsertS3ImageAsync(string originalImagePath, string modifiedImagePath);
    Task<StaticCacheElementEntity> UpsertStaticAsync(string originalPath);
}

public class CacheElementService : ICacheElementService
{
    public WebEdgeDbContext WebEdgeDb { get; init; }

    public CacheElementService(WebEdgeDbContext webEdgeDb)
    {
        WebEdgeDb = webEdgeDb;
    }

    public async Task<BarcodeCacheElementEntity> UpsertBarcodeAsync(string originalPath)
    {
        TimeSpan oneHour = TimeSpan.FromHours(1);
        TimeSpan oneWeek = TimeSpan.FromDays(7);

        return (await WebEdgeDb.BarcodeCacheElements.FromSqlInterpolated($@"
                INSERT INTO BarcodeCacheElements ( 
                    CachePath,
                    FileSizeBytes,
                    LastAccessedUtc,
                    ExpireUtc
                ) VALUES (
                    {originalPath},
                    {new FileInfo(Path.Combine(ConfigCtx.Options.EdgeCacheBarcodesDirectory, originalPath)).Length},
                    strftime('%s', 'now') - (strftime('%s', 'now') % {oneHour.TotalSeconds}),
                    strftime('%s', 'now') + {oneWeek.TotalSeconds}
                )
                
                ON CONFLICT (CachePath)
                DO UPDATE
                    SET LastAccessedUtc = strftime('%s', 'now') - (strftime('%s', 'now') % {oneHour.TotalSeconds}),
                        ExpireUtc = strftime('%s', 'now') + {oneWeek.TotalSeconds},
                        Updated = CURRENT_TIMESTAMP
                    WHERE CachePath = {originalPath}
                
                RETURNING *;
            ")
            .ToListAsync())
            .Single();
    }

    public async Task<ImageCacheElementEntity> UpsertImageAsync(string originalImagePath, string modifiedImagePath)
    {
        TimeSpan oneHour = TimeSpan.FromHours(1);
        TimeSpan oneWeek = TimeSpan.FromDays(7);

        return (await WebEdgeDb.ImageCacheElements.FromSqlInterpolated($@"
                INSERT OR IGNORE INTO ImageCacheElements ( 
                    CachePath,
                    FileSizeBytes,
                    LastAccessedUtc,
                    ExpireUtc
                ) VALUES (
                    {originalImagePath},
                    {new FileInfo(Path.Combine(ConfigCtx.Options.EdgeCacheImagesDirectory, originalImagePath)).Length},
                    strftime('%s', 'now') - (strftime('%s', 'now') % {oneHour.TotalSeconds}),
                    strftime('%s', 'now') + {oneWeek.TotalSeconds}
                );
                
                INSERT INTO ImageCacheElements ( 
                    CachePath,
                    FileSizeBytes,
                    LastAccessedUtc,
                    ExpireUtc
                ) VALUES (
                    {modifiedImagePath},
                    {new FileInfo(Path.Combine(ConfigCtx.Options.EdgeCacheImagesDirectory, modifiedImagePath)).Length},
                    strftime('%s', 'now') - (strftime('%s', 'now') % {oneHour.TotalSeconds}),
                    strftime('%s', 'now') + {oneWeek.TotalSeconds}
                )
                
                ON CONFLICT (CachePath)
                DO UPDATE
                    SET LastAccessedUtc = strftime('%s', 'now') - (strftime('%s', 'now') % {oneHour.TotalSeconds}),
                        ExpireUtc = strftime('%s', 'now') + {oneWeek.TotalSeconds},
                        Updated = CURRENT_TIMESTAMP
                    WHERE CachePath = {modifiedImagePath}
                
                RETURNING *;
            ")
            .ToListAsync())
            .Single();
    }

    public async Task<S3ImageCacheElementEntity> UpsertS3ImageAsync(string originalImagePath, string modifiedImagePath)
    {
        TimeSpan oneHour = TimeSpan.FromHours(1);
        TimeSpan oneWeek = TimeSpan.FromDays(7);

        return (await WebEdgeDb.S3ImageCacheElements.FromSqlInterpolated($@"
                INSERT OR IGNORE INTO S3ImageCacheElements ( 
                    CachePath,
                    FileSizeBytes,
                    LastAccessedUtc,
                    ExpireUtc
                ) VALUES (
                    {originalImagePath},
                    {new FileInfo(Path.Combine(ConfigCtx.Options.EdgeCacheS3ImagesDirectory, originalImagePath)).Length},
                    strftime('%s', 'now') - (strftime('%s', 'now') % {oneHour.TotalSeconds}),
                    strftime('%s', 'now') + {oneWeek.TotalSeconds}
                );
                
                INSERT INTO S3ImageCacheElements ( 
                    CachePath,
                    FileSizeBytes,
                    LastAccessedUtc,
                    ExpireUtc
                ) VALUES (
                    {modifiedImagePath},
                    {new FileInfo(Path.Combine(ConfigCtx.Options.EdgeCacheS3ImagesDirectory, modifiedImagePath)).Length},
                    strftime('%s', 'now') - (strftime('%s', 'now') % {oneHour.TotalSeconds}),
                    strftime('%s', 'now') + {oneWeek.TotalSeconds}
                )
                
                ON CONFLICT (CachePath)
                DO UPDATE
                    SET LastAccessedUtc = strftime('%s', 'now') - (strftime('%s', 'now') % {oneHour.TotalSeconds}),
                        ExpireUtc = strftime('%s', 'now') + {oneWeek.TotalSeconds},
                        Updated = CURRENT_TIMESTAMP
                    WHERE CachePath = {modifiedImagePath}
                
                RETURNING *;
            ")
            .ToListAsync())
            .Single();
    }

    public async Task<StaticCacheElementEntity> UpsertStaticAsync(string originalPath)
    {
        TimeSpan oneHour = TimeSpan.FromHours(1);
        TimeSpan oneWeek = TimeSpan.FromDays(7);

        return (await WebEdgeDb.StaticCacheElements.FromSqlInterpolated($@"
                INSERT INTO StaticCacheElements ( 
                    CachePath,
                    FileSizeBytes,
                    LastAccessedUtc,
                    ExpireUtc
                ) VALUES (
                    {originalPath},
                    {new FileInfo(Path.Combine(ConfigCtx.Options.EdgeCacheStaticDirectory, originalPath)).Length},
                    strftime('%s', 'now') - (strftime('%s', 'now') % {oneHour.TotalSeconds}),
                    strftime('%s', 'now') + {oneWeek.TotalSeconds}
                )
                
                ON CONFLICT (CachePath)
                DO UPDATE
                    SET LastAccessedUtc = strftime('%s', 'now') - (strftime('%s', 'now') % {oneHour.TotalSeconds}),
                        ExpireUtc = strftime('%s', 'now') + {oneWeek.TotalSeconds},
                        Updated = CURRENT_TIMESTAMP
                    WHERE CachePath = {originalPath}
                
                RETURNING *;
            ")
            .ToListAsync())
            .Single();
    }
}
