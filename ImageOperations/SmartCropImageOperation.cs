using BrianMed.SmartCrop;

public class SmartCropImageArgs
{
    public int Width { get; set; }
    public int Height { get; set; }

    public SmartCropImageArgs(List<string> args)
    {
        (string w, string h) = Parsers.ParseWH(args);

        Width = Int32.Parse(w);

        Height = Int32.Parse(h);
    }

    public override string ToString()
    {
        return JsonSerializer.Serialize(new
        {
            Width = Width,
            Height = Height
        });
    }
}

public class SmartCropImageOperation : ImageOperation
{
    public override void Run(FileStream fs, List<string> _args)
    {
        using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"{nameof(SmartCropImageOperation)}");

        SmartCropImageArgs args = new(_args);

        Log.Debug($"{nameof(SmartCropImageOperation)}: '{args}'");

        var result = new ImageCrop(args.Width, args.Height).Crop(fs);
        fs.Seek(0, System.IO.SeekOrigin.Begin);

        using (Image<Rgba32> image = Image.Load<Rgba32>(fs, out IImageFormat format))
        {
            image.Mutate(ctx =>
            {
                ctx.Crop(result.Area);
            });

            Log.Debug($"Saving: {fs.Name}");
            fs.Flush();
            fs.Seek(0, System.IO.SeekOrigin.Begin);
            image.Save(fs, format);
            fs.Flush();
            fs.Seek(0, System.IO.SeekOrigin.Begin);
        }
    }
}

