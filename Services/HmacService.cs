using System.Security.Cryptography;
using System.Text;

public interface IHmacService
{
    string Hash(string key, string text);

    bool IsValid(string key, string text, string hash);

    bool IsValid(string filePath, QueryString queryString);
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

    public bool IsValid(string filePath, QueryString queryString)
    {
        using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"{nameof(HmacService)}.{nameof(IsValid)}");

        string signature = WebApp.QueryStringGetValue(queryString, "signature");

        QueryString withoutSignature = WebApp.QueryStringExcept(queryString, "signature");

        bool haveQueryStringSignatureKey = String.IsNullOrWhiteSpace(signature)
            is false;
        bool haveCliSignatureKey = String.IsNullOrWhiteSpace(ConfigCtx.Options.EdgeOriginSignatureKey)
            is false;

        if (haveCliSignatureKey) {
            Log.Debug($"Signature IsValid: {filePath}{withoutSignature}");

            if (IsValid(ConfigCtx.Options.EdgeOriginSignatureKey, $"{filePath}{withoutSignature}", signature) is false) {
                Log.Debug($"Signature Mismatch");

                return false;
            }
        } else if (haveQueryStringSignatureKey) {
            Log.Debug($"Signature Given in Query and No Signature Configured");

            return false;
        }

        return true;
    }
}
