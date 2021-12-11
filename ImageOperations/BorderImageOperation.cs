public class BorderImageArgs
{
    public Color Color { get; init; }

    public int LengthLeft { get; init; }
    public int LengthTop { get; init; }
    public int LengthRight { get; init; }
    public int LengthBottom { get; init; }

    public BorderPlacement Placement { get; init; } = BorderPlacement.Extend;

    public Single Blend { get; init; } = 1.0F;

    public BorderImageArgs(List<string> args)
    {
        Color = Parsers.ParseColor(args, "Color");

        if (Parsers.HasArg(args, "Blend")) {
            Blend = Parsers.ParseSingleSingle(args, "Blend");
        }

        if (Parsers.HasArg(args, "Placement")) {
            Placement = Parsers.ParseSingleEnum<BorderPlacement>(args, "Placement");
        }

        (LengthLeft, LengthTop, LengthRight, LengthBottom) = Parsers.ParseFourIntegers(args, "Thickness");
    }

    public override string ToString()
    {
        return $"Color={Color}, Thickness=Left {LengthLeft}, Top {LengthTop}, Right {LengthRight}, Bottom {LengthBottom}";

        return JsonSerializer.Serialize(new
        {
            Color = Color,
            Placement = Placement,
            Blend = Blend,
            LengthLeft = LengthLeft,
            LengthTop = LengthTop,
            LengthRight = LengthRight,
            LengthBottom = LengthBottom
        });
    }
}

public class BorderImageOperation : ImageOperation
{
    public override void Run(FileStream fs, List<string> _args)
    {
        using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"{nameof(BorderImageOperation)}");

        BorderImageArgs args = new(_args);

        Log.Debug($"{nameof(BorderImageOperation)}: '{args}'");

        using (Image<Rgba32> image = Image.Load<Rgba32>(fs, out IImageFormat format))
        {
            int width = args.Placement == BorderPlacement.Extend ?
                image.Width + args.LengthLeft + args.LengthRight :
                image.Width;

            int height = args.Placement == BorderPlacement.Extend ?
                image.Height + args.LengthTop + args.LengthBottom :
                image.Height;

            using Image<Rgba32> imageWithBorder = new Image<Rgba32>(width, height);

            imageWithBorder.Mutate(ctx =>
            {
                if (args.Placement == BorderPlacement.Extend) {
                    ctx.DrawImage(image, new Point(args.LengthLeft, args.LengthTop), 1.0F);
                } else {
                    ctx.DrawImage(image, new Point(0, 0), 1.0F);
                }
            });

            DrawingOptions drawingOptions = new();
            drawingOptions.GraphicsOptions.BlendPercentage = args.Blend;

            int borderLeftWidth = args.LengthLeft;
            int borderTopWidth = width;
            int borderRightWidth = args.LengthRight;
            int borderBottomWidth = width;

            int borderLeftHeight = height - args.LengthTop;
            int borderTopHeight = args.LengthTop;
            int borderRightHeight = height - args.LengthTop - args.LengthBottom;
            int borderBottomHeight = args.LengthBottom;

            if (args.LengthLeft > 0) {
                imageWithBorder.Mutate(ctx =>
                {
                    RectangleF rectangle = new(0, 0, borderLeftWidth, borderLeftHeight);

                    ctx.Fill(drawingOptions, args.Color, rectangle);
                });
            }

            if (args.LengthTop > 0) {
                imageWithBorder.Mutate(ctx =>
                {
                    RectangleF rectangle = new(args.LengthLeft, 0, borderTopWidth, borderTopHeight);

                    ctx.Fill(drawingOptions, args.Color, rectangle);
                });
            }

            if (args.LengthRight > 0) {
                imageWithBorder.Mutate(ctx =>
                {
                    RectangleF rectangle = new(width - args.LengthRight, 0 + args.LengthTop, borderRightWidth, borderRightHeight);

                    ctx.Fill(drawingOptions, args.Color, rectangle);
                });
            }

            if (args.LengthBottom > 0) {
                imageWithBorder.Mutate(ctx =>
                {
                    RectangleF rectangle = new(0, height - args.LengthBottom, borderTopWidth, borderTopHeight);

                    ctx.Fill(drawingOptions, args.Color, rectangle);
                });
            }

            Log.Debug($"Saving: {fs.Name}");
            fs.Flush();
            fs.Seek(0, System.IO.SeekOrigin.Begin);
            imageWithBorder.Save(fs, format);
            fs.Flush();
            fs.Seek(0, System.IO.SeekOrigin.Begin);
        }
    }
}
