namespace Kassyi.GSharp.Runtime.Transpiler.Pipeline;

public static class NvccCompiler
{
    public static System.IO.Abstractions.IFileSystem FileSystem { get; set; } = new System.IO.Abstractions.FileSystem();

    public static Action<ProcessStartInfo> ProcessRunner { get; set; } = psi =>
    {
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start process");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new Exception("Process failed");
        }
    };

    public static Func<string> GetVswhereOutputFunc { get; set; } = () =>
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var vswhere = FileSystem.Path.Combine(programFiles, "Microsoft Visual Studio", "Installer", "vswhere.exe");

        if (!FileSystem.File.Exists(vswhere))
        {
            throw new Exception("vswhere.exe not found.");
        }

        var psi = new ProcessStartInfo(vswhere,
            "-latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process =
            Process.Start(psi) ?? throw new InvalidOperationException("Failed to start vswhere process");
        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        return output;
    };

    public static string GetVcvarsPath()
    {
        var output = GetVswhereOutputFunc();

        if (string.IsNullOrEmpty(output))
        {
            throw new Exception("Visual Studio installation not found.");
        }

        var vcvars = FileSystem.Path.Combine(output, "VC", "Auxiliary", "Build", "vcvars64.bat");
        return !FileSystem.File.Exists(vcvars) ? throw new Exception($"vcvars64.bat not found at {vcvars}") : vcvars;
    }

    public static string GetNvccPath()
    {
        var cudaPath = Environment.GetEnvironmentVariable("CUDA_PATH");
        if (!string.IsNullOrEmpty(cudaPath))
        {
            var nvcc = FileSystem.Path.Combine(cudaPath, "bin", "nvcc.exe");
            if (FileSystem.File.Exists(nvcc))
            {
                return nvcc;
            }
        }

        const string Fallback = @"C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v13.3\bin\nvcc.exe";
        return FileSystem.File.Exists(Fallback) ? Fallback : "nvcc.exe";
    }

    // ReSharper disable UnusedMember.Global
    [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members",
        Justification = "Public API for library")]
    public static void Compile(string cuFile, string dllFile, string? includePath, string tempDir)
    {
        RunNvccInternal(cuFile, dllFile, includePath, tempDir, [
            "-shared",
            "-o", dllFile,
            "-lcufft",
            "-O3",
            "-arch=sm_86",
            "-std=c++17",
            "-rdc=true"
        ], $"out_{Guid.CreateVersion7():N}.log");
    }

    public static CompilationResult CompileToFatbin(string cuFile, string fatbinFile, string? includePath,
        string tempDir, string smVersion = "sm_52")
    {
        return RunNvccInternal(cuFile, fatbinFile, includePath, tempDir, [
            "--fatbin",
            "-o", fatbinFile,
            "-O3",
            $"-arch={smVersion}",
            "-std=c++17",
            "--ptxas-options=-v"
        ], $"out_fatbin_{Guid.CreateVersion7():N}.log");
    }

    private static CompilationResult RunNvccInternal(string cuFile, string outFile, string? includePath, string tempDir,
        List<string> specificOpts, string logName)
    {
        var vcvars = GetVcvarsPath();
        var nvcc = GetNvccPath();

        var optsFile = FileSystem.Path.Combine(tempDir, Guid.NewGuid().ToString("N") + ".txt");
        var logFile = FileSystem.Path.Combine(tempDir, logName);

        var opts = new List<string>(specificOpts) { cuFile };
        if (!string.IsNullOrEmpty(includePath))
        {
            opts.Add($"-I{includePath}");
        }

        FileSystem.File.WriteAllLines(optsFile, opts, new UTF8Encoding(false));

        var psi = new ProcessStartInfo("cmd.exe",
            $"/c \"call \"{vcvars}\" && \"{nvcc}\" --options-file \"{optsFile}\" > \"{logFile}\" 2>&1\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = tempDir
        };

        Console.WriteLine($"[JIT] Compiling dynamically generated CUDA code ({logName})...");
        var sw = Stopwatch.StartNew();

        try
        {
            ProcessRunner(psi);
            var logContent = "";
            if (FileSystem.File.Exists(logFile))
            {
                logContent = FileSystem.File.ReadAllText(logFile);
            }

            var result = ParsePtxasOutput(logContent);

            sw.Stop();

            if (!FileSystem.File.Exists(outFile))
            {
                throw new Exception($"JIT Compilation failed! Expected {outFile} to be created.");
            }

            Console.WriteLine($"[JIT] Compilation successful in {sw.ElapsedMilliseconds} ms.");
            return result;
        }
        catch (Exception ex)
        {
            var log = FileSystem.File.Exists(logFile)
                ? FileSystem.File.ReadAllText(logFile)
                : "No log found.";
            throw new Exception($"NVCC Compilation Failed:\n{log}", ex);
        }
        finally
        {
            // Clean up temporary files on best-effort basis; ignore errors if files are locked or already deleted.
            try
            {
                FileSystem.File.Delete(optsFile);
            }
            catch
            {
                // Ignore cleanup errors
            }

            try
            {
                FileSystem.File.Delete(logFile);
            }
            catch
            {
                // Ignore cleanup errors
            }

            if (specificOpts.Contains("-shared"))
            {
                try
                {
                    FileSystem.File.Delete(FileSystem.Path.ChangeExtension(outFile, ".lib"));
                }
                catch
                {
                    // Ignore cleanup errors
                }

                try
                {
                    FileSystem.File.Delete(FileSystem.Path.ChangeExtension(outFile, ".exp"));
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }

    private static CompilationResult ParsePtxasOutput(string logContent)
    {
        var result = new CompilationResult();
        if (string.IsNullOrWhiteSpace(logContent))
        {
            return result;
        }

        var regMatch = System.Text.RegularExpressions.Regex.Match(logContent, @"Used (\d+) registers");
        if (regMatch.Success)
        {
            result.RegistersUsed = int.Parse(regMatch.Groups[1].Value);
        }

        var smemMatch = System.Text.RegularExpressions.Regex.Match(logContent, @"(\d+) bytes smem");
        if (smemMatch.Success)
        {
            result.SharedMemoryBytes = int.Parse(smemMatch.Groups[1].Value);
        }

        var cmemMatch = System.Text.RegularExpressions.Regex.Match(logContent, @"(\d+) bytes cmem");
        if (cmemMatch.Success)
        {
            result.ConstantMemoryBytes = int.Parse(cmemMatch.Groups[1].Value);
        }

        return result;
    }
}

