using System.Text.Json;
using System.Threading;

using Microsoft.EntityFrameworkCore;

using SpeedyCdn.Enums;
using SpeedyCdn.Server.DbContexts;
using SpeedyCdn.Server.Entities.Edge;

using Serilog.Context;

public class EdgePruneCacheHostedService : IHostedService, IDisposable
{
    private PeriodicTimer PeriodicTimer = null!;

    private IServiceScopeFactory ScopeFactory { get; init; }

    public EdgePruneCacheHostedService(IServiceScopeFactory scopeFactory)
    {
        ScopeFactory = scopeFactory;
    }

    public Task StartAsync(CancellationToken stoppingToken)
    {
        TimeSpan fourHours = TimeSpan.FromHours(4);
        PeriodicTimer = new(fourHours);

        Task.Run(async () => await DoWorkAsync());

        return Task.CompletedTask;
    }

    async private Task DoWorkAsync()
    {
        using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", "Edge::EdgePruneCacheHostedService");

        while (await PeriodicTimer.WaitForNextTickAsync())
        {
            using IServiceScope scope = ScopeFactory.CreateScope();

            WebEdgeDbContext webEdgeDb = scope.ServiceProvider.GetRequiredService<WebEdgeDbContext>();

            ICachePathService cachePathService = scope.ServiceProvider.GetRequiredService<ICachePathService>();

            // TODO: so much code.  Is there a better way..
            long utcNowExpireTimeUnixEpoch = DateTimeOffset.UtcNow
                .AddSeconds(-1 * Math.Abs(ConfigCtx.Options.EdgeCacheExpireSeconds))
                .ToUnixTimeSeconds();

            try
            {
                long cacheSizeInBytes = await webEdgeDb.ImageCacheElements
                    .Select(v => v.FileSizeBytes)
                    .SumAsync();

                if (cacheSizeInBytes <= ConfigCtx.Options.EdgeImageCacheInBytes) {
                    Log.Debug($"ImageCache cacheSizeInBytes: {cacheSizeInBytes} <= {ConfigCtx.Options.EdgeImageCacheInBytes}.. skipping");
                } else {
                    long minId = (await webEdgeDb.ImageCacheElements
                        .Where(v => v.LastAccessedUtc < utcNowExpireTimeUnixEpoch)
                        .OrderBy(v => v.LastAccessedUtc)
                        .Select(v => v.ImageCacheElementId)
                        .ToListAsync())
                        .DefaultIfEmpty(0)
                        .Min();

                    long maxId = (await webEdgeDb.ImageCacheElements
                        .Where(v => v.LastAccessedUtc < utcNowExpireTimeUnixEpoch)
                        .OrderBy(v => v.LastAccessedUtc)
                        .Select(v => v.ImageCacheElementId)
                        .ToListAsync())
                        .DefaultIfEmpty(0)
                        .Max();

                    long bytesLeft = minId > 0 && maxId > 0 ? cacheSizeInBytes - ConfigCtx.Options.EdgeImageCacheInBytes : 0;

                    if (minId > 0 && maxId > 0) {
                        Log.Debug($"ImageCache cacheSizeInBytes: {cacheSizeInBytes} > {ConfigCtx.Options.EdgeImageCacheInBytes}.. processing");
                    } else {
                        Log.Debug($"ImageCache cacheSizeInBytes: {cacheSizeInBytes} > {ConfigCtx.Options.EdgeImageCacheInBytes}.. nothing to process yet");

                        bytesLeft = 0;
                    }

                    for (long currentId = minId; currentId <= maxId && bytesLeft > 0; currentId = Math.Min(currentId + 30, maxId + 1))
                    {
                        Log.Debug($"Processing ImageCache: {currentId} .. {Math.Min(currentId + 30, maxId)}");

                        List<ImageCacheElementEntity> forDelete = new();
                        List<ImageCacheElementEntity> forProcessing = await webEdgeDb.ImageCacheElements
                            .Where(v => v.ImageCacheElementId >= currentId)
                            .Where(v => v.ImageCacheElementId <= Math.Min(currentId + 30, maxId))
                            .Where(v => v.LastAccessedUtc < utcNowExpireTimeUnixEpoch)
                            .ToListAsync();

                        foreach (ImageCacheElementEntity imageCacheElementEntity in forProcessing)
                        {
                            if (bytesLeft <= 0) {
                                break;
                            }

                            string filePath = cachePathService.CachePath(imageCacheElementEntity);

                            if (File.Exists(filePath)) {
                                File.Delete(filePath);
                            }

                            bytesLeft -= imageCacheElementEntity.FileSizeBytes;

                            forDelete.Add(imageCacheElementEntity);
                        }

                        Log.Debug($"Deleting {forDelete.Count} Elements from ImageCache");

                        webEdgeDb.ImageCacheElements
                            .RemoveRange(forDelete);

                        await webEdgeDb.SaveChangesAsync();

                        if (currentId != maxId) {
                            await Task.Delay(10);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Issue Processing ImageCache");
            }

            try
            {
                long cacheSizeInBytes = await webEdgeDb.BarcodeCacheElements
                    .Select(v => v.FileSizeBytes)
                    .SumAsync();

                if (cacheSizeInBytes <= ConfigCtx.Options.EdgeBarcodeCacheInBytes) {
                    Log.Debug($"BarcodeCache cacheSizeInBytes: {cacheSizeInBytes} <= {ConfigCtx.Options.EdgeBarcodeCacheInBytes}.. skipping");
                } else {
                    long minId = (await webEdgeDb.BarcodeCacheElements
                        .Where(v => v.LastAccessedUtc < utcNowExpireTimeUnixEpoch)
                        .OrderBy(v => v.LastAccessedUtc)
                        .Select(v => v.BarcodeCacheElementId)
                        .ToListAsync())
                        .DefaultIfEmpty(0)
                        .Min();

                    long maxId = (await webEdgeDb.BarcodeCacheElements
                        .Where(v => v.LastAccessedUtc < utcNowExpireTimeUnixEpoch)
                        .OrderBy(v => v.LastAccessedUtc)
                        .Select(v => v.BarcodeCacheElementId)
                        .ToListAsync())
                        .DefaultIfEmpty(0)
                        .Max();

                    long bytesLeft = minId > 0 && maxId > 0 ? cacheSizeInBytes - ConfigCtx.Options.EdgeBarcodeCacheInBytes : 0;

                    if (minId > 0 && maxId > 0) {
                        Log.Debug($"BarcodeCache cacheSizeInBytes: {cacheSizeInBytes} > {ConfigCtx.Options.EdgeBarcodeCacheInBytes}.. processing");
                    } else {
                        Log.Debug($"BarcodeCache cacheSizeInBytes: {cacheSizeInBytes} > {ConfigCtx.Options.EdgeBarcodeCacheInBytes}.. nothing to process yet");

                        bytesLeft = 0;
                    }

                    for (long currentId = minId; currentId <= maxId && bytesLeft > 0; currentId = Math.Min(currentId + 30, maxId + 1))
                    {
                        Log.Debug($"Processing BarcodeCache: {currentId} .. {Math.Min(currentId + 30, maxId)}");

                        List<BarcodeCacheElementEntity> forDelete = new();
                        List<BarcodeCacheElementEntity> forProcessing = await webEdgeDb.BarcodeCacheElements
                            .Where(v => v.BarcodeCacheElementId >= currentId)
                            .Where(v => v.BarcodeCacheElementId <= Math.Min(currentId + 30, maxId))
                            .Where(v => v.LastAccessedUtc < utcNowExpireTimeUnixEpoch)
                            .ToListAsync();

                        foreach (BarcodeCacheElementEntity barcodeCacheElementEntity in forProcessing)
                        {
                            if (bytesLeft <= 0) {
                                break;
                            }

                            string filePath = cachePathService.CachePath(barcodeCacheElementEntity);

                            if (File.Exists(filePath)) {
                                File.Delete(filePath);
                            }

                            bytesLeft -= barcodeCacheElementEntity.FileSizeBytes;

                            forDelete.Add(barcodeCacheElementEntity);
                        }

                        Log.Debug($"Deleting {forDelete.Count} Elements from BarcodeCache");

                        webEdgeDb.BarcodeCacheElements
                            .RemoveRange(forDelete);

                        await webEdgeDb.SaveChangesAsync();

                        if (currentId != maxId) {
                            await Task.Delay(10);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Issue Processing BarcodeCache");
            }

            try
            {
                long cacheSizeInBytes = await webEdgeDb.S3ImageCacheElements
                    .Select(v => v.FileSizeBytes)
                    .SumAsync();

                if (cacheSizeInBytes <= ConfigCtx.Options.EdgeS3ImageCacheInBytes) {
                    Log.Debug($"S3ImageCache cacheSizeInBytes: {cacheSizeInBytes} <= {ConfigCtx.Options.EdgeS3ImageCacheInBytes}.. skipping");
                } else {
                    long minId = (await webEdgeDb.S3ImageCacheElements
                        .Where(v => v.LastAccessedUtc < utcNowExpireTimeUnixEpoch)
                        .OrderBy(v => v.LastAccessedUtc)
                        .Select(v => v.S3ImageCacheElementId)
                        .ToListAsync())
                        .DefaultIfEmpty(0)
                        .Min();

                    long maxId = (await webEdgeDb.S3ImageCacheElements
                        .Where(v => v.LastAccessedUtc < utcNowExpireTimeUnixEpoch)
                        .OrderBy(v => v.LastAccessedUtc)
                        .Select(v => v.S3ImageCacheElementId)
                        .ToListAsync())
                        .DefaultIfEmpty(0)
                        .Max();

                    long bytesLeft = minId > 0 && maxId > 0 ? cacheSizeInBytes - ConfigCtx.Options.EdgeS3ImageCacheInBytes : 0;

                    if (minId > 0 && maxId > 0) {
                        Log.Debug($"S3ImageCache cacheSizeInBytes: {cacheSizeInBytes} > {ConfigCtx.Options.EdgeS3ImageCacheInBytes}.. processing");
                    } else {
                        Log.Debug($"S3ImageCache cacheSizeInBytes: {cacheSizeInBytes} > {ConfigCtx.Options.EdgeS3ImageCacheInBytes}.. nothing to process yet");

                        bytesLeft = 0;
                    }

                    for (long currentId = minId; currentId <= maxId && bytesLeft > 0; currentId = Math.Min(currentId + 30, maxId + 1))
                    {
                        Log.Debug($"Processing S3ImageCache: {currentId} .. {Math.Min(currentId + 30, maxId)}");

                        List<S3ImageCacheElementEntity> forDelete = new();
                        List<S3ImageCacheElementEntity> forProcessing = await webEdgeDb.S3ImageCacheElements
                            .Where(v => v.S3ImageCacheElementId >= currentId)
                            .Where(v => v.S3ImageCacheElementId <= Math.Min(currentId + 30, maxId))
                            .Where(v => v.LastAccessedUtc < utcNowExpireTimeUnixEpoch)
                            .ToListAsync();

                        foreach (S3ImageCacheElementEntity s3ImageCacheElementEntity in forProcessing)
                        {
                            if (bytesLeft <= 0) {
                                break;
                            }

                            string filePath = cachePathService.CachePath(s3ImageCacheElementEntity);

                            if (File.Exists(filePath)) {
                                File.Delete(filePath);
                            }

                            bytesLeft -= s3ImageCacheElementEntity.FileSizeBytes;

                            forDelete.Add(s3ImageCacheElementEntity);
                        }

                        Log.Debug($"Deleting {forDelete.Count} Elements from S3ImageCache");

                        webEdgeDb.S3ImageCacheElements
                            .RemoveRange(forDelete);

                        await webEdgeDb.SaveChangesAsync();

                        if (currentId != maxId) {
                            await Task.Delay(10);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Issue Processing S3ImageCache");
            }

            try
            {
                long cacheSizeInBytes = await webEdgeDb.StaticCacheElements
                    .Select(v => v.FileSizeBytes)
                    .SumAsync();

                if (cacheSizeInBytes <= ConfigCtx.Options.EdgeStaticCacheInBytes) {
                    Log.Debug($"StaticCache cacheSizeInBytes: {cacheSizeInBytes} <= {ConfigCtx.Options.EdgeStaticCacheInBytes}.. skipping");
                } else {
                    long minId = (await webEdgeDb.StaticCacheElements
                        .Where(v => v.LastAccessedUtc < utcNowExpireTimeUnixEpoch)
                        .OrderBy(v => v.LastAccessedUtc)
                        .Select(v => v.StaticCacheElementId)
                        .ToListAsync())
                        .DefaultIfEmpty(0)
                        .Min();

                    long maxId = (await webEdgeDb.StaticCacheElements
                        .Where(v => v.LastAccessedUtc < utcNowExpireTimeUnixEpoch)
                        .OrderBy(v => v.LastAccessedUtc)
                        .Select(v => v.StaticCacheElementId)
                        .ToListAsync())
                        .DefaultIfEmpty(0)
                        .Max();

                    long bytesLeft = minId > 0 && maxId > 0 ? cacheSizeInBytes - ConfigCtx.Options.EdgeStaticCacheInBytes : 0;

                    if (minId > 0 && maxId > 0) {
                        Log.Debug($"StaticCache cacheSizeInBytes: {cacheSizeInBytes} > {ConfigCtx.Options.EdgeStaticCacheInBytes}.. processing");
                    } else {
                        Log.Debug($"StaticCache cacheSizeInBytes: {cacheSizeInBytes} > {ConfigCtx.Options.EdgeStaticCacheInBytes}.. nothing to process yet");

                        bytesLeft = 0;
                    }

                    for (long currentId = minId; currentId <= maxId && bytesLeft > 0; currentId = Math.Min(currentId + 30, maxId + 1))
                    {
                        Log.Debug($"Processing StaticCache: {currentId} .. {Math.Min(currentId + 30, maxId)}");

                        List<StaticCacheElementEntity> forDelete = new();
                        List<StaticCacheElementEntity> forProcessing = await webEdgeDb.StaticCacheElements
                            .Where(v => v.StaticCacheElementId >= currentId)
                            .Where(v => v.StaticCacheElementId <= Math.Min(currentId + 30, maxId))
                            .Where(v => v.LastAccessedUtc < utcNowExpireTimeUnixEpoch)
                            .ToListAsync();

                        foreach (StaticCacheElementEntity staticCacheElementEntity in forProcessing)
                        {
                            if (bytesLeft <= 0) {
                                break;
                            }

                            string filePath = cachePathService.CachePath(staticCacheElementEntity);

                            if (File.Exists(filePath)) {
                                File.Delete(filePath);
                            }

                            bytesLeft -= staticCacheElementEntity.FileSizeBytes;

                            forDelete.Add(staticCacheElementEntity);
                        }

                        Log.Debug($"Deleting {forDelete.Count} Elements from StaticCache");

                        webEdgeDb.StaticCacheElements
                            .RemoveRange(forDelete);

                        await webEdgeDb.SaveChangesAsync();

                        if (currentId != maxId) {
                            await Task.Delay(10);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Issue Processing StaticCache");
            }
        }
    }

    public Task StopAsync(CancellationToken stoppingToken)
    {
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        PeriodicTimer?.Dispose();
    }
}

