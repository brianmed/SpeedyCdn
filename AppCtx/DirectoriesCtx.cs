namespace SpeedyCdn.Server.AppCtx;

public class DirectoriesCtx
{
    public static void Provision()
    {
        List<string> directories = new List<string>();

        directories.Add(ConfigCtx.Options.BaseDirectory);
        directories.Add(ConfigCtx.Options.AppDirectory);
        directories.Add(ConfigCtx.Options.LogDirectory);
        if (ConfigCtx.HasEdgeServer) {
            directories.Add(ConfigCtx.Options.EdgeCacheDirectory);
            directories.Add(ConfigCtx.Options.EdgeCacheBarcodesDirectory);
            directories.Add(ConfigCtx.Options.EdgeCacheImagesDirectory);
            directories.Add(ConfigCtx.Options.EdgeCacheS3ImagesDirectory);
            directories.Add(ConfigCtx.Options.EdgeCacheStaticDirectory);
            directories.Add(ConfigCtx.Options.EdgeAppDbDirectory);
        }
        if (ConfigCtx.HasOriginServer) {
            directories.Add(ConfigCtx.Options.OriginAppDbDirectory);
            directories.Add(ConfigCtx.Options.OriginSourceDirectory);
            directories.Add(ConfigCtx.Options.OriginSourceImagesDirectory);
            directories.Add(ConfigCtx.Options.OriginSourceStaticDirectory);
        }

        if (IsProvisioned(directories) is false)
        {
            foreach (string directory in directories)
            {
                if (Directory.Exists(directory) is false)
                {
                    Directory.CreateDirectory(directory);
                }
            }
        }
    }

    private static bool IsProvisioned(List<string> directories)
    {
        foreach (string directory in directories)
        {
            if (Directory.Exists(directory) is false) {
                return false;
            }
        }

        return true;
    }
}
