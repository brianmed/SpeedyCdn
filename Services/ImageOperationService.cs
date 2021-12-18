using System.Collections.Concurrent;
using System.Text.Json;

using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.Fonts;
using Serilog.Context;

using SpeedyCdn.Enums;
using SpeedyCdn.Server.Entities.Edge;

public interface IImageOperationService
{
    Task<ImageCacheElementEntity> ImageFromQueryAsync(string _originalCachePath, QueryString queryString, string _imageOpCachePath, string imagePath);
    Task<S3ImageCacheElementEntity> S3ImageFromQueryAsync(string _originalCachePath, QueryString queryString, string _imageOpCachePath, string imagePath);
}

public class ImageOperationService : IImageOperationService
{
    static ConcurrentDictionary<string, bool> InFlightImageOperations = new();

    WebEdgeDbContext WebEdgeDb { get; init; }

    ICacheElementService CacheElementService { get; init; }

    public IQueryStringService QueryStringService { get; init; }

    public ImageOperationService(
            WebEdgeDbContext webEdgeDb,
            ICacheElementService cacheElementService,
            IQueryStringService queryStringService)
    {
        CacheElementService = cacheElementService;

        WebEdgeDb = webEdgeDb;

        QueryStringService = queryStringService;
    }

    async public Task<ImageCacheElementEntity> ImageFromQueryAsync(string _originalCachePath, QueryString queryString, string _imageOpCachePath, string imagePath)
    {
        using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"{nameof(ImageOperationService)}.{nameof(ImageFromQueryAsync)}");

        string originalCachePath = Path.Combine(ConfigCtx.Options.EdgeCacheImagesDirectory, _originalCachePath);
        string imageOpCachePath = Path.Combine(ConfigCtx.Options.EdgeCacheImagesDirectory, _imageOpCachePath);

        ImageCacheElementEntity cacheElement = null;

        try
        {
            SpinWait sw = new SpinWait();

            while (InFlightImageOperations.TryAdd(_imageOpCachePath, true) is false)
            {
                sw.SpinOnce();
            }

            Directory.CreateDirectory(Path.GetDirectoryName(imageOpCachePath));

            string imageOpWithId = Directory.EnumerateFiles(
                Path.GetDirectoryName(imageOpCachePath),
                    $"{Path.GetFileName(imageOpCachePath)}.*", SearchOption.TopDirectoryOnly)
                .SingleOrDefault();

            if (imageOpWithId is not null && File.Exists(imageOpWithId)) {
                if (new FileInfo(imageOpWithId).Length > 0) {
                    Log.Debug($"Image Operation Cache Hit: {imagePath}{queryString}");
                    
                    long imageCacheElementId = long.Parse(Path.GetExtension(Path.GetFileName(imageOpWithId)).Replace(".", String.Empty));

                    cacheElement = await WebEdgeDb.ImageCacheElements
                        .Where(v => v.ImageCacheElementId == imageCacheElementId)
                        .SingleAsync();

                    cacheElement.LastAccessedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    await WebEdgeDb.SaveChangesAsync();

                    return cacheElement;
                } else {
                    Log.Debug($"Zero Byte Image Operation Cache File: {imagePath}{queryString}");
                }
            } else {
                Log.Debug($"No Image Operation Cache File Found: {imagePath}{queryString}");
            }

            await RunAllFromQueryAsync(originalCachePath, queryString, imageOpCachePath, imagePath);

            cacheElement = await CacheElementService.InsertImageAsync(imageOpCachePath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Issue During ImageFromQueryAsync: {originalCachePath} {queryString}");
        }
        finally
        {
            if (InFlightImageOperations.TryRemove(_imageOpCachePath, out bool whence) is false) {
                Log.Error($"Issue Removing {_imageOpCachePath}");
            }
        }

        return cacheElement;
    }

    async public Task<S3ImageCacheElementEntity> S3ImageFromQueryAsync(string _originalCachePath, QueryString queryString, string _s3ImageOpCachePath, string s3ImagePath)
    {
        using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"{nameof(ImageOperationService)}.{nameof(S3ImageFromQueryAsync)}");

