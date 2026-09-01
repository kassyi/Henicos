using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Kassyi.GSharp.Runtime.Jit;

/// <summary>
/// Invokes the MSVC Compiler (<c>cl.exe</c>) subprocess to compile Standard C++ files into shared libraries (.dll).
/// </summary>
public sealed class MsvcCompiler
{
    public static System.IO.Abstractions.IFileSystem FileSystem { get; set; } = new System.IO.Abstractions.FileSystem();

    public class ProcessFailedException(string message, string output) : Exception(message)
    {
        public string Output { get; } = output;
    }

    public static Func<ProcessStartInfo, string> ProcessRunner { get; set; } = psi =>
    {
        using var process = new Process();
        process.StartInfo = psi;
        var outputBuilder = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (outputBuilder) outputBuilder.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (outputBuilder) outputBuilder.AppendLine(e.Data); };

        if (!process.Start()) throw new InvalidOperationException("Failed to start process");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit(60000))
        {
            try { process.Kill(true); } catch { try { process.Kill(); } catch { } }
            throw new TimeoutException($"Process timed out after 60 seconds. Partial output:\n{outputBuilder}");
        }

        process.WaitForExit();
        var output = outputBuilder.ToString();
        return process.ExitCode != 0
            ? throw new ProcessFailedException($"Process failed with exit code {process.ExitCode}", output)
            : output;
    };

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public static void Compile(string cppFile, string dllFile, string? includePath, string tempDir)
    {
        var vcvars = NvccCompiler.GetVcvarsPath();

        var logName = $"out_msvc_{Guid.CreateVersion7():N}.log";
        var logFile = FileSystem.Path.Combine(tempDir, logName);
        var pdbFile = FileSystem.Path.ChangeExtension(dllFile, ".pdb");

        var opts = new List<string>
        {
            "/LD", // Create DLL
            "/O2", // Optimize
            "/EHsc", // Exceptions
            "/std:c++17", // C++17
            $"/Fe:\"{dllFile}\"", // Output file
            $"/Fd:\"{pdbFile}\"", // PDB file
            $"\"{cppFile}\"" // Input
        };

        if (!string.IsNullOrEmpty(includePath))
        {
            opts.Add($"/I\"{includePath}\"");
        }

        var cmdLine = string.Join(" ", opts);
        var psi = new ProcessStartInfo("cmd.exe", $"/c \"call \"{vcvars}\" && cl.exe {cmdLine}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = tempDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        Logger<MsvcCompiler>.Info($"[JIT] Compiling dynamically generated C++ code ({logName}) via MSVC...");
        var sw = Stopwatch.StartNew();

        try
        {
            var logContent = ProcessRunner(psi);
            FileSystem.File.WriteAllText(logFile, logContent, new UTF8Encoding(false));
            sw.Stop();

            if (!FileSystem.File.Exists(dllFile))
            {
                throw new Exception($"MSVC Compilation failed! Expected {dllFile} to be created.");
            }

            Logger<MsvcCompiler>.Info($"[JIT] MSVC Compilation successful in {sw.ElapsedMilliseconds} ms.");
        }
        catch (ProcessFailedException ex)
        {
            FileSystem.File.WriteAllText(logFile, ex.Output, new UTF8Encoding(false));
            throw new Exception($"MSVC Compilation Failed:\n{ex.Output}", ex);
        }
        catch (Exception ex)
        {
            var log = FileSystem.File.Exists(logFile) ? FileSystem.File.ReadAllText(logFile) : "No log found.";
            throw new Exception($"MSVC Compilation Failed:\n{log}", ex);
        }
        finally
        {
            var filesToClean = new List<string>
            {
                logFile,
                FileSystem.Path.ChangeExtension(dllFile, ".lib"),
                FileSystem.Path.ChangeExtension(dllFile, ".exp"),
                FileSystem.Path.ChangeExtension(dllFile, ".obj")
            };

            foreach (var file in filesToClean)
            {
                try { FileSystem.File.Delete(file); } catch { /* Ignore cleanup errors */ }
            }
        }
    }
}
