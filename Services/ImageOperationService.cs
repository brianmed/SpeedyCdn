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
            QueryStringEnumerable.Enumerator queryEnumerator = new QueryStringEnumerable(queryString).GetEnumerator();

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

                ImageOperation imageOperation = ImageOperationFactory(opName);

                foreach (QueryStringEnumerable.EncodedNameValuePair query in currentOpQueries)
                {
                    string opSuffix = query.DecodeName().ToString().Split('.').Last();
                    Log.Debug($"Found queryParameter: {opName}.{opSuffix}={query.DecodeValue().ToString()}");
                    args.Add($"{opSuffix}={query.DecodeValue().ToString()}");
                }

                imageOperation.Run(imageCacheFS, args);
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

    private ImageOperation ImageOperationFactory(string operation)
    {
        switch (operation.ToLower())
        {
            case "border":
                return new BorderImageOperation();

            case "crop":
                return new CropImageOperation();

            case "flip":
                return new FlipImageOperation();

            case "label":
                return new LabelImageOperation();

            case "replacecolor":
                return new ReplaceColorImageOperation();

            case "resize":
                return new ResizeImageOperation();

            case "rotate":
                return new RotateImageOperation();

            default:
                throw new ArgumentException($"Unsupported {operation}");
        }
    }
}

public abstract class ImageOperation
{
    public abstract void Run(FileStream fs, List<string> args);
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
