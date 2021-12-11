using System.Text;
using System.Text.Json;

public interface ICachePathService
{
    string RelativeWithBucket(string translate, string fileName);

    string DecodedWithoutBucket(string encoded);
}

public class CachePathService : ICachePathService
{
    public string RelativeWithBucket(string translate, string fileName)
    {
        List<string> cachePathSegments = new();

        foreach (byte[] chunk in Encoding.UTF8.GetBytes(translate).Chunk(128))
        {
            string segment = Convert.ToBase64String(chunk)
                .Replace("+", "_")
                .Replace("/", "-");

            cachePathSegments.Add(segment);
        }

        string cachePathSegment = $"{Path.Combine(cachePathSegments.ToArray())}{Path.GetExtension(fileName)}";

        using MemoryStream stream = new MemoryStream(Encoding.ASCII.GetBytes(cachePathSegment));
        uint cachePathBucket = ((uint)MurMurHash3.Hash(stream)) % 5000;

        return Path.Combine(cachePathBucket.ToString(), cachePathSegment);
    }

    public string DecodedWithoutBucket(string _encoded)
    {
        string encoded = Path.ChangeExtension(_encoded, null);

        string decoded = String.Empty;

        foreach (string segment in encoded.Split(Path.DirectorySeparatorChar).Skip(1))
        {
            decoded += Encoding.UTF8.GetString(Convert.FromBase64String(segment));
        }

        return decoded;
    }
}
