using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Kassyi.GSharp;

public class JitCompiledKernel<TDelegate> : IDisposable where TDelegate : Delegate
{
    private IntPtr _hModule;
    private readonly Action<IntPtr> _freeFunc;
    private readonly Action _deleteFileFunc;

    public TDelegate Kernel
    {
        get;
    }

    public IntPtr NativeHandle => _hModule;

    public JitCompiledKernel(IntPtr hModule, TDelegate kernel, Action<IntPtr> freeFunc, Action deleteFileFunc)
    {
        _hModule = hModule;
        Kernel = kernel;
        _freeFunc = freeFunc;
        _deleteFileFunc = deleteFileFunc;
    }

    public void Dispose()
    {
        if (_hModule != IntPtr.Zero)
        {
            _freeFunc?.Invoke(_hModule);
            _hModule = IntPtr.Zero;
        }

        _deleteFileFunc?.Invoke();
    }
}

public static class NvccCompiler
{
    public static System.IO.Abstractions.IFileSystem FileSystem { get; set; } = new System.IO.Abstractions.FileSystem();
    public static Action<ProcessStartInfo> ProcessRunner
    {
        get; set;
    } = psi =>
    {
        using var process = Process.Start(psi);
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new Exception("Process failed");
        } 
    };

    public static Func<string> GetVswhereOutputFunc
    {
        get; set;
    } = () =>
    {
        var programFiles = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
        var vswhere = FileSystem.Path.Combine(programFiles, "Microsoft Visual Studio", "Installer", "vswhere.exe");

        if (!FileSystem.File.Exists(vswhere))
        {
            throw new Exception("vswhere.exe not found.");
        }

        var psi = new ProcessStartInfo(vswhere, "-latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
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

    public static void Compile(string cuFile, string dllFile, string includePath, string tempDir)
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
            var log = FileSystem.File.Exists($"{tempDir}\\out.log") ? FileSystem.File.ReadAllText($"{tempDir}\\out.log") : "No log found.";
            throw new Exception($"NVCC Compilation Failed:\n{log}");
        }
        finally
        {
            // Cleanup intermediate files
            try { FileSystem.File.Delete(optsFile); } catch (Exception ex) { Console.WriteLine($"[JIT Warning] Failed to delete opts file: {ex.Message}"); }
            try { FileSystem.File.Delete(batFile); } catch (Exception ex) { Console.WriteLine($"[JIT Warning] Failed to delete bat file: {ex.Message}"); }
            try { FileSystem.File.Delete(FileSystem.Path.ChangeExtension(dllFile, ".lib")); } catch (Exception ex) { Console.WriteLine($"[JIT Warning] Failed to delete lib file: {ex.Message}"); }
            try { FileSystem.File.Delete(FileSystem.Path.ChangeExtension(dllFile, ".exp")); } catch (Exception ex) { Console.WriteLine($"[JIT Warning] Failed to delete exp file: {ex.Message}"); }
        }
        sw.Stop();

        if (!FileSystem.File.Exists(dllFile))
        {
            throw new Exception($"JIT Compilation failed! Expected {dllFile} to be created.");
        }
        Console.WriteLine($"[JIT] Compilation successful in {sw.ElapsedMilliseconds} ms.");
    }
}

public static class NativeModuleLoader
{
    public static System.IO.Abstractions.IFileSystem FileSystem { get; set; } = new System.IO.Abstractions.FileSystem();
    public static Func<string, IntPtr> LoadLibraryFunc { get; set; } = NativeLibrary.Load;
    public static Func<IntPtr, string, IntPtr> GetExportFunc { get; set; } = NativeLibrary.GetExport;
    public static Action<IntPtr> FreeLibraryFunc { get; set; } = NativeLibrary.Free;

    public static JitCompiledKernel<TDelegate> Load<TDelegate>(string dllFile, string functionName) where TDelegate : Delegate
    {
        var hModule = LoadLibraryFunc(dllFile);
        if (hModule == IntPtr.Zero)
        {
            throw new Exception("Failed to load compiled JIT DLL.");
        }

        var pFunc = GetExportFunc(hModule, functionName);
        if (pFunc == IntPtr.Zero)
        {
            FreeLibraryFunc(hModule);
            throw new Exception($"Failed to find exported function '{functionName}'. Make sure it has extern \"C\" __declspec(dllexport).");
        }

        var del = Marshal.GetDelegateForFunctionPointer<TDelegate>(pFunc);

        var deleteAction = () =>
        {
            if (FileSystem.File.Exists(dllFile))
            {
                try { FileSystem.File.Delete(dllFile); } catch (Exception ex) { Console.WriteLine($"[JIT Warning] Failed to delete dll file: {ex.Message}"); }
            }
        };

        return new JitCompiledKernel<TDelegate>(hModule, del, FreeLibraryFunc, deleteAction);
    }
}

public class JitCompiler
{
    public static System.IO.Abstractions.IFileSystem FileSystem { get; set; } = new System.IO.Abstractions.FileSystem();

    public static JitCompiledKernel<TDelegate> CompileAndLoad<TDelegate>(string cuCode, string functionName, string includePath = null) where TDelegate : Delegate
    {
        var tempDir = FileSystem.Path.Combine(FileSystem.Path.GetTempPath(), "KassyiGSharpJit");
        FileSystem.Directory.CreateDirectory(tempDir);

        var id = Guid.NewGuid().ToString("N");
        var cuFile = FileSystem.Path.Combine(tempDir, $"{id}.cu");
        var dllFile = FileSystem.Path.Combine(tempDir, $"{id}.dll");

        try
        {
            FileSystem.File.WriteAllText(cuFile, cuCode, new UTF8Encoding(false));
            NvccCompiler.Compile(cuFile, dllFile, includePath, tempDir);
            return NativeModuleLoader.Load<TDelegate>(dllFile, functionName);
        }
        catch
        {
            if (FileSystem.File.Exists(dllFile))
            {
                try { FileSystem.File.Delete(dllFile); } catch (Exception ex) { Console.WriteLine($"[JIT Warning] Failed to delete dll file: {ex.Message}"); }
            }
            throw;
        }
        finally
        {
            // Clean up .cu file always
            if (FileSystem.File.Exists(cuFile))
            {
                try { FileSystem.File.Delete(cuFile); } catch (Exception ex) { Console.WriteLine($"[JIT Warning] Failed to delete cu file: {ex.Message}"); }
            }
        }
    }
}
