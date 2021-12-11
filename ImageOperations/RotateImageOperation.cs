public class RotateImageArgs
{
    public RotateMode RotateMode { get; set; } = RotateMode.None;

    public RotateImageArgs(List<string> args)
    {
        RotateMode = Parsers.ParseSingleEnum<RotateMode>(args, "Mode");
    }

    public override string ToString()
    {
        return JsonSerializer.Serialize(new
        {
            RotateMode = RotateMode
        });
    }
}

public class RotateImageOperation : ImageOperation
{
    public override void Run(FileStream fs, List<string> _args)
    {
        using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"{nameof(RotateImageOperation)}");

        RotateImageArgs args = new(_args);

        Log.Debug($"{nameof(RotateImageOperation)}: '{args}'");

        using (Image<Rgba32> image = Image.Load<Rgba32>(fs, out IImageFormat format))
        {
            image.Mutate(ctx =>
            {
                ctx.RotateFlip(args.RotateMode, FlipMode.None);
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
