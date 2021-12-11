public class CropImageArgs
{
    public int X { get; set; }
    public int Y { get; set; }

    public string Width { get; set; }
    public string Height { get; set; }

    public Rectangle Rectangle { get; set; }

    public CropImageArgs(List<string> args)
    {
        (X, Y) = Parsers.ParseXY(args);

        (Width, Height) = Parsers.ParseWH(args);

        Rectangle = new()
        {
            X = X,
            Y = Y,
            Width = Int32.Parse(Width),
            Height = Int32.Parse(Height)
        };
    }

    public override string ToString()
    {
        return JsonSerializer.Serialize(new
        {
            X = X,
            Y = Y,
            Width = Width,
            Height = Height,
            Rectangle = Rectangle
        });
    }
}

public class CropImageOperation : ImageOperation
{
    public override void Run(FileStream fs, List<string> _args)
    {
        using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"{nameof(CropImageOperation)}");

        CropImageArgs args = new(_args);

        Log.Debug($"{nameof(CropImageOperation)}: '{args}'");

        using (Image<Rgba32> image = Image.Load<Rgba32>(fs, out IImageFormat format))
        {
            image.Mutate(ctx =>
            {
                ctx.Crop(args.Rectangle);
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