        string originalCachePath = Path.Combine(ConfigCtx.Options.EdgeCacheS3ImagesDirectory, _originalCachePath);
        string s3ImageOpCachePath = Path.Combine(ConfigCtx.Options.EdgeCacheS3ImagesDirectory, _s3ImageOpCachePath);

        S3ImageCacheElementEntity cacheElement = null;

        try
        {
            SpinWait sw = new SpinWait();

            while (InFlightImageOperations.TryAdd(_s3ImageOpCachePath, true) is false)
            {
                sw.SpinOnce();
            }

            Directory.CreateDirectory(Path.GetDirectoryName(s3ImageOpCachePath));

            string s3ImageOpWithId = Directory.EnumerateFiles(
                Path.GetDirectoryName(s3ImageOpCachePath),
                    $"{Path.GetFileName(s3ImageOpCachePath)}.*", SearchOption.TopDirectoryOnly)
                .SingleOrDefault();

            if (s3ImageOpWithId is not null && File.Exists(s3ImageOpWithId)) {
                if (new FileInfo(s3ImageOpWithId).Length > 0) {
                    Log.Debug($"S3Image Operation Cache Hit: {s3ImagePath}{queryString}");
                    
                    long s3ImageCacheElementId = long.Parse(Path.GetExtension(Path.GetFileName(s3ImageOpWithId)).Replace(".", String.Empty));

                    cacheElement = await WebEdgeDb.S3ImageCacheElements
                        .Where(v => v.S3ImageCacheElementId == s3ImageCacheElementId)
                        .SingleAsync();

                    return cacheElement;
                } else {
                    Log.Debug($"Zero Byte S3Image Operation Cache File: {s3ImagePath}{queryString}");
                }
            } else {
                Log.Debug($"No S3Image Operation Cache File Found: {s3ImagePath}{queryString}");
            }

            await RunAllFromQueryAsync(originalCachePath, queryString, s3ImageOpCachePath, s3ImagePath);

            cacheElement = await CacheElementService.InsertS3ImageAsync(s3ImageOpCachePath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Issue During S3ImageFromQueryAsync: {originalCachePath} {queryString}");
        }
        finally
        {
            if (InFlightImageOperations.TryRemove(_s3ImageOpCachePath, out bool whence) is false) {
                Log.Error($"Issue Removing {_s3ImageOpCachePath}");
            }
        }

        return cacheElement;
    }

    async public Task RunAllFromQueryAsync(string originalCachePath, QueryString queryString, string imageOpCachePath, string imagePath)
    {
        using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"{nameof(ImageOperationService)}.{nameof(RunAllFromQueryAsync)}");

        try
        {
            using (FileStream sourceStream = new(originalCachePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan))
            using (FileStream destinationStream = new(imageOpCachePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await sourceStream.CopyToAsync(destinationStream);
            }

            Dictionary<string, HashSet<string>> opRequiredParameters = new();

            opRequiredParameters.Add("border", new());
            opRequiredParameters.Add("crop", new());
            opRequiredParameters.Add("flip", new());
            opRequiredParameters.Add("label", new());
            opRequiredParameters.Add("replacecolor", new());
            opRequiredParameters.Add("resize", new());
            opRequiredParameters.Add("rotate", new());
            opRequiredParameters.Add("smartcrop", new());

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
            opRequiredParameters["smartcrop"].Add("smartcrop.WH");

            List<(string Name, List<string> Args)> args = QueryStringService.Args(queryString, opRequiredParameters);

            foreach ((string Name, List<string> Args) arg in args)
            {
                using FileStream imageCacheFS = new FileStream(Path.Combine(ConfigCtx.Options.EdgeCacheImagesDirectory, imageOpCachePath), FileMode.OpenOrCreate);

                ImageOperation imageOperation = ImageOperationFactory(arg.Name);

                imageOperation.Run(imageCacheFS, arg.Args);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Issue During Image Operation: {originalCachePath} {queryString}");

            throw ex;
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

            case "smartcrop":
                return new SmartCropImageOperation();

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
