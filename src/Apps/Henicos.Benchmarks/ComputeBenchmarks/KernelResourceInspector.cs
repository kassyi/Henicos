
namespace Henicos.Benchmarks.ComputeBenchmarks;

/// <summary>
/// Utility to inspect GPU kernel hardware resources such as register allocation,
/// local memory spill sizes, and compiled SASS instruction counts.
/// </summary>
public static class KernelResourceInspector
{
    private static readonly Dictionary<string, int> s_instructionCounts = new(StringComparer.OrdinalIgnoreCase);
    private static bool s_fatbinsInspected;
    private static readonly Lock s_lock = new();

    public static GpuKernelLoader.KernelResourceAttributes? TryGetAttributes(IntPtr hfunc)
    {
        if (hfunc == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return GpuKernelLoader.GetKernelAttributes(hfunc);
        }
        catch
        {
            return null;
        }
    }

    public static int GetInstructionCount(string kernelName)
    {
        if (string.IsNullOrEmpty(kernelName))
        {
            return -1;
        }

        EnsureFatbinsInspected();
        lock (s_lock)
        {
            return s_instructionCounts.GetValueOrDefault(kernelName, -1);
        }
    }

    private static void EnsureFatbinsInspected()
    {
        if (s_fatbinsInspected)
        {
            return;
        }

        lock (s_lock)
        {
            if (s_fatbinsInspected)
            {
                return;
            }

            s_fatbinsInspected = true;

            var baseDir = AppContext.BaseDirectory;
            if (!Directory.Exists(baseDir))
            {
                return;
            }

            var fatbins = Directory.GetFiles(baseDir, "*.gfatbin");
            foreach (var fatbin in fatbins)
            {
                InspectFatbin(fatbin);
            }
        }
    }

    private static void InspectFatbin(string fatbinPath)
    {
        try
        {
            var bytes = File.ReadAllBytes(fatbinPath);
            if (bytes.Length < 32)
            {
                return;
            }

            // Strip trailing 32-byte verification hash
            var payload = new byte[bytes.Length - 32];
            Array.Copy(bytes, payload, payload.Length);

            var tempPath = Path.Combine(Path.GetTempPath(), $"henicos_inspect_{Guid.NewGuid():N}.gfatbin");
            try
            {
                File.WriteAllBytes(tempPath, payload);
                var psi = new ProcessStartInfo("cuobjdump", $"-sass \"{tempPath}\"")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    return;
                }

                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();

                ParseSassOutput(output);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch
                    {
                        /* best effort cleanup */
                    }
                }
            }
        }
        catch
        {
            // Best effort; if cuobjdump is unavailable, instruction count will be -1
        }
    }

    private static void ParseSassOutput(string sassOutput)
    {
        using var reader = new StringReader(sassOutput);
        string? currentFunc = null;

        while (reader.ReadLine() is { } line)
        {
            var funcIdx = line.IndexOf("Function", StringComparison.Ordinal);
            if (funcIdx >= 0)
            {
                var colonIdx = line.IndexOf(':', funcIdx + 8);
                if (colonIdx >= 0)
                {
                    currentFunc = line.Substring(colonIdx + 1).Trim();
                    s_instructionCounts[currentFunc] = 0;
                    continue;
                }
            }

            if (currentFunc == null)
            {
                continue;
            }

            var trimmed = line.TrimStart();
            if (!trimmed.StartsWith("/*", StringComparison.Ordinal))
            {
                continue;
            }

            var endIdx = trimmed.IndexOf("*/", 2, StringComparison.Ordinal);
            if (endIdx <= 2)
            {
                continue;
            }

            var afterComment = trimmed.Substring(endIdx + 2);
            if (afterComment.Length <= 0 || !char.IsWhiteSpace(afterComment[0]))
            {
                continue;
            }

            var instruction = afterComment.TrimStart();
            if (instruction.Length > 0 && (char.IsAsciiLetterUpper(instruction[0]) || char.IsDigit(instruction[0]) ||
                                           instruction[0] == '_' || instruction[0] == '.'))
            {
                s_instructionCounts[currentFunc]++;
            }
        }
    }
}
