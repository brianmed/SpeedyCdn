using System.Collections.Concurrent;

using Microsoft.EntityFrameworkCore;

using SpeedyCdn.Server.Entities.Edge;

public interface IBarcodeService
{
    Task<BarcodeCacheElementEntity> GenerateFromQueryString(QueryString queryString);
}

public class BarcodeService : IBarcodeService
{
    WebEdgeDbContext WebEdgeDb { get; init; }

    ICacheElementService CacheElementService { get; init; }

    ICachePathService CachePathService { get; init; }

    IQueryStringService QueryStringService { get; init; }

    static ConcurrentDictionary<string, bool> InFlightBarcodeOperations = new();

    public BarcodeService(
            WebEdgeDbContext webEdgeDb,
            ICacheElementService cacheElementService,
            ICachePathService cachePathService,
            IQueryStringService queryStringService)
    {
        CacheElementService = cacheElementService;

        CachePathService = cachePathService;

        WebEdgeDb = webEdgeDb;

        QueryStringService = queryStringService;
    }

    public async Task<BarcodeCacheElementEntity> GenerateFromQueryString(QueryString queryString)
    {
        using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"{nameof(BarcodeService)}.{nameof(GenerateFromQueryString)}");

        BarcodeCacheElementEntity cacheElement = null;

        string cachePathAbsolute = CachePathService.CachePath(
            ConfigCtx.Options.EdgeCacheBarcodesDirectory,
            new[] { String.Empty, queryString.ToString() });

        try
        {
            SpinWait sw = new SpinWait();

            while (InFlightBarcodeOperations.TryAdd(queryString.ToString(), true) is false)
            {
                sw.SpinOnce();
            }

            Directory.CreateDirectory(Path.GetDirectoryName(cachePathAbsolute));

            cacheElement = await WebEdgeDb.BarcodeCacheElements
                .Where(v => v.UrlPath == String.Empty)
                .Where(v => v.QueryString == queryString.ToString())
                .SingleOrDefaultAsync();

            if (cacheElement is not null && File.Exists(cachePathAbsolute)) {
                if (new FileInfo(cachePathAbsolute).Length > 0) {
                    Log.Debug($"Cache Hit: {queryString}");

                    cacheElement.LastAccessedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    await WebEdgeDb.SaveChangesAsync();

                    return cacheElement;
                } else {
                    Log.Debug($"Zero Byte Cache File: {queryString}");
                }
            } else {
                Log.Debug($"No Cache File Found: {queryString}");
            }

            Dictionary<string, HashSet<string>> barcodeRequiredParameters = new();

            barcodeRequiredParameters.Add("qrcode", new());

            barcodeRequiredParameters["qrcode"].Add("qrcode.Text");

            List<(string Name, List<string> Args)> args = QueryStringService.Args(queryString, barcodeRequiredParameters);

            BarcodeFormat barcodeFormat = BarcodeFormatFactory(args.First().Name);

            using (FileStream barcodeStream = new FileStream(cachePathAbsolute, FileMode.OpenOrCreate))
            {
                barcodeFormat.Generate(barcodeStream, args.First().Args);
            }

            cacheElement = await CacheElementService.InsertBarcodeAsync(cachePathAbsolute, queryString.ToString());
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Issue Generating Barcode: {queryString}");
        }
        finally
        {
            if (InFlightBarcodeOperations.TryRemove(queryString.ToString(), out bool whence) is false) {
                Log.Error($"Issue Removing {queryString}");
            }
        }

        return cacheElement;
    }

    private BarcodeFormat BarcodeFormatFactory(string formatName)
    {
        switch (formatName.ToLower())
        {
            case "qrcode":
                return new QrCodeBarcodeFormat();

            default:
                throw new ArgumentException($"Unsupported {formatName}");
        }
    }
}

public abstract class BarcodeFormat
{
    public abstract void Generate(FileStream fs, List<string> args);
}
