using System.Text.RegularExpressions;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Runtime.Jit;

/// <summary>
/// Invokes the NVIDIA CUDA Compiler (<c>nvcc</c>) subprocess to compile CUDA C++ files into native Fatbin binaries or shared libraries (.dll/.so).
/// </summary>
/// <remarks>
/// Automates discovery of MSVC build environment (<c>vcvars64.bat</c> via <c>vswhere</c>) and executes <c>nvcc</c> with optimized register budgets.
/// </remarks>
/// <remarks><para>Implements: <b>GEN-44, GEN-45</b></para></remarks>
public sealed partial class NvccCompiler
{
    /// <summary>Gets or sets the filesystem abstraction used for I/O operations.</summary>
    public static System.IO.Abstractions.IFileSystem FileSystem { get; set; } = new System.IO.Abstractions.FileSystem();

    /// <summary>
    /// The exception thrown when an external compiler process exits with a non-zero exit code.
    /// </summary>
    public class ProcessFailedException(string message, string output) : Exception(message)
    {
        /// <summary>Gets the standard output and error captured from the process.</summary>
        public string Output { get; } = output;
    }

    /// <summary>Gets or sets the delegate used to execute external system processes.</summary>
    public static Func<ProcessStartInfo, string> ProcessRunner
    {
        get; set;
    } = psi =>
    {
        using var process = new Process();
        process.StartInfo = psi;
        var outputBuilder = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
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
                process.Kill(true);
            }
            catch (Exception ex1)
            {
                try
                {
                    process.Kill();
                }
                catch (Exception ex2)
                {
                    Logger<NvccCompiler>.Info(
                        $"[JIT] Failed to kill process tree: {ex1.Message}. Fallback to kill process also failed: {ex2.Message}");
                }
            }

            throw new TimeoutException($"Process timed out after 60 seconds. Partial output:\n{outputBuilder}");
        }

        process.WaitForExit(); // Ensure async event handlers finish

        var output = outputBuilder.ToString();
        return process.ExitCode != 0
            ? throw new ProcessFailedException($"Process failed with exit code {process.ExitCode}", output)
            : output;
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
            RedirectStandardError = true, // Required for ProcessRunner
            UseShellExecute = false,
            CreateNoWindow = true
        };

        return ProcessRunner(psi).Trim();
    });

    /// <summary>Gets or sets the factory retrieving the Visual Studio installation directory from vswhere.</summary>
    public static Func<string> GetVswhereOutputFunc { get; set; } = () => s_vswhereCache.Value;

    /// <summary>
    /// Discovers and returns the absolute path to the MSVC <c>vcvars64.bat</c> environment initialization script.
    /// </summary>
    /// <returns>The path to <c>vcvars64.bat</c>.</returns>
    /// <exception cref="Exception">Thrown when Visual Studio or <c>vcvars64.bat</c> is not installed.</exception>
    /// <remarks>
    /// <para><b>Performance:</b> Results are cached in-memory after the first successful resolution via <c>vswhere</c>.</para>
    /// </remarks>
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

    /// <summary>
    /// Discovers and returns the path to the <c>nvcc</c> compiler executable.
    /// </summary>
    /// <returns>The executable path to <c>nvcc</c>.</returns>
    /// <remarks>
    /// <para>Resolution checks the <c>CUDA_PATH</c> environment variable or default installation locations.</para>
    /// </remarks>
    public static string GetNvccPath()
    {
        var cudaPath = CudaEnvironment.GetCudaPath();
        if (!string.IsNullOrEmpty(cudaPath))
        {
            var nvcc = FileSystem.Path.Combine(cudaPath, "bin", OperatingSystem.IsWindows() ? "nvcc.exe" : "nvcc");
            if (FileSystem.File.Exists(nvcc))
            {
                return nvcc;
            }
        }

        return OperatingSystem.IsWindows() ? "nvcc.exe" : "nvcc";
    }

    /// <summary>
    /// Compiles a CUDA source file into a shared native library (.dll).
    /// </summary>
    /// <param name="cuFile">The input <c>.cu</c> source file path.</param>
    /// <param name="dllFile">The destination DLL output path.</param>
    /// <param name="includePath">Optional extra include directory to pass to NVCC.</param>
    /// <param name="tempDir">The working directory for intermediate files.</param>
    /// <exception cref="Exception">Thrown when NVCC compilation fails.</exception>
    /// <remarks>
    /// <para><b>Execution Target:</b> Host-only (CPU). Spawns a synchronous sub-process.</para>
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "UnusedMember.Global",
        Justification = "Public API exposed for runtime consumers")]
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

    /// <summary>
    /// Compiles a CUDA source file into an optimized Fatbin binary and parses compilation resource metrics.
    /// </summary>
    /// <param name="cuFile">The input <c>.cu</c> source file path.</param>
    /// <param name="fatbinFile">The destination <c>.fatbin</c> output path.</param>
    /// <param name="includePath">Optional extra include directory to pass to NVCC.</param>
    /// <param name="tempDir">The working directory for intermediate files.</param>
    /// <param name="smVersion">The target NVIDIA GPU SM architecture (e.g., <c>sm_52</c>, <c>sm_86</c>).</param>
    /// <returns>A <see cref="CompilationResult"/> containing register, shared memory, and constant memory statistics.</returns>
    /// <exception cref="Exception">Thrown when NVCC compilation fails.</exception>
    /// <remarks>
    /// <para><b>Execution Target:</b> Host-only (CPU). Spawns a synchronous sub-process.</para>
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "UnusedMember.Global",
        Justification = "Public API exposed for runtime consumers")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members",
        Justification = "Public API for library")]
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

        Logger<NvccCompiler>.Info($"[JIT] Compiling dynamically generated CUDA code ({logName})...");
        Logger<NvccCompiler>.Info("====== CUDA SOURCE ======");
        var cuSrc = FileSystem.File.ReadAllText(cuFile);
        Logger<NvccCompiler>.Info(cuSrc.AsSpan(0, Math.Min(cuSrc.Length, 2000)).ToString()); // Print first 2000 chars
        Logger<NvccCompiler>.Info("...");
        if (cuSrc.Contains("Kernel_StaticFermatLoop"))
        {
            var match = System.Text.RegularExpressions.Regex.Match(cuSrc,
                @"void Kernel_StaticFermatLoop[\s\S]*?(?=\}\s*extern|\z)");
            if (match.Success)
            {
                Logger<NvccCompiler>.Info(match.Value);
            }
        }

        Logger<NvccCompiler>.Info("=========================");

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

            Logger<NvccCompiler>.Info($"[JIT] Compilation successful in {sw.ElapsedMilliseconds} ms.");
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

        var regMatch = RegistersUsedRegex().Match(logContent);
        if (regMatch.Success)
        {
            result.RegistersUsed = int.Parse(regMatch.Groups[1].Value);
        }

        var smemMatch = SharedMemoryRegex().Match(logContent);
        if (smemMatch.Success)
        {
            result.SharedMemoryBytes = int.Parse(smemMatch.Groups[1].Value);
        }

        var cmemMatch = ConstantMemoryRegex().Match(logContent);
        if (cmemMatch.Success)
        {
            result.ConstantMemoryBytes = int.Parse(cmemMatch.Groups[1].Value);
        }

        return result;
    }

    [GeneratedRegex(@"(\d+) bytes smem")]
    private static partial Regex SharedMemoryRegex();

    [GeneratedRegex(@"(\d+) bytes cmem")]
    private static partial Regex ConstantMemoryRegex();

    [GeneratedRegex(@"Used (\d+) registers")]
    private static partial Regex RegistersUsedRegex();
}

