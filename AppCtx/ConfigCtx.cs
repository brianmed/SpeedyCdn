using System.Net;

using Microsoft.Extensions.PlatformAbstractions;

using CommandLine;
using CommandLine.Text;

namespace SpeedyCdn.Server.AppCtx;

public class Options
{
    [Option("baseDirectory", Required = false, HelpText = $"{nameof(SpeedyCdn)} BaseDirectory")]
    public string BaseDirectory { get; internal set; } = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) ?? ".";

    [Option("originUrls", Required = false, HelpText = "Origin Urls Specifying the Addresses to Listen on (eg http://*:8080)")]
    public string? OriginUrls { get; internal set; }

    // 

    private string _originSourceDirectory;
    [Option("originSourceDirectory", Required = false, HelpText = "Directory for Source Files")]
    public string OriginSourceDirectory
    {
        get
        {
            if (_originSourceDirectory is null) {
                return Path.Combine(AppDirectory, "OriginSource");
            } else {
                return _originSourceDirectory;
            }
        }

        private set
        {
            _originSourceDirectory = value;
        }
    }

    private string _originSourceImagesDirectory;
    [Option("originSourceImagesDirectory", Required = false, HelpText = "Directory for Source Images")]
    public string OriginSourceImagesDirectory
    {
        get
        {
            if (_originSourceImagesDirectory is null) {
                return Path.Combine(OriginSourceDirectory, "Images");
            } else {
                return _originSourceImagesDirectory;
            }
        }

        private set
        {
            _originSourceImagesDirectory = value;
        }
    }

    private string _originSourceStaticDirectory;
    [Option("originSourceStaticDirectory", Required = false, HelpText = "Directory for Source Static Files")]
    public string OriginSourceStaticDirectory
    {
        get
        {
            if (_originSourceStaticDirectory is null) {
                return Path.Combine(OriginSourceDirectory, "Static");
            } else {
                return _originSourceStaticDirectory;
            }
        }

        private set
        {
            _originSourceStaticDirectory = value;
        }
    }

    [Option("originEnableSensitiveLogging", Required = false, HelpText = "Origin Log Sensitive Information to Sql Log")]
    public bool OriginEnableSensitiveLogging { get; internal set; }

    [Option("originS3AccessKey", Required = false, HelpText = "Origin S3 Access Key")]
    public string OriginS3AccessKey { get; internal set; }

    [Option("originS3SecretKey", Required = false, HelpText = "Origin S3 Secret Key")]
    public string OriginS3SecretKey { get; internal set; }

    [Option("originS3ServiceUrl", Required = false, HelpText = "Origin S3 Service Url")]
    public string OriginS3ServiceUrl { get; internal set; }

    [Option("originShowKeys", Required = false, HelpText = "Origin Show Api Key and Signature Key")]
    public bool OriginShowKeys { get; internal set; }

    [Option("edgeUrls", Required = false, HelpText = "Edge Urls Specifying the Addresses to Listen on (eg http://*:80)")]
    public string EdgeUrls { get; internal set; }

    [Option("edgeOriginUrl", Required = false, HelpText = "Absolute Url of Origin used by Api in Edge")]
    public string EdgeOriginUrl { get; private set; }

    private string _edgeCacheDirecotry;
    [Option("edgeCacheDirectory", Required = false, HelpText = "Edge Cache Base Directory")]
    public string EdgeCacheDirectory
    {
        get
        {
            if (_edgeCacheDirecotry is null) {
                return EdgeCacheDirectory = Path.Combine(AppDirectory, "EdgeCache");
            } else {
                return _edgeCacheDirecotry;
            }
        }

        private set
        {
            _edgeCacheDirecotry = value;
        }
    }

    private string _edgeCacheBarcodesDirecotry;
    [Option("edgeCacheBarcodesDirectory", Required = false, HelpText = "Edge Directory for Cached Barcodes (can be relative)")]
    public string EdgeCacheBarcodesDirectory
    {
        get
        {
            if (_edgeCacheBarcodesDirecotry is null) {
                return Path.Combine(EdgeCacheDirectory, "Barcodes");
            } else {
                return _edgeCacheBarcodesDirecotry;
            }
        }

        private set
        {
            _edgeCacheImagesDirecotry = value;
        }
    }

    private string _edgeCacheImagesDirecotry;
    [Option("edgeCacheImagesDirectory", Required = false, HelpText = "Edge Directory for Cached Images (can be relative)")]
    public string EdgeCacheImagesDirectory
    {
        get
        {
            if (_edgeCacheImagesDirecotry is null) {
                return Path.Combine(EdgeCacheDirectory, "Images");
            } else {
                return _edgeCacheImagesDirecotry;
            }
        }

        private set
        {
            _edgeCacheImagesDirecotry = value;
        }
    }

    private string _edgeCacheStaticDirecotry;
    [Option("edgeCacheStaticDirectory", Required = false, HelpText = "Edge Directory for Cached Static Files (can be relative)")]
    public string EdgeCacheStaticDirectory
    {
        get
        {
            if (_edgeCacheStaticDirecotry is null) {
                return Path.Combine(EdgeCacheDirectory, "Static");
            } else {
                return _edgeCacheStaticDirecotry;
            }
        }

        private set
        {
            _edgeCacheStaticDirecotry = value;
        }
    }

    private string _edgeCacheS3ImagesDirecotry;
    [Option("edgeCacheS3ImagesDirectory", Required = false, HelpText = "Edge Directory for Cached S3 Images (can be relative)")]
    public string EdgeCacheS3ImagesDirectory
    {
        get
        {
            if (_edgeCacheS3ImagesDirecotry is null) {
                return Path.Combine(EdgeCacheDirectory, "S3", "Images");
            } else {
                return _edgeCacheS3ImagesDirecotry;
            }
        }

        private set
        {
            _edgeCacheS3ImagesDirecotry = value;
        }
    }

    [Option("edgeCacheInBytes", Required = false, HelpText = "Allowed Size of Cache in Bytes")]
    public long EdgeCacheInBytes
    {
        get;

        private set;
    }

    [Option("edgeEnableSensitiveLogging", Required = false, HelpText = "Edge Log Sensitive Information to Sql Log")]
    public bool EdgeEnableSensitiveLogging { get; internal set; }

    [Option("edgeOriginApiKey", Required = false, HelpText = "Origin Api Key Utilized by the Edge")]
    public string EdgeOriginApiKey { get; internal set; }

    [Option("edgeOriginSignatureKey", Required = false, HelpText = "Origin Signature Key Utilized by the Edge")]
    public string EdgeOriginSignatureKey { get; internal set; }

    [Option("edgeNumberOfImageOps", Required = false, HelpText = "Number of Concurrent Image Operations")]
    public int EdgeNumberOfImageOps { get; private set; }

    // 

    [Option("version", Required = false, HelpText = "Version Information")]
    public bool Version { get; internal set; }

    public string AppName
    {
        get
        {
            return nameof(SpeedyCdn);
        }
    }

    public string AppDirectory
    {
        get
        {
            return Path.Combine(BaseDirectory, AppName);
        }
    }

    public string LogDirectory
    {
        get
        {
            return Path.Combine(AppDirectory, "Log");
        }
    }

    public string EdgeAppDbDirectory
    {
        get
        {
            return Path.Combine(AppDirectory, "AppDb", "Edge");
        }
    }

    public string EdgeAppDbFile
    {
        get
        {
            return Path.Combine(EdgeAppDbDirectory, $"{nameof(SpeedyCdn)}.sqlite");
        }
    }

    public string EdgeAppDbConnectionString
    {
        get
        {
            return $"Data Source={ConfigCtx.Options.EdgeAppDbFile};";
        }
    }

    public string OriginAppDbDirectory
    {
        get
        {
            return Path.Combine(AppDirectory, "AppDb", "Origin");
        }
    }

    public string OriginAppDbFile
    {
        get
        {
            return Path.Combine(OriginAppDbDirectory, $"{nameof(SpeedyCdn)}.sqlite");
        }
    }

    public string OriginAppDbConnectionString
    {
        get
        {
            return $"Data Source={ConfigCtx.Options.OriginAppDbFile};";
        }
    }

    public Options()
    {
        EdgeNumberOfImageOps = Environment.ProcessorCount;

        EdgeCacheInBytes = 1_073_741_824L * 5L;

        EdgeUrls = "http://*:8080";
        EdgeOriginUrl = "http://localhost:8000";
        OriginUrls = "http://*:8000";
    }
}

