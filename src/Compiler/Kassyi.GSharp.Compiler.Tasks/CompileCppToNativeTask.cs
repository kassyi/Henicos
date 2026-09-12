using System.Diagnostics;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Kassyi.GSharp.Compiler.Tasks;

public class CompileCppToNativeTask : CompileKernelTaskBase
{
    [Required]
    public ITaskItem[] CppSources { get; set; } = [];

    public bool GSharpMultiTarget { get; set; }

    [Output]
    public ITaskItem[] OutputDlls { get; set; } = [];

    private string[] _architectures = [];
    private string? _vcvarsPath;

    protected override ITaskItem[] GetSources() => CppSources;
    protected override void SetOutputItems(ITaskItem[] outputItems) => OutputDlls = outputItems;

    protected override bool OnBeforeExecute()
    {
        _architectures = GetTargetArchitectures();
        _vcvarsPath = FindVcvarsall();

        if (string.IsNullOrEmpty(_vcvarsPath))
        {
            Log.LogError(
                "Could not locate vcvarsall.bat via vswhere.exe. Please ensure MSVC C++ Build Tools are installed.");
            return false;
        }
        return true;
    }

    protected override ITaskItem[]? ProcessSourceItem(ITaskItem sourceItem)
    {
        var sourcePath = sourceItem.GetMetadata("FullPath");
        var fileName = ArtifactHelper.GetCleanKernelFileName(sourcePath);
        var keyFilePath = sourcePath + ".key";
        var items = new List<ITaskItem>();

        foreach (var arch in _architectures)
        {
            // cl.exe architecture argument
            var vcvarsArch = arch == "x64" ? "x64" : (arch == "arm64" ? "x64_arm64" : "x64");

            var outputDllPath = Path.Combine(OutputDirectory, $"{fileName}_{arch}.dll");

            if (!CompileWithMsvc(sourcePath, outputDllPath, _vcvarsPath!, vcvarsArch))
            {
                return null;
            }

            if (!ArtifactHelper.ProcessArtifact(outputDllPath, keyFilePath, Log))
            {
                return null;
            }

            var taskItem = new TaskItem(outputDllPath);
            taskItem.SetMetadata("LogicalName", $"Kassyi.GSharp.Kernels.{Path.GetFileName(outputDllPath)}");
            items.Add(taskItem);
        }

        return [.. items];
    }

    private string[] GetTargetArchitectures()
    {
        if (GSharpMultiTarget)
        {
            return ["x64", "arm64"];
        }

        // Determine current host architecture
        var arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture;
        return arch == System.Runtime.InteropServices.Architecture.Arm64 ? ["arm64"] : ["x64"];
    }

    private string? FindVcvarsall()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var vswhere = Path.Combine(programFiles, "Microsoft Visual Studio", "Installer", "vswhere.exe");

        if (!File.Exists(vswhere))
        {
            return null;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = vswhere,
            Arguments =
                "-latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            return null;
        }

        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();

        if (string.IsNullOrEmpty(output))
        {
            return null;
        }

        var vcvars = Path.Combine(output, "VC", "Auxiliary", "Build", "vcvarsall.bat");
        return File.Exists(vcvars) ? vcvars : null;
    }



    private bool CompileWithMsvc(string sourcePath, string outputDllPath, string vcvarsPath, string vcvarsArch)
    {
        // /O2 = Optimize for speed
        // /LD = Create DLL
        // /Fe = Output file name
        // /nologo = Suppress copyright message

        var clArgs = $"/O2 /LD /nologo \"{sourcePath}\" /Fe:\"{outputDllPath}\"";
        var arguments = $"/c \"call \"{vcvarsPath}\" {vcvarsArch} >nul && cl.exe {clArgs}\"";

        Log.LogMessage(MessageImportance.Normal, $"Compiling C++ source for {vcvarsArch}: {sourcePath}");

        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            Log.LogError("Failed to start cmd.exe to invoke MSVC.");
            return false;
        }

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode == 0 && File.Exists(outputDllPath))
        {
            // MSVC produces .obj, .lib, .exp. Clean them up to avoid clutter.
            var baseDir = Path.GetDirectoryName(outputDllPath)!;
            var baseName = Path.GetFileNameWithoutExtension(outputDllPath);
            var objPath = Path.Combine(baseDir, baseName + ".obj");
            var libPath = Path.Combine(baseDir, baseName + ".lib");
            var expPath = Path.Combine(baseDir, baseName + ".exp");

            // Also the source .obj (if cl.exe drops it in the source folder, but typically it drops in cwd or output dir)
            var srcObjPath = Path.Combine(Path.GetDirectoryName(sourcePath)!,
                Path.GetFileNameWithoutExtension(sourcePath) + ".obj");
            var cwdObjPath = Path.Combine(Environment.CurrentDirectory,
                Path.GetFileNameWithoutExtension(sourcePath) + ".obj");

            SafeDeleteFiles(objPath, libPath, expPath, srcObjPath, cwdObjPath);

            return true;
        }

        Log.LogError($"MSVC compilation failed with exit code {process.ExitCode}.");
        if (!string.IsNullOrWhiteSpace(output))
        {
            Log.LogMessage(MessageImportance.High, output);
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            Log.LogError(error);
        }

        return false;
    }


}
