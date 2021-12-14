using System.Collections.Concurrent;

public interface IBarcodeService
{
    void GenerateFromQueryString(string barcodePath, QueryString queryString);
}

public class BarcodeService : IBarcodeService
{
    public IQueryStringService QueryStringService { get; init; }

    static ConcurrentDictionary<string, bool> InFlightBarcodeOperations = new();

    public BarcodeService(IQueryStringService queryStringService)
    {
        QueryStringService = queryStringService;
    }

    public void GenerateFromQueryString(string barcodePath, QueryString queryString)
    {
        using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"{nameof(BarcodeService)}.{nameof(GenerateFromQueryString)}");

        try
        {
            SpinWait sw = new SpinWait();

            while (InFlightBarcodeOperations.TryAdd(barcodePath, true) is false)
            {
                sw.SpinOnce();
            }

            string barcodeAbsPath = Path.Combine(ConfigCtx.Options.EdgeCacheBarcodesDirectory, barcodePath);

            Directory.CreateDirectory(Path.GetDirectoryName(barcodeAbsPath));

            if (File.Exists(barcodeAbsPath)) {
                if (new FileInfo(barcodeAbsPath).Length > 0) {
                    Log.Debug($"Barcode Cache Hit: {queryString}");

                    return;
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

            FileStream barcodeStream = new FileStream(Path.Combine(ConfigCtx.Options.EdgeCacheBarcodesDirectory, barcodePath), FileMode.OpenOrCreate);

            barcodeFormat.Generate(barcodeStream, args.First().Args);
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Issue Generating Barcode: {barcodePath} {queryString}");
        }
        finally
        {
            if (InFlightBarcodeOperations.TryRemove(barcodePath, out bool whence) is false) {
                Log.Error($"Issue Removing {barcodePath}");
            }
        }
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
