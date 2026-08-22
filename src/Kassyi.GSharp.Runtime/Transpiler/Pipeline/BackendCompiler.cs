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
        if (!FileSystem.File.Exists(vcvars))
        {
            throw new Exception($"vcvars64.bat not found at {vcvars}");
        }

        return vcvars;
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

        var fallback = @"C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v13.3\bin\nvcc.exe";
        if (FileSystem.File.Exists(fallback))
        {
            return fallback;
        }

        return "nvcc.exe";
    }

    public static void Compile(string cuFile, string dllFile, string? includePath, string tempDir)
    {
        var vcvars = GetVcvarsPath();
        var nvcc = GetNvccPath();

        // Generate an options file to prevent OS command injection
        var optsFile = FileSystem.Path.Combine(tempDir, Guid.NewGuid().ToString("N") + ".txt");
        var opts = new List<string>
        {
            "-shared",
            "-o", dllFile,
            cuFile,
            "-lcufft",
            "-O3",
            "-arch=sm_86",
            "-std=c++17",
            "-rdc=true"
        };
        if (!string.IsNullOrEmpty(includePath))
        {
            opts.Add($"-I{includePath}");
        }

        FileSystem.File.WriteAllLines(optsFile, opts, new UTF8Encoding(false));

        var batFile = FileSystem.Path.Combine(tempDir, Guid.NewGuid().ToString("N") + ".bat");
        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine($"call \"{vcvars}\"");
        sb.AppendLine($"\"{nvcc}\" --options-file \"{optsFile}\" > \"{tempDir}\\out.log\" 2>&1");
        FileSystem.File.WriteAllText(batFile, sb.ToString(), new UTF8Encoding(false));

        var psi = new ProcessStartInfo("cmd.exe", $"/c \"{batFile}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = tempDir
        };

        Console.WriteLine("[JIT] Compiling dynamically generated CUDA code...");
        var sw = Stopwatch.StartNew();

        try
        {
            ProcessRunner(psi); // Injectable process execution
        }
        catch (Exception)
        {
            var log = FileSystem.File.Exists($"{tempDir}\\out.log")
                ? FileSystem.File.ReadAllText($"{tempDir}\\out.log")
                : "No log found.";
            throw new Exception($"NVCC Compilation Failed:\n{log}");
        }
        finally
        {
            // Cleanup intermediate files
            try
            {
                FileSystem.File.Delete(optsFile);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[JIT Warning] Failed to delete opts file: {ex.Message}");
            }

            try
            {
                FileSystem.File.Delete(batFile);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[JIT Warning] Failed to delete bat file: {ex.Message}");
            }

            try
            {
                FileSystem.File.Delete(FileSystem.Path.ChangeExtension(dllFile, ".lib"));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[JIT Warning] Failed to delete lib file: {ex.Message}");
            }

            try
            {
                FileSystem.File.Delete(FileSystem.Path.ChangeExtension(dllFile, ".exp"));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[JIT Warning] Failed to delete exp file: {ex.Message}");
            }
        }

        sw.Stop();

        if (!FileSystem.File.Exists(dllFile))
        {
            throw new Exception($"JIT Compilation failed! Expected {dllFile} to be created.");
        }

        Console.WriteLine($"[JIT] Compilation successful in {sw.ElapsedMilliseconds} ms.");
    }

    public static void CompileToFatbin(string cuFile, string fatbinFile, string? includePath, string tempDir)
    {
        var vcvars = GetVcvarsPath();
        var nvcc = GetNvccPath();

        // Generate an options file to prevent OS command injection
        var optsFile = FileSystem.Path.Combine(tempDir, Guid.NewGuid().ToString("N") + ".txt");
        var opts = new List<string>
        {
            "--fatbin",
            "-o", fatbinFile,
            cuFile,
            "-O3",
            "-arch=sm_86",
            "-std=c++17"
        };
        if (!string.IsNullOrEmpty(includePath))
        {
            opts.Add($"-I{includePath}");
        }

        FileSystem.File.WriteAllLines(optsFile, opts, new UTF8Encoding(false));

        var batFile = FileSystem.Path.Combine(tempDir, Guid.NewGuid().ToString("N") + ".bat");
        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine($"call \"{vcvars}\"");
        sb.AppendLine($"\"{nvcc}\" --options-file \"{optsFile}\" > \"{tempDir}\\out_fatbin.log\" 2>&1");
        FileSystem.File.WriteAllText(batFile, sb.ToString(), new UTF8Encoding(false));

        var psi = new ProcessStartInfo("cmd.exe", $"/c \"{batFile}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = tempDir
        };

        Console.WriteLine("[JIT] Compiling dynamically generated CUDA code to FATBIN...");
        var sw = Stopwatch.StartNew();

        try
        {
            ProcessRunner(psi); // Injectable process execution
        }
        catch (Exception)
        {
            var log = FileSystem.File.Exists($"{tempDir}\\out_fatbin.log")
                ? FileSystem.File.ReadAllText($"{tempDir}\\out_fatbin.log")
                : "No log found.";
            throw new Exception($"NVCC FATBIN Compilation Failed:\n{log}");
        }
        finally
        {
            // Cleanup intermediate files
            try
            {
                FileSystem.File.Delete(optsFile);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[JIT Warning] Failed to delete opts file: {ex.Message}");
            }

            try
            {
                FileSystem.File.Delete(batFile);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[JIT Warning] Failed to delete bat file: {ex.Message}");
            }
        }

        sw.Stop();

        if (!FileSystem.File.Exists(fatbinFile))
        {
            throw new Exception($"JIT FATBIN Compilation failed! Expected {fatbinFile} to be created.");
        }

        Console.WriteLine($"[JIT] FATBIN Compilation successful in {sw.ElapsedMilliseconds} ms.");
    }
}

