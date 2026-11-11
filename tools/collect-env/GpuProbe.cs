using System.Globalization;
using static Collector;
using static Privacy;
using static ProcessProbe;

/// <summary>Reads the GPUs from nvidia-smi and the CUDA Toolkit from nvcc.</summary>
internal static class GpuProbe
{
    internal static void CollectGpus(ReportModel model)
    {
        const string queryArgs =
            "--query-gpu=name,compute_cap,memory.total,driver_version --format=csv,noheader";
        string? query = Run("nvidia-smi", queryArgs);
        string driverCuda = ExtractDriverCudaVersion(Run("nvidia-smi", string.Empty));
        foreach (string line in Lines(query))
        {
            string? gpu = ParseGpuLine(line, driverCuda);
            if (gpu is not null)
            {
                model.Gpus.Add(gpu);
            }
        }
    }

    private static string ExtractDriverCudaVersion(string? header)
    {
        if (header is null)
        {
            return string.Empty;
        }

        // Older drivers print "CUDA Version:", newer ones "CUDA UMD Version:".
        string marker = "CUDA UMD Version:";
        int at = header.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0)
        {
            marker = "CUDA Version:";
            at = header.IndexOf(marker, StringComparison.Ordinal);
        }

        if (at < 0)
        {
            return string.Empty;
        }

        string rest = header.Substring(at + marker.Length).TrimStart();
        int end = 0;
        while (end < rest.Length && (char.IsAsciiDigit(rest[end]) || rest[end] == '.'))
        {
            end++;
        }

        return end > 0 ? $" (driver CUDA {rest.Substring(0, end)})" : string.Empty;
    }

    private static string? ParseGpuLine(string line, string driverCuda)
    {
        string[] parts = line.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length < 4)
        {
            return null;
        }

        string sm = parts[1].Replace(".", string.Empty, StringComparison.Ordinal);
        string memory = parts[2];
        if (memory.EndsWith("MiB", StringComparison.Ordinal)
            && double.TryParse(
                memory.Substring(0, memory.Length - 3).Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double mib))
        {
            memory = $"{Math.Round(mib / 1024.0).ToString("0", CultureInfo.InvariantCulture)} GB";
        }

        return Clean($"{parts[0]}, sm_{sm}, {memory}, driver {parts[3]}{driverCuda}");
    }

    internal static string CudaToolkit()
    {
        string? output = QueryNvccOutput();
        return output is null ? NotFound : ParseNvccVersion(output);
    }

    private static string? QueryNvccOutput()
    {
        string exe = OperatingSystem.IsWindows() ? "nvcc.exe" : "nvcc";
        string? output = Run("nvcc", "--version");
        if (output is null)
        {
            string? cudaPath = Environment.GetEnvironmentVariable("CUDA_PATH");
            if (!string.IsNullOrEmpty(cudaPath))
            {
                output = Run(Path.Combine(cudaPath, "bin", exe), "--version");
            }
        }

        if (output is null && !OperatingSystem.IsWindows())
        {
            output = Run("/usr/local/cuda/bin/nvcc", "--version");
        }

        return output;
    }

    private static string ParseNvccVersion(string output)
    {
        foreach (string line in Lines(output))
        {
            const string marker = "release ";
            int at = line.IndexOf(marker, StringComparison.Ordinal);
            if (at >= 0)
            {
                string rest = line.Substring(at + marker.Length);
                int comma = rest.IndexOf(',');
                string release = comma >= 0 ? rest.Substring(0, comma) : rest;
                string build = comma >= 0 ? rest.Substring(comma + 1).Trim() : string.Empty;
                return build.Length > 0 ? $"nvcc {release} ({build})" : $"nvcc {release}";
            }
        }

        return Clean(FirstLine(output));
    }
}
