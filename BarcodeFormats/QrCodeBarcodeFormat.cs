using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using ZXing;
using ZXing.QrCode;

public class QrCodeBarcodeArgs
{
    public string Text { get; init; }

    public string Width { get; set; } = "400";
    public string Height { get; set; } = "400";

    public QrCodeBarcodeArgs(List<string> args)
    {
        Text = Parsers.ParseSingleString(args, "Text");

        if (Parsers.HasArg(args, "WH")) {
            (Width, Height) = Parsers.ParseWH(args);
        }

        if (Width == "0") {
            Width = "400";
        }

        if (Height == "0") {
            Height = "400";
        }
    }

    public override string ToString()
    {
        return JsonSerializer.Serialize(new
        {
            Text = Text,

            Width = Width,
            Height = Height
        });
    }
}

public class QrCodeBarcodeFormat : BarcodeFormat
{
    public override void Generate(FileStream fs, List<string> _args)
    {
        using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"{nameof(QrCodeBarcodeFormat)}");

        QrCodeBarcodeArgs args = new(_args);

        Log.Debug($"{nameof(Generate)}: {args}");

        BarcodeWriter<Rgba32> writer = new();

        QrCodeEncodingOptions qr = new()
        {
            CharacterSet = "UTF-8",
            ErrorCorrection = ZXing.QrCode.Internal.ErrorCorrectionLevel.H,
            Height = Int32.Parse(args.Height),
            Width = Int32.Parse(args.Width)
        };

        writer.Options = qr;
        writer.Format = ZXing.BarcodeFormat.QR_CODE;

        Image<Rgba32> image = writer.WriteAsImageSharp<Rgba32>(args.Text);

        Log.Debug($"Saving: {fs.Name}");
        fs.Flush();
        fs.Seek(0, System.IO.SeekOrigin.Begin);
        image.SaveAsPng(fs);
        fs.Flush();
        fs.Seek(0, System.IO.SeekOrigin.Begin);
    }
}
