public class FlipImageArgs
{
    public FlipMode FlipMode { get; set; } = FlipMode.None;

    public FlipImageArgs(List<string> args)
    {
        FlipMode = Parsers.ParseSingleEnum<FlipMode>(args, "Mode");
    }

    public override string ToString()
    {
        return JsonSerializer.Serialize(new
        {
            FlipMode = FlipMode
        });
    }
}

public class FlipImageOperation : ImageOperation
{
    public override void Run(FileStream fs, List<string> _args)
    {
        using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"{nameof(FlipImageOperation)}");

        FlipImageArgs args = new(_args);

        Log.Debug($"{nameof(FlipImageOperation)}: '{args}'");

        using (Image<Rgba32> image = Image.Load<Rgba32>(fs, out IImageFormat format))
        {
            image.Mutate(ctx =>
            {
                ctx.RotateFlip(RotateMode.None, args.FlipMode);
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
