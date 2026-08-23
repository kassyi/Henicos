namespace Kassyi.GSharp.Runtime.Jit;

public static class NvccCompiler
{
    public static System.IO.Abstractions.IFileSystem FileSystem { get; set; } = new System.IO.Abstractions.FileSystem();

    public class ProcessFailedException(string message, string output) : Exception(message)
    {
        public string Output { get; } = output;
    }

    public static Func<ProcessStartInfo, string> ProcessRunner
    {
        get; set;
    } = psi =>
    {
        using var process = new Process { StartInfo = psi };
        var outputBuilder = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                lock (outputBuilder)
                {
                    outputBuilder.AppendLine(e.Data);
                }
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null)
            {
                return;
            }

            lock (outputBuilder)
            {
                outputBuilder.AppendLine(e.Data);
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start process");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit(60000))
        {
            try
            {
                process.Kill();
            }
            catch
            {
                // Ignore kill errors on best-effort basis
            }
            throw new TimeoutException($"Process timed out after 60 seconds. Partial output:\n{outputBuilder}");
        }
        process.WaitForExit(); // Ensure async event handlers finish

        var output = outputBuilder.ToString();
        if (process.ExitCode != 0)
        {
            throw new ProcessFailedException($"Process failed with exit code {process.ExitCode}", output);
        }

        return output;
    };

    private static readonly Lazy<string> s_vswhereCache = new(() =>
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
        if (!process.WaitForExit(10000))
        {
            try { process.Kill(); } catch { /* Ignore cleanup errors on best-effort basis */ }
            throw new TimeoutException("vswhere.exe timed out.");
        }
        var output = process.StandardOutput.ReadToEnd().Trim();
        return output;
    });

    public static Func<string> GetVswhereOutputFunc { get; set; } = () => s_vswhereCache.Value;



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
                "-rdc=true",
                "-DCCCL_IGNORE_MSVC_TRADITIONAL_PREPROCESSOR_WARNING"
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
                "--ptxas-options=-v",
                "-maxrregcount=64",
                "-DCCCL_IGNORE_MSVC_TRADITIONAL_PREPROCESSOR_WARNING"
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
            $"/c \"call \"{vcvars}\" && \"{nvcc}\" --options-file \"{optsFile}\"\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = tempDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        Console.WriteLine($"[JIT] Compiling dynamically generated CUDA code ({logName})...");
        Console.WriteLine("====== CUDA SOURCE ======");
        var cuSrc = FileSystem.File.ReadAllText(cuFile);
        Console.WriteLine(cuSrc.Substring(0, Math.Min(cuSrc.Length, 2000))); // Print first 2000 chars
        Console.WriteLine("...");
        if (cuSrc.Contains("Kernel_StaticFermatLoop"))
        {
            var match = System.Text.RegularExpressions.Regex.Match(cuSrc, @"void Kernel_StaticFermatLoop[\s\S]*?(?=\}\s*extern|\z)");
            if (match.Success)
            {
                Console.WriteLine(match.Value);
            }
        }
        Console.WriteLine("=========================");

        var sw = Stopwatch.StartNew();

        try
        {
            var logContent = ProcessRunner(psi);
            FileSystem.File.WriteAllText(logFile, logContent, new UTF8Encoding(false));

            var result = ParsePtxasOutput(logContent);

            sw.Stop();

            if (!FileSystem.File.Exists(outFile))
            {
                throw new Exception($"JIT Compilation failed! Expected {outFile} to be created.");
            }

            Console.WriteLine($"[JIT] Compilation successful in {sw.ElapsedMilliseconds} ms.");
            return result;
        }
        catch (ProcessFailedException ex)
        {
            FileSystem.File.WriteAllText(logFile, ex.Output, new UTF8Encoding(false));
            throw new Exception($"NVCC Compilation Failed:\n{ex.Output}", ex);
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
            var filesToClean = new List<string> { optsFile, logFile };
            if (specificOpts.Contains("-shared"))
            {
                filesToClean.Add(FileSystem.Path.ChangeExtension(outFile, ".lib"));
                filesToClean.Add(FileSystem.Path.ChangeExtension(outFile, ".exp"));
            }

            foreach (var file in filesToClean)
            {
                try
                {
                    FileSystem.File.Delete(file);
                }
                catch
                {
                    // Ignore cleanup errors on best-effort basis
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

