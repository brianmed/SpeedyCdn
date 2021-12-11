public class ResizeImageArgs
{
    public string Width { get; set; }
    public string Height { get; set; }

    public ResizeImageArgs(List<string> args)
    {
        (Width, Height) = Parsers.ParseWH(args);
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

public class ResizeImageOperation : ImageOperation
{
    public override void Run(FileStream fs, List<string> _args)
    {
        using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"{nameof(ResizeImageOperation)}");

        ResizeImageArgs args = new(_args);

        Log.Debug($"{nameof(ResizeImageOperation)}: '{args}'");

        using (Image<Rgba32> image = Image.Load<Rgba32>(fs, out IImageFormat format))
        {
            double percentWidth = args.Width.Contains('%') ? (double.Parse(args.Width.Replace("%", String.Empty)) / 100.0) : 1.0;
            double percentHeight = args.Height.Contains('%') ? (double.Parse(args.Height.Replace("%", String.Empty)) / 100.0) : 1.0;

            int width = args.Width.Contains('%') ? ((int)(image.Width * percentWidth)) : Int32.Parse(args.Width);
            int height = args.Height.Contains('%') ? ((int)(image.Height * percentHeight)) : Int32.Parse(args.Height);

            Log.Debug($"Width: {args.Width}: Height: {args.Height}: percentWidth: {percentWidth}: percentHeight: {percentHeight}: width: {width}: height: {height}");

            image.Mutate(ctx =>
            {
                ctx.Resize(width, height);
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
