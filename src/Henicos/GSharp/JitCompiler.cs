using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Kassyi.GSharp
{
    public class JitCompiler
    {
        private static string GetVcvarsPath()
        {
            string programFiles = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
            string vswhere = Path.Combine(programFiles, "Microsoft Visual Studio", "Installer", "vswhere.exe");
            
            if (!File.Exists(vswhere))
                throw new Exception("vswhere.exe not found.");

            ProcessStartInfo psi = new ProcessStartInfo(vswhere, "-latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            string output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            if (string.IsNullOrEmpty(output))
                throw new Exception("Visual Studio installation not found.");

            string vcvars = Path.Combine(output, "VC", "Auxiliary", "Build", "vcvars64.bat");
            if (!File.Exists(vcvars))
                throw new Exception($"vcvars64.bat not found at {vcvars}");

            return vcvars;
        }

        private static string GetNvccPath()
        {
            string cudaPath = Environment.GetEnvironmentVariable("CUDA_PATH");
            if (!string.IsNullOrEmpty(cudaPath))
            {
                string nvcc = Path.Combine(cudaPath, "bin", "nvcc.exe");
                if (File.Exists(nvcc)) return nvcc;
            }

            // Fallback to known path
            string fallback = @"C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v13.3\bin\nvcc.exe";
            if (File.Exists(fallback)) return fallback;

            return "nvcc.exe"; // Hope it's in PATH
        }

        public static TDelegate CompileAndLoad<TDelegate>(string cuCode, string functionName, string includePath = null) where TDelegate : Delegate
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "KassyiGSharpJit");
            Directory.CreateDirectory(tempDir);

            string id = Guid.NewGuid().ToString("N");
            string cuFile = Path.Combine(tempDir, $"{id}.cu");
            string dllFile = Path.Combine(tempDir, $"{id}.dll");
            string batFile = Path.Combine(tempDir, $"{id}.bat");

            File.WriteAllText(cuFile, cuCode, new UTF8Encoding(false)); 

            string vcvars = GetVcvarsPath();
            string nvcc = GetNvccPath();

            string includeArg = string.IsNullOrEmpty(includePath) ? "" : $"-I\"{includePath}\"";

            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine($"call \"{vcvars}\"");
            sb.AppendLine($"\"{nvcc}\" -shared -o \"{dllFile}\" \"{cuFile}\" {includeArg} -lcufft -O3 -arch=sm_86 -std=c++17 -rdc=true -Xcompiler \"/wd 4819\"");
            
            File.WriteAllText(batFile, sb.ToString(), new UTF8Encoding(false));

            // Compile
            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", $"/c \"{batFile}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = tempDir
            };

            Console.WriteLine("[JIT] Compiling dynamically generated CUDA code...");
            Stopwatch sw = Stopwatch.StartNew();
            using var process = Process.Start(psi);
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            sw.Stop();

            if (process.ExitCode != 0 || !File.Exists(dllFile))
            {
                throw new Exception($"JIT Compilation failed! ExitCode: {process.ExitCode}\nOutput: {output}\nError: {error}");
            }
            Console.WriteLine($"[JIT] Compilation successful in {sw.ElapsedMilliseconds} ms.");

            // Load DLL
            IntPtr hModule = NativeLibrary.Load(dllFile);
            if (hModule == IntPtr.Zero)
            {
                throw new Exception("Failed to load compiled JIT DLL.");
            }

            // Get Function Pointer
            IntPtr pFunc = NativeLibrary.GetExport(hModule, functionName);
            if (pFunc == IntPtr.Zero)
            {
                throw new Exception($"Failed to find exported function '{functionName}'. Make sure it has extern \"C\" __declspec(dllexport).");
            }

            // Convert to Delegate
            return Marshal.GetDelegateForFunctionPointer<TDelegate>(pFunc);
        }
    }
}
