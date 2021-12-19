using System.Text;
using System.Text.Json;

using SpeedyCdn.Server.Entities.Edge;

public interface ICachePathService
{
    string RelativeWithBucket(string[] translate);
    string RelativeWithBucket(string translate);

    string CachePath(BarcodeCacheElementEntity cacheElement);
    string CachePath(ImageCacheElementEntity cacheElement);
    string CachePath(S3ImageCacheElementEntity cacheElement);
    string CachePath(StaticCacheElementEntity cacheElement);

    string CachePath(string directory, string[] translate);

    // 
}

public class CachePathService : ICachePathService
{
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

    public string CachePath(BarcodeCacheElementEntity cacheElement)
    {
        string cacheRelative = RelativeWithBucket(new[] { cacheElement.UrlPath, cacheElement.QueryString });

        return Path.Combine(ConfigCtx.Options.EdgeCacheBarcodesDirectory, cacheRelative);
    }

    public string CachePath(ImageCacheElementEntity cacheElement)
    {
        string cacheRelative = RelativeWithBucket(new[] { cacheElement.UrlPath, cacheElement.QueryString });

        return Path.Combine(ConfigCtx.Options.EdgeCacheImagesDirectory, cacheRelative);
    }

    public string CachePath(S3ImageCacheElementEntity cacheElement)
    {
        string cacheRelative = RelativeWithBucket(new[] { cacheElement.UrlPath, cacheElement.QueryString });

        return Path.Combine(ConfigCtx.Options.EdgeCacheS3ImagesDirectory, cacheRelative);
    }

    public string CachePath(StaticCacheElementEntity cacheElement)
    {
        string cacheRelative = RelativeWithBucket(new[] { cacheElement.UrlPath, cacheElement.QueryString });

        return Path.Combine(ConfigCtx.Options.EdgeCacheStaticDirectory, cacheRelative);
    }

    public string CachePath(string directory, string[] translate)
    {
        return Path.Combine(directory, RelativeWithBucket(translate));
    }

    // 
}
