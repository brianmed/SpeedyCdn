public interface IBarcodeService
{
    void GenerateFromQueryString(string barcodePath, QueryString queryString);
}

public class BarcodeService : IBarcodeService
{
    public IQueryStringService QueryStringService { get; init; }

    public BarcodeService(IQueryStringService queryStringService)
    {
        QueryStringService = queryStringService;
    }

    public void GenerateFromQueryString(string barcodePath, QueryString queryString)
    {
        Dictionary<string, HashSet<string>> barcodeRequiredParameters = new();

        barcodeRequiredParameters.Add("qrcode", new());

        barcodeRequiredParameters["qrcode"].Add("qrcode.Text");

        List<(string Name, List<string> Args)> args = QueryStringService.Args(queryString, barcodeRequiredParameters);

        BarcodeFormat barcodeFormat = BarcodeFormatFactory(args.First().Name);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(ConfigCtx.Options.EdgeCacheBarcodesDirectory, barcodePath)));
        FileStream barcodeStream = new FileStream(Path.Combine(ConfigCtx.Options.EdgeCacheBarcodesDirectory, barcodePath), FileMode.OpenOrCreate);

        barcodeFormat.Generate(barcodeStream, args.First().Args);
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
