public class ReplaceColorImageArgs
{
    public Color OldColor { get; }

    public Color NewColor { get; }

    public double Difference { get; set; } = 0;

    public ReplaceColorImageArgs(List<string> args)
    {
        if (Parsers.HasArg(args, "Difference")) {
            Difference = Parsers.ParseSingleDouble(args, "Difference");
        }

        OldColor = Parsers.ParseColor(args, "OldColor");
        NewColor = Parsers.ParseColor(args, "NewColor");
    }

    public override string ToString()
    {
        return JsonSerializer.Serialize(new
        {
            OldColor = OldColor.ToString(),
            NewColor = NewColor.ToString(),
            Difference = Difference
        });
    }
}

public class ReplaceColorImageOperation : ImageOperation
{
    public override void Run(FileStream fs, List<string> _args)
    {
        using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"{nameof(ReplaceColorImageOperation)}");

        ReplaceColorImageArgs args = new(_args);

        Log.Debug($"{nameof(ReplaceColorImageOperation)}: '{args}'");

        using (Image<Rgba32> image = Image.Load<Rgba32>(fs, out IImageFormat format))
        {
            if (args.OldColor == Color.Transparent) {
                image.Mutate(ctx =>
                {
                    ctx.BackgroundColor(args.NewColor);
                });
            } else {
                image.ProcessPixelRows(pixelAccessor =>
                {
                    for (int y = 0; y < pixelAccessor.Height; y++)
                    {
                        Span<Rgba32> row = pixelAccessor.GetRowSpan(y);

                        for (int x = 0; x < row.Length; x++)
                        {
                            Color color = new(row[x]);

                            if (color == args.OldColor || (args.Difference > 0 && ColorDifference(row[x], args.OldColor) <= args.Difference)) {
                                row[x] = args.NewColor;
                            }
                        }
                    }
                });
            }

            Log.Debug($"Saving: {fs.Name}");
            fs.Flush();
            fs.Seek(0, System.IO.SeekOrigin.Begin);
            image.Save(fs, format);
            fs.Flush();
            fs.Seek(0, System.IO.SeekOrigin.Begin);
        }
    }

    // https://newbedev.com/compare-rgb-colors-in-c
    private double ColorDifference(Rgba32 color1, Rgba32 color2)
    {
        // Gray Color = .11 * B + .59 * G + .30 * R

        // And your difference will be

        // difference = (GrayColor1 - GrayColor2) * 100.0 / 255.0

        // with difference ranging from 0-100.

        double gray1 = (0.11 * color1.B) + (0.59 * color1.G) + (0.30 * color1.R);
        double gray2 = (0.11 * color2.B) + (0.59 * color2.G) + (0.30 * color2.R);

        return (Math.Abs(gray1 - gray2) * 100.0) / 255.0;
    }
}
