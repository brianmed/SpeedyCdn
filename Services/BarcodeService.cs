using System.Collections.Concurrent;

using Microsoft.EntityFrameworkCore;

using SpeedyCdn.Server.Entities.Edge;

public interface IBarcodeService
{
    Task<BarcodeCacheElementEntity> GenerateFromQueryString(string barcodePathRelative, QueryString queryString);
}

public class BarcodeService : IBarcodeService
{
    WebEdgeDbContext WebEdgeDb { get; init; }

    ICacheElementService CacheElementService { get; init; }

    IQueryStringService QueryStringService { get; init; }

    static ConcurrentDictionary<string, bool> InFlightBarcodeOperations = new();

    public BarcodeService(
            WebEdgeDbContext webEdgeDb,
            ICacheElementService cacheElementService,
            IQueryStringService queryStringService)
    {
        CacheElementService = cacheElementService;

        WebEdgeDb = webEdgeDb;

        QueryStringService = queryStringService;
    }

    public async Task<BarcodeCacheElementEntity> GenerateFromQueryString(string barcodePathRelative, QueryString queryString)
    {
        using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"{nameof(BarcodeService)}.{nameof(GenerateFromQueryString)}");

        string barcodePathAbsNoId = null;
        BarcodeCacheElementEntity cacheElement = null;

        try
        {
            SpinWait sw = new SpinWait();

            while (InFlightBarcodeOperations.TryAdd(barcodePathRelative, true) is false)
            {
                sw.SpinOnce();
            }

            barcodePathAbsNoId = Path.Combine(ConfigCtx.Options.EdgeCacheBarcodesDirectory, barcodePathRelative);
            cacheElement = null;

            Directory.CreateDirectory(Path.GetDirectoryName(barcodePathAbsNoId));

            string barcodePathAbsWithId = Directory.EnumerateFiles(
                Path.GetDirectoryName(barcodePathAbsNoId),
                    $"{Path.GetFileName(barcodePathAbsNoId)}.*", SearchOption.TopDirectoryOnly)
                .SingleOrDefault();

            if (barcodePathAbsWithId is not null && File.Exists(barcodePathAbsWithId)) {
                if (new FileInfo(barcodePathAbsWithId).Length > 0) {
                    Log.Debug($"Barcode Cache Hit: {queryString}");
                    
                    long barcodeCacheElementId = long.Parse(Path.GetExtension(Path.GetFileName(barcodePathAbsWithId)).Replace(".", String.Empty));

                    cacheElement = await WebEdgeDb.BarcodeCacheElements
                        .Where(v => v.BarcodeCacheElementId == barcodeCacheElementId)
                        .SingleAsync();

                    return cacheElement;
                } else {
                    Log.Debug($"Zero Byte Barcode Cache File: {queryString}");
                }
            } else {
                Log.Debug($"No Barcode Cache File Found: {queryString}");
            }

            Dictionary<string, HashSet<string>> barcodeRequiredParameters = new();

            barcodeRequiredParameters.Add("qrcode", new());

            barcodeRequiredParameters["qrcode"].Add("qrcode.Text");

            List<(string Name, List<string> Args)> args = QueryStringService.Args(queryString, barcodeRequiredParameters);

            BarcodeFormat barcodeFormat = BarcodeFormatFactory(args.First().Name);

            using (FileStream barcodeStream = new FileStream(barcodePathAbsNoId, FileMode.OpenOrCreate))
            {
                barcodeFormat.Generate(barcodeStream, args.First().Args);
            }

            cacheElement = await CacheElementService.InsertBarcodeAsync(barcodePathAbsNoId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Issue Generating Barcode: {barcodePathRelative} {queryString}");
        }
        finally
        {
            if (InFlightBarcodeOperations.TryRemove(barcodePathRelative, out bool whence) is false) {
                Log.Error($"Issue Removing {barcodePathRelative}");
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
