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
    Task<ImageCacheElementEntity> ImageFromQueryAsync(ImageCacheElementEntity originaImage, QueryString queryString);
    Task<S3ImageCacheElementEntity> S3ImageFromQueryAsync(S3ImageCacheElementEntity originalImage, QueryString queryString);
}

public class ImageOperationService : IImageOperationService
{
    static ConcurrentDictionary<string, bool> InFlightImageOperations = new();

    WebEdgeDbContext WebEdgeDb { get; init; }

    ICacheElementService CacheElementService { get; init; }

    ICachePathService CachePathService { get; init; }

    public IQueryStringService QueryStringService { get; init; }

    public ImageOperationService(
            WebEdgeDbContext webEdgeDb,
            ICacheElementService cacheElementService,
            ICachePathService cachePathService,
            IQueryStringService queryStringService)
    {
        CacheElementService = cacheElementService;

        CachePathService = cachePathService;

        WebEdgeDb = webEdgeDb;

        QueryStringService = queryStringService;
    }

    async public Task<ImageCacheElementEntity> ImageFromQueryAsync(ImageCacheElementEntity originaImage, QueryString queryString)
    {
        using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"{nameof(ImageOperationService)}.{nameof(ImageFromQueryAsync)}");

        ImageCacheElementEntity cacheElement = null;

        string mutexKey = originaImage.UrlPath;

        string cacheImageOpPathAbs = CachePathService.CachePath(
                ConfigCtx.Options.EdgeCacheImagesDirectory, new[] { originaImage.UrlPath, queryString.ToString() });

        try
        {
            SpinWait sw = new SpinWait();

            while (InFlightImageOperations.TryAdd(mutexKey, true) is false)
            {
                sw.SpinOnce();
            }

            Directory.CreateDirectory(Path.GetDirectoryName(cacheImageOpPathAbs));

            cacheElement = await WebEdgeDb.ImageCacheElements
                .Where(v => v.UrlPath == originaImage.UrlPath)
                .Where(v => v.QueryString == queryString.ToString())
                .SingleOrDefaultAsync();

            if (cacheElement is not null && File.Exists(cacheImageOpPathAbs)) {
                if (new FileInfo(cacheImageOpPathAbs).Length > 0) {
                    Log.Debug($"Cache Hit: {cacheElement.UrlPath}{cacheElement.QueryString} - {cacheElement.ImageCacheElementId}");

                    cacheElement.LastAccessedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    await WebEdgeDb.SaveChangesAsync();

                    return cacheElement;
                } else {
                    Log.Debug($"Zero Byte Image Operation Cache File: {originaImage.UrlPath}{queryString}");
                }
            } else {
                Log.Debug($"No Image Operation Cache File Found: {originaImage.UrlPath}{queryString}");
            }

            await RunAllFromQueryAsync(originaImage, queryString, cacheImageOpPathAbs);

            cacheElement = await CacheElementService.InsertImageAsync(cacheImageOpPathAbs, originaImage.UrlPath, queryString.ToString());
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Issue During ImageFromQueryAsync: {originaImage.UrlPath} {queryString}");
        }
        finally
        {
            if (InFlightImageOperations.TryRemove(mutexKey, out bool whence) is false) {
                Log.Error($"Issue Removing {mutexKey}");
            }
        }

        return cacheElement;
    }

    async public Task<S3ImageCacheElementEntity> S3ImageFromQueryAsync(S3ImageCacheElementEntity originalS3Image, QueryString queryString)
    {
        using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"{nameof(ImageOperationService)}.{nameof(S3ImageFromQueryAsync)}");

        S3ImageCacheElementEntity cacheElement = null;

        string mutexKey = $"{originalS3Image.UrlPath}{queryString}";

        string cacheS3ImageOpAbsolute = CachePathService.CachePath(
            ConfigCtx.Options.EdgeCacheS3ImagesDirectory, 
            new[] { originalS3Image.UrlPath, queryString.ToString() });

        try
        {
            SpinWait sw = new SpinWait();

            while (InFlightImageOperations.TryAdd(mutexKey, true) is false)
            {
                sw.SpinOnce();
            }

            Directory.CreateDirectory(Path.GetDirectoryName(cacheS3ImageOpAbsolute));

            cacheElement = await WebEdgeDb.S3ImageCacheElements
                .Where(v => v.UrlPath == originalS3Image.UrlPath)
                .Where(v => v.QueryString == queryString.ToString())
                .SingleOrDefaultAsync();

            if (cacheElement is not null && File.Exists(cacheS3ImageOpAbsolute)) {
                if (new FileInfo(cacheS3ImageOpAbsolute).Length > 0) {
                    Log.Debug($"Cache Hit: {originalS3Image.UrlPath}{queryString}");
                    
                    cacheElement.LastAccessedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    await WebEdgeDb.SaveChangesAsync();

                    return cacheElement;
                } else {
                    Log.Debug($"Zero Byte Cache File: {originalS3Image.UrlPath}{queryString}");
                }
            } else {
                Log.Debug($"No Cache File Found: {originalS3Image.UrlPath}{queryString}");
            }

            await RunAllFromQueryAsync(originalS3Image, queryString, cacheS3ImageOpAbsolute);

            cacheElement = await CacheElementService.InsertS3ImageAsync(cacheS3ImageOpAbsolute, originalS3Image.UrlPath, queryString.ToString());
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Issue: {originalS3Image.UrlPath}{queryString}");
        }
        finally
        {
            if (InFlightImageOperations.TryRemove(mutexKey, out bool whence) is false) {
                Log.Error($"Issue Removing {mutexKey}");
            }
        }

        return cacheElement;
    }

    async public Task RunAllFromQueryAsync(object originalImage, QueryString queryString, string newImageOpPathAbs)
    {
        using IDisposable logContext = LogContext.PushProperty("WebAppPrefix", $"{nameof(ImageOperationService)}.{nameof(RunAllFromQueryAsync)}");

        try
        {
            string originalCachePath = originalImage.GetType().Name switch
            {
                nameof(ImageCacheElementEntity) => CachePathService.CachePath(originalImage as ImageCacheElementEntity),
                nameof(S3ImageCacheElementEntity) => CachePathService.CachePath(originalImage as S3ImageCacheElementEntity)
            };
                
            using (FileStream sourceStream = new(originalCachePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan))
            using (FileStream destinationStream = new(newImageOpPathAbs, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan))
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
                using FileStream imageCacheFS = new FileStream(newImageOpPathAbs, FileMode.Open);

                ImageOperation imageOperation = ImageOperationFactory(arg.Name);

                imageOperation.Run(imageCacheFS, arg.Args);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Issue During Image Operation: {originalImage.GetType().GetProperty("UrlPath").GetValue(originalImage)} {queryString}");

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
