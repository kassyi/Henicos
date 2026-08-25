namespace Kassyi.GSharp.Core;

/// <summary>
/// CUDA Toolkit のインストールパスや環境変数を解決するための共通ユーティリティ。
/// アプリケーション全体で CUDA のパス解決ロジックを統一するために使用します。
/// </summary>
public static class CudaEnvironment
{
    /// <summary>
    /// システム上の CUDA インストールパスを取得します。
    /// 環境変数 CUDA_PATH が設定されていない場合は、OS の標準的なインストール先から最新バージョンを探索します。
    /// </summary>
    public static string? GetCudaPath()
    {
        var cudaPath = Environment.GetEnvironmentVariable("CUDA_PATH");
        if (!string.IsNullOrEmpty(cudaPath) && Directory.Exists(cudaPath))
        {
            return cudaPath;
        }

        if (OperatingSystem.IsWindows())
        {
            const string BaseDir = @"C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA";
            if (!Directory.Exists(BaseDir))
            {
                return null;
            }

            var subDirs = Directory.GetDirectories(BaseDir, "v*");
            if (subDirs.Length > 0)
            {
                return subDirs
                    .Select(d => new
                    {
                        Path = d,
                        Version = Version.TryParse(Path.GetFileName(d).TrimStart('v'), out var v)
                            ? v
                            : new Version(0, 0)
                    })
                    .OrderByDescending(x => x.Version)
                    .Select(x => x.Path)
                    .FirstOrDefault();
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            if (Directory.Exists("/usr/local/cuda"))
            {
                return "/usr/local/cuda";
            }
        }

        return null;
    }

    /// <summary>
    /// CUDA Toolkit の bin ディレクトリのパスを取得します。
    /// 見つからない場合は null を返します。
    /// </summary>
    public static string? GetCudaBinPath()
    {
        var cudaPath = GetCudaPath();
        if (cudaPath != null)
        {
            var binPath = Path.Combine(cudaPath, "bin");
            if (OperatingSystem.IsWindows())
            {
                var binX64 = Path.Combine(binPath, "x64");
                if (Directory.Exists(binX64))
                {
                    return binX64;
                }
            }

            if (Directory.Exists(binPath))
            {
                return binPath;
            }
        }

        return null;
    }
}
