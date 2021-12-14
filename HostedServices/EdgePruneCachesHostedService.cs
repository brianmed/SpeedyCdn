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
        TimeSpan oneHour = TimeSpan.FromHours(4);
        PeriodicTimer = new(oneHour);

        Task.Run(async () => await DoWorkAsync());

        return Task.CompletedTask;
    }

    async private Task DoWorkAsync()
    {
        using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", "Edge::EdgePruneCacheHostedService");

        while (await PeriodicTimer.WaitForNextTickAsync())
        {
            try
            {
                using IServiceScope scope = ScopeFactory.CreateScope();

                WebEdgeDbContext webEdgeDb = scope.ServiceProvider.GetRequiredService<WebEdgeDbContext>();
                ICachePathService cachePathService = scope.ServiceProvider.GetRequiredService<ICachePathService>();

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
                Log.Error(ex, $"Exception during DoWork");
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

