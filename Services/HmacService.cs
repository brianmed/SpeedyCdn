using System.Security.Cryptography;
using System.Text;

public interface IHmacService
{
    string Hash(string key, string text);

    bool IsValid(string key, string text, string hash);
}

public class HmacService : IHmacService
{
    public string Hash(string key, string text)
    {
        UTF8Encoding encoding = new UTF8Encoding();

        byte[] textBytes = encoding.GetBytes(text);
        byte[] keyBytes = encoding.GetBytes(key);

        using HMACSHA256 hash = new HMACSHA256(keyBytes);

        byte[] hashBytes = hash.ComputeHash(textBytes);

        return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
    }

    public bool IsValid(string key, string text, string hash)
    {
        return Hash(key, text) == hash;
    }
}
