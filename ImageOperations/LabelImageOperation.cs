using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Processors.Text;

public class LabelImageArgs
{
    public string Text { get; init; }

    public string FontName { get; init; } = "Arial";

    public FontStyle FontStyle { get; init; } = FontStyle.Regular;

    public int FontSize { get; init; } = 18;

    public Color TextColor { get; init; } = Color.Black;

    public Color? OutlineColor { get; init; } = null;

    public int OutlineSize { get; init; } = 1;

    public Gravity Gravity { get; init; } = Gravity.None;

    public int X { get; set; } = 0;

    public int Y { get; set; } = 0;

    public LabelImageArgs(List<string> args)
    {
        Text = Parsers.ParseSingleString(args, "Text");

        if (Parsers.HasArg(args, "FontName")) {
            FontName = Parsers.ParseSingleString(args, "FontName");
        }

        if (Parsers.HasArg(args, "FontSize")) {
            FontSize = Parsers.ParseSingleInteger(args, "FontSize");
        }

        if (Parsers.HasArg(args, "FontStyle")) {
            FontStyle = Parsers.ParseSingleEnum<FontStyle>(args, "FontStyle");
        }

        if (Parsers.HasArg(args, "TextColor")) {
            TextColor = Parsers.ParseColor(args, "TextColor");
        }

        if (Parsers.HasArg(args, "OutlineColor")) {
            OutlineColor = Parsers.ParseColor(args, "OutlineColor");
        }

        if (Parsers.HasArg(args, "OutlineSize")) {
            OutlineSize = Parsers.ParseSingleInteger(args, "OutlineSize");
        }

        if (Parsers.HasArg(args, "Gravity")) {
            Gravity = Parsers.ParseSingleEnum<Gravity>(args, "Gravity");
        }

        if (Parsers.HasArg(args, "XY")) {
            (X, Y) = Parsers.ParseXY(args);
        }
    }

    public override string ToString()
    {
        return JsonSerializer.Serialize(new
        {
            Text = Text,
            FontName = FontName,
            FontSize = FontSize,
            TextColor = TextColor,
            OutlineColor = OutlineColor,
            OutlineSize = OutlineSize,
            Gravity = Gravity.ToString(),
            X = X,
            Y = Y
        });
    }
}

public class LabelImageOperation : ImageOperation
{
    public override void Run(FileStream fs, List<string> _args)
    {
        using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"{nameof(LabelImageOperation)}");

        LabelImageArgs args = new(_args);

        using (Image<Rgba32> image = Image.Load<Rgba32>(fs, out IImageFormat format))
        {
            Font font = SystemFonts.CreateFont(args.FontName, args.FontSize, args.FontStyle);

            FontRectangle sz = TextMeasurer.Measure(args.Text, new SixLabors.Fonts.TextOptions(font));

            switch (args.Gravity)
            {
                case Gravity.Center:
                    args.X = (int)((image.Width / 2) - (sz.Width / 2));
                    args.Y = (int)((image.Height / 2) - (sz.Height / 2));

                    break; 

                case Gravity.North:
                    args.X = (int)((image.Width / 2) - (sz.Width / 2));
                    args.Y = 0;

                    break; 

                case Gravity.NorthEast:
                    args.X = (int)(image.Width - sz.Width);
                    args.Y = 0;

                    break;

                case Gravity.East:
                    args.X = (int)(image.Width - sz.Width);
                    args.Y = (int)((image.Height / 2) - (sz.Height / 2));

                    break;

                case Gravity.SouthEast:
                    args.X = (int)(image.Width - sz.Width);
                    args.Y = (int)(image.Height - sz.Height);

                    break;

                case Gravity.South:
                    args.X = (int)((image.Width / 2) - (sz.Width / 2));
                    args.Y = (int)(image.Height - sz.Height);

                    break;

                case Gravity.SouthWest:
                    args.X = 0;
                    args.Y = (int)(image.Height - sz.Height);

                    break;

                case Gravity.West:
                    args.X = 0;
                    args.Y = (int)((image.Height / 2) - (sz.Height / 2));

                    break;

                case Gravity.NorthWest:
                    args.X = 0;
                    args.Y = 0;

                    break;
            }

            Log.Debug($"{nameof(LabelImageOperation)}: '{args}'");

            image.Mutate(ctx =>
            {
                if (args.OutlineColor.HasValue) {
                    ctx.DrawText(args.Text, font, Brushes.Solid(args.TextColor), Pens.Solid(args.OutlineColor.Value, args.OutlineSize), new PointF(args.X, args.Y));
                } else {
                    ctx.DrawText(args.Text, font, Brushes.Solid(args.TextColor), new PointF(args.X, args.Y));
                }
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
