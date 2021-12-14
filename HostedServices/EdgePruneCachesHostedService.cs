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

            // TODO: so much code.  Is there a better way

            try
            {
                Log.Debug("Processing ImageCache");
                long cacheSizeInBytes = await webEdgeDb.ImageCacheElements
                    .Select(v => v.FileSizeBytes)
                    .SumAsync();

                if (cacheSizeInBytes <= ConfigCtx.Options.EdgeCacheInBytes) {
                    Log.Debug($"Current cacheSizeInBytes: {cacheSizeInBytes} <= {ConfigCtx.Options.EdgeCacheInBytes}.. skipping");
                } else {
                    Log.Debug($"Current cacheSizeInBytes: {cacheSizeInBytes} > {ConfigCtx.Options.EdgeCacheInBytes}.. processing");

                    long minId = await webEdgeDb.ImageCacheElements
                        .OrderBy(v => v.LastAccessedUtc)
                        .Select(v => v.ImageCacheElementId)
                        .MinAsync();

                    long maxId = await webEdgeDb.ImageCacheElements
                        .OrderBy(v => v.LastAccessedUtc)
                        .Select(v => v.ImageCacheElementId)
                        .MaxAsync();

                    long bytesLeft = cacheSizeInBytes - ConfigCtx.Options.EdgeCacheInBytes;

                    for (long currentId = minId; currentId <= maxId && bytesLeft > 0; currentId = Math.Min(currentId + 30, maxId + 1))
                    {
                        Log.Debug($"Processing ImageCache: {currentId} .. {Math.Min(currentId + 30, maxId)}");

                        List<ImageCacheElementEntity> forDelete = new();

                        // TODO: the int <--> long conversion is a problem for another day
                        foreach (int id in Enumerable.Range((int)currentId, 30))
                        {
                            if (bytesLeft <= 0) {
                                break;
                            }

                            ImageCacheElementEntity imageCacheElementEntity = await webEdgeDb.ImageCacheElements
                                .Where(v => v.ImageCacheElementId == id)
                                .SingleOrDefaultAsync();

                            if (imageCacheElementEntity is not null) {
                                string filePath = Path.Combine(ConfigCtx.Options.EdgeCacheImagesDirectory, imageCacheElementEntity.CachePath);

                                if (File.Exists(filePath)) {
                                    File.Delete(filePath);
                                }

                                bytesLeft -= imageCacheElementEntity.FileSizeBytes;

                                forDelete.Add(imageCacheElementEntity);
                            }
                        }

                        Log.Debug($"Deleting {forDelete.Count} Elements from ImageCache");

                        webEdgeDb.ImageCacheElements
                            .RemoveRange(forDelete);

                        await webEdgeDb.SaveChangesAsync();

                        if (currentId != maxId) {
                            await Task.Delay(300);
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
                Log.Debug("Processing BarcodeCache");
                long cacheSizeInBytes = await webEdgeDb.BarcodeCacheElements
                    .Select(v => v.FileSizeBytes)
                    .SumAsync();

                if (cacheSizeInBytes <= ConfigCtx.Options.EdgeCacheInBytes) {
                    Log.Debug($"Current cacheSizeInBytes: {cacheSizeInBytes} <= {ConfigCtx.Options.EdgeCacheInBytes}.. skipping");
                } else {
                    Log.Debug($"Current cacheSizeInBytes: {cacheSizeInBytes} > {ConfigCtx.Options.EdgeCacheInBytes}.. processing");

                    long minId = await webEdgeDb.BarcodeCacheElements
                        .OrderBy(v => v.LastAccessedUtc)
                        .Select(v => v.BarcodeCacheElementId)
                        .MinAsync();

                    long maxId = await webEdgeDb.BarcodeCacheElements
                        .OrderBy(v => v.LastAccessedUtc)
                        .Select(v => v.BarcodeCacheElementId)
                        .MaxAsync();

                    long bytesLeft = cacheSizeInBytes - ConfigCtx.Options.EdgeCacheInBytes;

                    for (long currentId = minId; currentId <= maxId && bytesLeft > 0; currentId = Math.Min(currentId + 30, maxId + 1))
                    {
                        Log.Debug($"Processing BarcodeCache: {currentId} .. {Math.Min(currentId + 30, maxId)}");

                        List<BarcodeCacheElementEntity> forDelete = new();

                        // TODO: the int <--> long conversion is a problem for another day
                        foreach (int id in Enumerable.Range((int)currentId, 30))
                        {
                            if (bytesLeft <= 0) {
                                break;
                            }

                            BarcodeCacheElementEntity barcodeCacheElementEntity = await webEdgeDb.BarcodeCacheElements
                                .Where(v => v.BarcodeCacheElementId == id)
                                .SingleOrDefaultAsync();

                            if (barcodeCacheElementEntity is not null) {
                                string filePath = Path.Combine(ConfigCtx.Options.EdgeCacheBarcodesDirectory, barcodeCacheElementEntity.CachePath);

                                if (File.Exists(filePath)) {
                                    File.Delete(filePath);
                                }

                                bytesLeft -= barcodeCacheElementEntity.FileSizeBytes;

                                forDelete.Add(barcodeCacheElementEntity);
                            }
                        }

                        Log.Debug($"Deleting {forDelete.Count} Elements from BarcodeCache");

                        webEdgeDb.BarcodeCacheElements
                            .RemoveRange(forDelete);

                        await webEdgeDb.SaveChangesAsync();

                        if (currentId != maxId) {
                            await Task.Delay(300);
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
                Log.Debug("Processing S3ImageCache");
                long cacheSizeInBytes = await webEdgeDb.S3ImageCacheElements
                    .Select(v => v.FileSizeBytes)
                    .SumAsync();

                if (cacheSizeInBytes <= ConfigCtx.Options.EdgeCacheInBytes) {
                    Log.Debug($"Current cacheSizeInBytes: {cacheSizeInBytes} <= {ConfigCtx.Options.EdgeCacheInBytes}.. skipping");
                } else {
                    Log.Debug($"Current cacheSizeInBytes: {cacheSizeInBytes} > {ConfigCtx.Options.EdgeCacheInBytes}.. processing");

                    long minId = await webEdgeDb.S3ImageCacheElements
                        .OrderBy(v => v.LastAccessedUtc)
                        .Select(v => v.S3ImageCacheElementId)
                        .MinAsync();

                    long maxId = await webEdgeDb.S3ImageCacheElements
                        .OrderBy(v => v.LastAccessedUtc)
                        .Select(v => v.S3ImageCacheElementId)
                        .MaxAsync();

                    long bytesLeft = cacheSizeInBytes - ConfigCtx.Options.EdgeCacheInBytes;

                    for (long currentId = minId; currentId <= maxId && bytesLeft > 0; currentId = Math.Min(currentId + 30, maxId + 1))
                    {
                        Log.Debug($"Processing S3ImageCache: {currentId} .. {Math.Min(currentId + 30, maxId)}");

                        List<S3ImageCacheElementEntity> forDelete = new();

                        // TODO: the int <--> long conversion is a problem for another day
                        foreach (int id in Enumerable.Range((int)currentId, 30))
                        {
                            if (bytesLeft <= 0) {
                                break;
                            }

                            S3ImageCacheElementEntity s3ImageCacheElementEntity = await webEdgeDb.S3ImageCacheElements
                                .Where(v => v.S3ImageCacheElementId == id)
                                .SingleOrDefaultAsync();

                            if (s3ImageCacheElementEntity is not null) {
                                string filePath = Path.Combine(ConfigCtx.Options.EdgeCacheS3ImagesDirectory, s3ImageCacheElementEntity.CachePath);

                                if (File.Exists(filePath)) {
                                    File.Delete(filePath);
                                }

                                bytesLeft -= s3ImageCacheElementEntity.FileSizeBytes;

                                forDelete.Add(s3ImageCacheElementEntity);
                            }
                        }

                        Log.Debug($"Deleting {forDelete.Count} Elements from S3ImageCache");

                        webEdgeDb.S3ImageCacheElements
                            .RemoveRange(forDelete);

                        await webEdgeDb.SaveChangesAsync();

                        if (currentId != maxId) {
                            await Task.Delay(300);
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
                Log.Debug("Processing StaticCache");
                long cacheSizeInBytes = await webEdgeDb.StaticCacheElements
                    .Select(v => v.FileSizeBytes)
                    .SumAsync();

                if (cacheSizeInBytes <= ConfigCtx.Options.EdgeCacheInBytes) {
                    Log.Debug($"Current cacheSizeInBytes: {cacheSizeInBytes} <= {ConfigCtx.Options.EdgeCacheInBytes}.. skipping");
                } else {
                    Log.Debug($"Current cacheSizeInBytes: {cacheSizeInBytes} > {ConfigCtx.Options.EdgeCacheInBytes}.. processing");

                    long minId = await webEdgeDb.StaticCacheElements
                        .OrderBy(v => v.LastAccessedUtc)
                        .Select(v => v.StaticCacheElementId)
                        .MinAsync();

                    long maxId = await webEdgeDb.StaticCacheElements
                        .OrderBy(v => v.LastAccessedUtc)
                        .Select(v => v.StaticCacheElementId)
                        .MaxAsync();

                    long bytesLeft = cacheSizeInBytes - ConfigCtx.Options.EdgeCacheInBytes;

                    for (long currentId = minId; currentId <= maxId && bytesLeft > 0; currentId = Math.Min(currentId + 30, maxId + 1))
                    {
                        Log.Debug($"Processing StaticCache: {currentId} .. {Math.Min(currentId + 30, maxId)}");

                        List<StaticCacheElementEntity> forDelete = new();

                        // TODO: the int <--> long conversion is a problem for another day
                        foreach (int id in Enumerable.Range((int)currentId, 30))
                        {
                            if (bytesLeft <= 0) {
                                break;
                            }

                            StaticCacheElementEntity staticCacheElementEntity = await webEdgeDb.StaticCacheElements
                                .Where(v => v.StaticCacheElementId == id)
                                .SingleOrDefaultAsync();

                            if (staticCacheElementEntity is not null) {
                                string filePath = Path.Combine(ConfigCtx.Options.EdgeCacheStaticDirectory, staticCacheElementEntity.CachePath);

                                if (File.Exists(filePath)) {
                                    File.Delete(filePath);
                                }

                                bytesLeft -= staticCacheElementEntity.FileSizeBytes;

                                forDelete.Add(staticCacheElementEntity);
                            }
                        }

                        Log.Debug($"Deleting {forDelete.Count} Elements from StaticCache");

                        webEdgeDb.StaticCacheElements
                            .RemoveRange(forDelete);

                        await webEdgeDb.SaveChangesAsync();

                        if (currentId != maxId) {
                            await Task.Delay(300);
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