public static class ConfigCtx
{
    public static Options Options { get; private set; }

    public static bool HasEdgeServer { get; private set; }

    public static bool HasOriginServer { get; private set; }

    public static void ParseOptions(string[] args)
    {
        try
        {
            Parser parser = new Parser(with => with.HelpWriter = null);
            ParserResult<Options> result = parser.ParseArguments<Options>(args);

            result
                .WithParsed<Options>(options =>
                {
                    ConfigCtx.Options = options.Adapt<Options>();
                })
                .WithNotParsed(errors => DisplayHelp(result, errors));

            if (String.IsNullOrWhiteSpace(ConfigCtx.Options.EdgeUrls) is false) {
                if (ConfigCtx.Options.OriginShowKeys is false) {
                    ConfigCtx.HasEdgeServer = true;
                }
            }

            if (String.IsNullOrWhiteSpace(ConfigCtx.Options.OriginUrls) is false) {
                ConfigCtx.HasOriginServer = true;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            Console.WriteLine($"Try '{PlatformServices.Default.Application.ApplicationName} --help' for more information.");

            Environment.Exit(1);
        }   
    }

    static int DisplayHelp<T>(ParserResult<T> result, IEnumerable<Error> errors)
    {
        if (errors.Any(e => e.Tag == ErrorType.VersionRequestedError))
        {
            Console.WriteLine($"{nameof(SpeedyCdn)} Copyright (C) 2021 Sparks and Magic LLC");

            // Version Information
            Console.WriteLine($"{nameof(SpeedyCdn)} Version [Community Support]: {WhenBuilt.ItWas.ToString("s")}");
            Console.WriteLine($"{nameof(SpeedyCdn)} https://github.com/brianmed/SpeedyCdn");
            //  
        }
        else
        {
            HelpText helpText = HelpText.AutoBuild(result, h =>
                {
                    h.Copyright = $"{nameof(SpeedyCdn)} Copyright (C) 2021 Sparks and Magic LLC";

                    h.AutoVersion = false;

                    h.Heading = String.Empty;

                    h.AddDashesToOption = true;

                    h.AddEnumValuesToHelpText = true;

                    return HelpText.DefaultParsingErrorsHandler(result, h);
                },
                e =>
                {
                    return e;
                },
                verbsIndex: true);

            helpText.OptionComparison = HelpText.RequiredThenAlphaComparison;

            Console.WriteLine(helpText);
        }

        Environment.Exit(1);

        return 1;
    }
}
