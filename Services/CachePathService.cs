using System.Text;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using SpeedyCdn.Server.Entities;
using SpeedyCdn.Server.Entities.Edge;

public interface ICachePathService
{
    string CachePath(BarcodeCacheElementEntity cacheElement);
    string CachePath(ImageCacheElementEntity cacheElement);
    string CachePath(S3ImageCacheElementEntity cacheElement);
    string CachePath(StaticCacheElementEntity cacheElement);

    string CacheBucketDirectory(string directory, string[] translate);

    Task<string> GetCachePathAbsoluteAsync(string tableName, string urlPath, string queryString);

    // 
}

public class CachePathService : ICachePathService
{
    WebEdgeDbContext WebEdgeDb { get; init; }

    static SemaphoreSlim SequenceMutex { get; } = new(1, 1);

    public CachePathService(
        WebEdgeDbContext webEdgeDb)
    {
        WebEdgeDb = webEdgeDb;
    }

    public string RelativeWithBucket(string[] translate)
    {
        string json = JsonSerializer.Serialize(translate);

        return RelativeWithBucket(json);
    }

    public string RelativeWithBucket(string translate)
    {
        List<string> cachePathSegments = new();

        foreach (byte[] chunk in Encoding.UTF8.GetBytes(translate).Chunk(128))
        {
            string segment = Convert.ToBase64String(chunk)
                .Replace("+", "_")
                .Replace("/", "-");

            cachePathSegments.Add(segment);
        }

        string cachePathSegment = $"{Path.Combine(cachePathSegments.ToArray())}";

        // Need two streams because we got an error with Seek
        using MemoryStream stream1st = new MemoryStream(Encoding.ASCII.GetBytes(cachePathSegment));
        uint cachePathBucket1st = ((uint)MurMurHash3.Hash(stream1st)) % 5000;

        using MemoryStream stream2nd = new MemoryStream(Encoding.ASCII.GetBytes(cachePathSegment));
        uint cachePathBucket2nd = ((uint)MurMurHash3.Hash(stream2nd)) % 50;

        return Path.Combine(cachePathBucket1st.ToString(), cachePathBucket2nd.ToString(), cachePathSegment);
    }

    async public Task<string> GetCachePathAbsoluteAsync(string tableName, string urlPath, string queryString)
    {
        long cachePathSequence;

        using var transaction = WebEdgeDb.Database.BeginTransaction();

        TableSequenceEntity tableSequence = (await WebEdgeDb.TableSequences
            .FromSqlInterpolated($@"
                INSERT INTO TableSequences (Name, Sequence) VALUES ({tableName}, {1})

                ON CONFLICT(Name) DO

                UPDATE SET
                    Sequence = Sequence + 1
                WHERE Name = excluded.Name

                RETURNING *;
            ")
            .ToListAsync())
            .Single();

        cachePathSequence = tableSequence.Sequence;

        await transaction.CommitAsync();

        string cacheDirectory = tableName switch
        {
            "BarcodeCacheElements" => ConfigCtx.Options.EdgeCacheBarcodesDirectory,
            "ImageCacheElements" => ConfigCtx.Options.EdgeCacheImagesDirectory,
            "S3ImageCacheElements" => ConfigCtx.Options.EdgeCacheS3ImagesDirectory,
            "StaticCacheElements" => ConfigCtx.Options.EdgeCacheStaticDirectory,
        };

        string cacheBucketDirectory = CacheBucketDirectory(
            cacheDirectory,
            new[] { urlPath, queryString });

        string cachePathAbsolute = Path.Combine(cacheDirectory, cacheBucketDirectory, cachePathSequence.ToString());

        Directory.CreateDirectory(Path.GetDirectoryName(cachePathAbsolute));

        return cachePathAbsolute;
    }

    public string CacheBucketDirectory(string directory, string[] translate)
    {
        string json = JsonSerializer.Serialize(translate);

        List<string> cachePathSegments = new();

        foreach (byte[] chunk in Encoding.UTF8.GetBytes(json).Chunk(128))
        {
            string segment = Convert.ToBase64String(chunk)
                .Replace("+", "_")
                .Replace("/", "-");

            cachePathSegments.Add(segment);
        }

        string cachePathSegment = $"{Path.Combine(cachePathSegments.ToArray())}";

        // Need two streams because we got an error with Seek
        using MemoryStream stream1st = new MemoryStream(Encoding.ASCII.GetBytes(cachePathSegment));
        uint cachePathBucket1st = ((uint)MurMurHash3.Hash(stream1st)) % 5000;

        using MemoryStream stream2nd = new MemoryStream(Encoding.ASCII.GetBytes(cachePathSegment));
        uint cachePathBucket2nd = ((uint)MurMurHash3.Hash(stream2nd)) % 50;

        return Path.Combine(cachePathBucket1st.ToString(), cachePathBucket2nd.ToString());
    }

    public string CachePath(BarcodeCacheElementEntity cacheElement)
    {
        return Path.Combine(ConfigCtx.Options.EdgeCacheBarcodesDirectory, cacheElement.CachePath);
    }

    public string CachePath(ImageCacheElementEntity cacheElement)
    {
        return Path.Combine(ConfigCtx.Options.EdgeCacheBarcodesDirectory, cacheElement.CachePath);
    }

    public string CachePath(S3ImageCacheElementEntity cacheElement)
    {
        return Path.Combine(ConfigCtx.Options.EdgeCacheBarcodesDirectory, cacheElement.CachePath);
    }

    public string CachePath(StaticCacheElementEntity cacheElement)
    {
        return Path.Combine(ConfigCtx.Options.EdgeCacheBarcodesDirectory, cacheElement.CachePath);
    }

    // 
}
