using System.Collections.Concurrent;
using System.Text.Json;

using Microsoft.AspNetCore.WebUtilities;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.Fonts;
using Serilog.Context;

using SpeedyCdn.Enums;

public interface IImageOperationService
{
    Task RunAllFromQueryAsync(string originalCachePath, string queryString, string imageOpCachePath, string imagePath);
}

public class ImageOperationService : IImageOperationService
{
    static ConcurrentDictionary<string, bool> InFlightImageOperations = new();

    async public Task RunAllFromQueryAsync(string _originalCachePath, string queryString, string _imageOpCachePath, string imagePath)
    {
        using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"{nameof(ImageOperationService)}.{nameof(RunAllFromQueryAsync)}");

        SpinWait sw = new SpinWait();

        while (InFlightImageOperations.TryAdd(_imageOpCachePath, true) is false)
        {
            sw.SpinOnce();
        }

        string originalCachePath = Path.Combine(ConfigCtx.Options.EdgeCacheImagesDirectory, _originalCachePath);
        string imageOpCachePath = Path.Combine(ConfigCtx.Options.EdgeCacheImagesDirectory, _imageOpCachePath);

        Directory.CreateDirectory(Path.GetDirectoryName(imageOpCachePath));

        try
        {
            if (File.Exists(imageOpCachePath)) {
                if (new FileInfo(imageOpCachePath).Length > 0) {
                    Log.Debug($"Image Operation Cache Hit: {imagePath}{queryString}");

                    return;
                } else {
                    Log.Debug($"Zero Byte Image Operation Cache File: {imagePath}{queryString}");
                }
            } else {
                Log.Debug($"No Image Operation Cache File Found: {imagePath}{queryString}");
            }

            using (FileStream sourceStream = new(originalCachePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan))
            using (FileStream destinationStream = new(imageOpCachePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await sourceStream.CopyToAsync(destinationStream);
            }

            FileStream imageCacheFS = new FileStream(Path.Combine(ConfigCtx.Options.EdgeCacheImagesDirectory, imageOpCachePath), FileMode.OpenOrCreate);
            
            List<QueryStringEnumerable.EncodedNameValuePair> queries = new();
            var queryEnumerator = new QueryStringEnumerable(queryString).GetEnumerator();

            while (queryEnumerator.MoveNext())
            {
                queries.Add(queryEnumerator.Current);
            }

            Dictionary<string, HashSet<string>> opRequiredParameters = new();

            opRequiredParameters.Add("border", new());
            opRequiredParameters.Add("crop", new());
            opRequiredParameters.Add("flip", new());
            opRequiredParameters.Add("label", new());
            opRequiredParameters.Add("replacecolor", new());
            opRequiredParameters.Add("resize", new());
            opRequiredParameters.Add("rotate", new());

            opRequiredParameters["border"].Add("border.Color");
            opRequiredParameters["border"].Add("border.Thickness");
            opRequiredParameters["crop"].Add("crop.WH");
            opRequiredParameters["crop"].Add("crop.XY");
            opRequiredParameters["flip"].Add("flip.Mode");
            opRequiredParameters["label"].Add("label.Text");
            opRequiredParameters["replacecolor"].Add("replaceColor.OldColor");
            opRequiredParameters["replacecolor"].Add("replaceColor.NewColor");
            opRequiredParameters["resize"].Add("resize.WH");
            opRequiredParameters["rotate"].Add("rotate.Mode");

            List<QueryStringEnumerable.EncodedNameValuePair> currentOpQueries = new();

            for (int idx = 0; idx < queries.Count(); ++idx)
            {
                currentOpQueries.Clear();

                HashSet<string> foundOpRequiredParameters = new();

                string opName = queries[idx]
                        .DecodeName()
                        .ToString()
                        .Split('.')
                        .First()
                        .ToLower();

                int totalAllowedRequiredParameters = opRequiredParameters[opName].Count();

                for (int jdx = idx; jdx < queries.Count(); ++jdx)
                {
                    string jdxName = queries[jdx].DecodeName().ToString();

                    string jdxOpName = queries[jdx]
                        .DecodeName()
                        .ToString()
                        .Split('.')
                        .First()
                        .ToLower();

                    bool isInCurrentOpSet = false;

                    if (opRequiredParameters[jdxOpName].Contains(jdxName) && totalAllowedRequiredParameters > 0) {
                        --totalAllowedRequiredParameters;

                        isInCurrentOpSet = true;
                    } else if (opRequiredParameters[jdxOpName].Contains(jdxName) is false) {
                        isInCurrentOpSet = true;
                    }

                    if (opName == jdxOpName && isInCurrentOpSet) {
                        currentOpQueries.Add(queries[idx]);

                        ++idx;
                    } else {
                        --idx;

                        break;
                    }
                }

                List<string> args = new();

                Action<FileStream, ImageArgs> opAction = GetImageOperation(opName);

                foreach (QueryStringEnumerable.EncodedNameValuePair query in currentOpQueries)
                {
                    string opSuffix = query.DecodeName().ToString().Split('.').Last();
                    Log.Debug($"Found queryParameter: {opName}.{opSuffix}={query.DecodeValue().ToString()}");
                    args.Add($"{opSuffix}={query.DecodeValue().ToString()}");
                }

                switch (opName)
                {
                    case "border":
                    {
                        BorderImageArgs opArgs = new(args);

                        opAction(imageCacheFS, opArgs);

                        break;
                    }

                    case "crop":
                    {
                        CropImageArgs opArgs = new(args);

                        opAction(imageCacheFS, opArgs);

                        break;
                    }

                    case "flip":
                    {
                        FlipImageArgs opArgs = new(args);

                        opAction(imageCacheFS, opArgs);

                        break;
                    }

                    case "label":
                    {
                        LabelImageArgs opArgs = new(args);

                        opAction(imageCacheFS, opArgs);

                        break;
                    }

                    case "replacecolor":
                    {
                        ReplaceColorImageArgs opArgs = new(args);

                        opAction(imageCacheFS, opArgs);

                        break;
                    }

                    case "resize":
                    {
                        ResizeImageArgs opArgs = new(args);

                        opAction(imageCacheFS, opArgs);

                        break;
                    }

                    case "rotate":
                    {
                        RotateImageArgs opArgs = new(args);

                        opAction(imageCacheFS, opArgs);

                        break;
                    }

                    default:
                        throw new ArgumentException($"Unsupported {opName}");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Issue During Image Operation: {originalCachePath} {queryString}");
        }
        finally
        {
            if (InFlightImageOperations.TryRemove(_imageOpCachePath, out bool whence) is false) {
                Log.Error($"Issue Removing {_imageOpCachePath}");
            }
        }
    }

    public Action<FileStream, ImageArgs> GetImageOperation(string operation)
    {
        switch (operation.ToLower())
        {
            case "border":
                return BorderImageOperation;

            case "crop":
                return CropImageOperation;

            case "flip":
                return FlipImageOperation;

            case "label":
                return LabelImageOperation;

            case "replacecolor":
                return ReplaceColorImageOperation;

            case "resize":
                return ResizeImageOperation;

            case "rotate":
                return RotateImageOperation;

            default:
                throw new ArgumentException($"Unsupported {operation}");
        }
    }

    public void BorderImageOperation(FileStream fs, ImageArgs _border)
    {
        using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"{nameof(BorderImageOperation)}");

        BorderImageArgs args = (BorderImageArgs)_border;

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


    public void CropImageOperation(FileStream fs, ImageArgs _crop)
    {
        using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"{nameof(CropImageOperation)}");

        CropImageArgs args = (CropImageArgs)_crop;

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

    public void LabelImageOperation(FileStream fs, ImageArgs _text)
    {
        using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"{nameof(LabelImageOperation)}");

        LabelImageArgs args = (LabelImageArgs)_text;

        using (Image<Rgba32> image = Image.Load<Rgba32>(fs, out IImageFormat format))
        {
            Font font = SystemFonts.CreateFont(args.FontName, args.FontSize, args.FontStyle);

            FontRectangle sz = TextMeasurer.Measure(args.Text, new SixLabors.Fonts.TextOptions(font));

            // TODO: Make sure we use outlineSize when computing x,y
            //
            // None, Center, North, NorthEast, East, SouthEast, South, SouthWest, West, NorthWest
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

    public void ResizeImageOperation(FileStream fs, ImageArgs _resize)
    {
        using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"{nameof(ResizeImageOperation)}");

        ResizeImageArgs args = (ResizeImageArgs)_resize;

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

    public void ReplaceColorImageOperation(FileStream fs, ImageArgs _replaceColor)
    {
        using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"{nameof(ReplaceColorImageOperation)}");

        ReplaceColorImageArgs args = (ReplaceColorImageArgs)_replaceColor;

        Log.Debug($"{nameof(ReplaceColorImageOperation)}: '{args}'");

        using (Image<Rgba32> image = Image.Load<Rgba32>(fs, out IImageFormat format))
        {
            foreach (int idx in Enumerable.Range(0, image.Height))
            {
                Span<Rgba32> row = image.GetPixelRowSpan(idx);

                for (int rdx = 0; rdx < row.Length; ++rdx)
                {
                    Color color = new(row[rdx]);

                    if (color == args.OldColor || (args.Difference > 0 && ColorDifference(color, args.OldColor) <= args.Difference)) {
                        row[rdx] = args.NewColor;
                    }
                }
            }

            Log.Debug($"Saving: {fs.Name}");
            fs.Flush();
            fs.Seek(0, System.IO.SeekOrigin.Begin);
            image.Save(fs, format);
            fs.Flush();
            fs.Seek(0, System.IO.SeekOrigin.Begin);
        }
    }

    public void FlipImageOperation(FileStream fs, ImageArgs _rotate)
    {
        using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"{nameof(FlipImageOperation)}");

        FlipImageArgs args = (FlipImageArgs)_rotate;

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

    public void RotateImageOperation(FileStream fs, ImageArgs _rotate)
    {
        using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"{nameof(RotateImageOperation)}");

        RotateImageArgs args = (RotateImageArgs)_rotate;

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

public class ImageArgs
{
    public List<string> Args { get; init; }
}

public class BorderImageArgs : ImageArgs
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
        Args = args;

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

public class CropImageArgs : ImageArgs
{
    public int X { get; set; }
    public int Y { get; set; }

    public string Width { get; set; }
    public string Height { get; set; }

    public Rectangle Rectangle { get; set; }

    public CropImageArgs(List<string> args)
    {
        Args = args;

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

public class ResizeImageArgs : ImageArgs
{
    public string Width { get; set; }
    public string Height { get; set; }

    public ResizeImageArgs(List<string> args)
    {
        Args = args;

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

public class FlipImageArgs : ImageArgs
{
    public FlipMode FlipMode { get; set; } = FlipMode.None;

    public FlipImageArgs(List<string> args)
    {
        Args = args;

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

public class RotateImageArgs : ImageArgs
{
    public RotateMode RotateMode { get; set; } = RotateMode.None;

    public RotateImageArgs(List<string> args)
    {
        Args = args;

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

public class LabelImageArgs : ImageArgs
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
        Args = args;

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

public class ReplaceColorImageArgs : ImageArgs
{
    public Color OldColor { get; }

    public Color NewColor { get; }

    public double Difference { get; set; } = 0;

    public ReplaceColorImageArgs(List<string> args)
    {
        Args = args;

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

class Parsers
{
    public static bool HasArg(List<string> args, string name)
    {
        return args.Where(v => v.StartsWith($"{name}=")).Any();
    }

    public static (string, string) ParseWH(List<string> args)
    {
        string process = args.Where(v => v.StartsWith("WH=")).Single().Substring("WH=".Length);

        if (process.Contains('x') is false) {
            // Either empty string or number by itself
            process += "x";
        }

        if (process.StartsWith('x')) {
            process = "0" + process;
        }

        if (process.EndsWith('x')) {
            process += "0";
        }

        var (w, h) = process.Split('x') switch
        {
            var segments when segments.Length == 2 => (segments[0], segments[1]),

            _ => throw new ArgumentException($"Unsupported WH Option")
        };

        return (w, h);
    }

    public static T ParseSingleEnum<T>(List<string> args, string name)
    {
        return (T)Enum.Parse(typeof(T), args
            .Where(v => v.StartsWith($"{name}="))
            .Single()
            .Substring($"{name}=".Length)
            .Replace(" ", String.Empty));
    }

    public static string ParseSingleString(List<string> args, string name)
    {
        return args
            .Where(v => v.StartsWith($"{name}="))
            .Single()
            .Substring($"{name}=".Length);
    }

    public static Single ParseSingleSingle(List<string> args, string name)
    {
        return Single.Parse(args
            .Where(v => v.StartsWith($"{name}="))
            .Single()
            .Substring($"{name}=".Length)
            .Replace(" ", String.Empty));
    }

    public static double ParseSingleDouble(List<string> args, string name)
    {
        return Double.Parse(args
            .Where(v => v.StartsWith($"{name}="))
            .Single()
            .Substring($"{name}=".Length)
            .Replace(" ", String.Empty));
    }

    public static int ParseSingleInteger(List<string> args, string name)
    {
        return Int32.Parse(args
            .Where(v => v.StartsWith($"{name}="))
            .Single()
            .Substring($"{name}=".Length)
            .Replace(" ", String.Empty));
    }

    public static (int, int, int, int) ParseFourIntegers(List<string> args, string name)
    {
        string process = args
            .Where(v => v.StartsWith($"{name}="))
            .Single()
            .Substring($"{name}=".Length)
            .Replace(" ", String.Empty);

        var (one, two, three, four) = process.Split(',') switch
        {
            var segments when segments.Length == 4 => (
                Int32.Parse(segments[0]),
                Int32.Parse(segments[1]),
                Int32.Parse(segments[2]),
                Int32.Parse(segments[3])
            ),

            _ => throw new ArgumentException($"Unsupported {name} Option")
        };

        return (one, two, three, four);
    }

    public static Color ParseColor(List<string> args, string name)
    {
        string process = args
            .Where(v => v.StartsWith($"{name}="))
            .Single()
            .Substring($"{name}=".Length)
            .Replace(" ", String.Empty);

        if (Color.TryParse(process, out Color foundColor)) {
            return foundColor;
        } else if (Color.TryParseHex(process, out Color foundHexColor)) {
            return foundHexColor;
        } else {
            throw new ArgumentException($"Unsupported Color Option");
        }
    }

    public static (int, int) ParseXY(List<string> args)
    {
        string process = args
            .Where(v => v.StartsWith("XY="))
            .Single()
            .Substring("XY=".Length)
            .Replace(" ", String.Empty);

        var (x, y) = process.Split(',') switch
        {
            var segments when segments.Length == 2 => (Int32.Parse(segments[0]), Int32.Parse(segments[1])),

            _ => throw new ArgumentException($"Unsupported XY Option")
        };

        return (x, y);
    }
}
