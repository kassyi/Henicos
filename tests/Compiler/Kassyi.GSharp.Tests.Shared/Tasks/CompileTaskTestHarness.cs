#nullable enable
using System.Collections;
using System.Diagnostics;
using Kassyi.GSharp.Compiler.Tasks;
using Kassyi.GSharp.Compiler.Tasks.Helpers;
using Kassyi.GSharp.Compiler.Tasks.Toolchains;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Task = System.Threading.Tasks.Task;

namespace Kassyi.GSharp.Tests.Shared.Tasks;

internal class MockBuildEngine : IBuildEngine
{
    public bool ContinueOnError => false;
    public int LineNumberOfTaskNode => 0;
    public int ColumnNumberOfTaskNode => 0;
    public string ProjectFileOfTaskNode => "";

    public List<string> LoggedErrors { get; } = new();
    public List<string> LoggedWarnings { get; } = new();
    public List<string> LoggedMessages { get; } = new();

    public virtual bool BuildProjectFile(string projectFileName, string[] targetNames,
        IDictionary globalProperties, IDictionary targetOutputs) => true;

    public virtual void LogCustomEvent(CustomBuildEventArgs e)
    {
    }

    public virtual void LogErrorEvent(BuildErrorEventArgs e)
    {
        if (e.Message != null) LoggedErrors.Add(e.Message);
        Console.WriteLine($"ERROR: {e.Message}");
    }

    public virtual void LogMessageEvent(BuildMessageEventArgs e)
    {
        if (e.Message != null) LoggedMessages.Add(e.Message);
    }

    public virtual void LogWarningEvent(BuildWarningEventArgs e)
    {
        if (e.Message != null) LoggedWarnings.Add(e.Message);
        Console.WriteLine($"WARN: {e.Message}");
    }
}

internal sealed class CompileTaskTestContext : IDisposable
{
    public string TempDir { get; }
    public string FakeVswherePath { get; }
    public MockBuildEngine BuildEngine { get; }
    public MockProcessRunner Runner { get; }
    private readonly Dictionary<string, string?> _envOverrides = new();

    public CompileTaskTestContext()
    {
        TempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(TempDir);
        FakeVswherePath = Path.Combine(TempDir, "vswhere.exe");
        File.WriteAllText(FakeVswherePath, "");
        BuildEngine = new MockBuildEngine();
        Runner = new MockProcessRunner(TempDir) { NextExitCode = 0, NextStandardOutput = "Success" };
        SetEnv("GSHARP_TEST_NO_CUDA", null);
    }

    public void SetEnv(string key, string? value)
    {
        if (!_envOverrides.ContainsKey(key))
        {
            _envOverrides[key] = Environment.GetEnvironmentVariable(key);
        }

        Environment.SetEnvironmentVariable(key, value);
    }

    public string CreateDummySource(string fileName, string content = "dummy")
    {
        var p = Path.Combine(TempDir, fileName);
        File.WriteAllText(p, content);
        return p;
    }

    private TaskItem[] CreateTaskItems(string[] sourceFiles)
    {
        var items = new TaskItem[sourceFiles.Length];
        for (var i = 0; i < sourceFiles.Length; i++)
        {
            items[i] = new TaskItem(Path.Combine(TempDir, sourceFiles[i]));
        }

        return items;
    }

    public MsvcToolchain CreateMsvcToolchain(bool disableCache = true) =>
        new(Runner, disableCache, FakeVswherePath);

    public GccClangToolchain CreateGccClangToolchain(string? compilerExecutable = "g++") =>
        new(Runner, compilerExecutable);

    public CompileCudaToFatbinTask CreateCudaTask(
        string[] sourceFiles,
        string? combinedName = "combined_fatbin",
        INativeToolchain? toolchain = null)
    {
        return new CompileCudaToFatbinTask
        {
            BuildEngine = BuildEngine,
            CudaSources = CreateTaskItems(sourceFiles),
            OutputDirectory = TempDir,
            ProcessRunner = Runner,
            Toolchain = toolchain ?? CreateMsvcToolchain(disableCache: true),
            CombinedFatbinName = combinedName,
            MockCompilation = false,
            DisableVcvarsCache = true
        };
    }

    public CompileCppToNativeTask CreateCppTask(
        string[] sourceFiles,
        INativeToolchain? toolchain = null)
    {
        return new CompileCppToNativeTask
        {
            BuildEngine = BuildEngine,
            CppSources = CreateTaskItems(sourceFiles),
            OutputDirectory = TempDir,
            ProcessRunner = Runner,
            Toolchain = toolchain ?? CreateMsvcToolchain(disableCache: true),
            MockCompilation = false,
            DisableVcvarsCache = true,
            GSharpMultiTarget = false
        };
    }

    public CompileKernelTaskBase CreateTask(
        bool isCpp,
        string[] sourceFiles,
        string? combinedFatbinName = null,
        INativeToolchain? toolchain = null)
    {
        if (isCpp) return CreateCppTask(sourceFiles, toolchain);
        return CreateCudaTask(sourceFiles, combinedFatbinName, toolchain);
    }

    public void Dispose()
    {
        foreach (var kvp in _envOverrides)
        {
            Environment.SetEnvironmentVariable(kvp.Key, kvp.Value);
        }

        if (Directory.Exists(TempDir)) Directory.Delete(TempDir, true);
    }
}

internal sealed class MockProcessRunner : IProcessRunner
{
    private readonly string _mockVsPath;

    public MockProcessRunner(string tempDir)
    {
        _mockVsPath = Path.Combine(tempDir, "VS");
        var vcvarsDir = Path.Combine(_mockVsPath, "VC", "Auxiliary", "Build");
        Directory.CreateDirectory(vcvarsDir);
        File.WriteAllText(Path.Combine(vcvarsDir, "vcvarsall.bat"), "@echo off");
    }

    public List<ProcessStartInfo> Invocations { get; } = new();
    public List<CancellationToken> ObservedTokens { get; } = new();
    public Action<ProcessStartInfo, CancellationToken>? OnRun { get; set; }
    public int NextExitCode { get; set; } = 0;
    public string NextStandardOutput { get; set; } = "";
    public string NextStandardError { get; set; } = "";
    public Exception? NextException { get; set; }
    public bool SimulateCompilationOutput { get; set; } = true;

    public bool OverrideVswhere { get; set; } = false;
    public string? VswhereOutput { get; set; }
    public int CmdExitCode { get; set; } = 0;
    public string CmdStandardOutput { get; set; } = "INCLUDE=C:\\dummy\r\nLIB=C:\\dummy";
    public int VersionCheckExitCode { get; set; } = 0;

    public ProcessResult Run(ProcessStartInfo startInfo, CancellationToken cancellationToken = default)
    {
        Invocations.Add(startInfo);
        ObservedTokens.Add(cancellationToken);
        OnRun?.Invoke(startInfo, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (startInfo.FileName.EndsWith("vswhere.exe", StringComparison.OrdinalIgnoreCase))
        {
            if (OverrideVswhere) return new ProcessResult(1, "", "Not found");
            return new ProcessResult(0, VswhereOutput ?? _mockVsPath, "");
        }

        if (startInfo.FileName.EndsWith("cmd.exe", StringComparison.OrdinalIgnoreCase))
        {
            return new ProcessResult(CmdExitCode, CmdExitCode == 0 ? CmdStandardOutput : "",
                CmdExitCode != 0 ? "error" : "");
        }

        if (startInfo.Arguments.Contains("--version"))
        {
            return new ProcessResult(
                VersionCheckExitCode,
                VersionCheckExitCode == 0 ? "compiler version check ok" : "",
                VersionCheckExitCode != 0 ? "compiler not found" : "");
        }

        if (NextException != null)
        {
            throw NextException;
        }

        if (SimulateCompilationOutput &&
            (startInfo.FileName.Contains("nvcc") ||
             startInfo.FileName.Contains("cl.exe") ||
             startInfo.FileName.Contains("g++") ||
             startInfo.FileName.Contains("clang++") ||
             startInfo.FileName.Contains("c++")))
        {
            var argsStr = startInfo.Arguments;
            var outFile = "";
            if (argsStr.Contains("-o "))
            {
                outFile = argsStr.Split("-o ")[1].Split(" ")[0].Trim('"');
            }
            else if (argsStr.Contains("/Fe:"))
            {
                outFile = argsStr.Split("/Fe:")[1].Split(" ")[0].Trim('"');
            }

            if (!string.IsNullOrEmpty(outFile) && outFile.EndsWith(".gfatbin"))
            {
                File.WriteAllText(outFile, "Mock Fatbin Content from MockProcessRunner");
            }
            else if (!string.IsNullOrEmpty(outFile) && outFile.EndsWith(".dll"))
            {
                File.WriteAllText(outFile, "Mock DLL Content from MockProcessRunner");
            }
        }

        return new ProcessResult(NextExitCode, NextStandardOutput, NextStandardError);
    }

    public Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Run(startInfo, cancellationToken));
    }
}

internal sealed class TestCompileKernelTask : CompileKernelTaskBase
{
    public Func<ITaskItem[]> SourcesFunc { get; set; } = () => [];
    public Func<ITaskItem, ITaskItem[]?> ProcessFunc { get; set; } = _ => null;

    public Action<ITaskItem[]> SetOutputFunc { get; set; } = _ =>
    {
    };

    protected override ITaskItem[] GetSources() => SourcesFunc();
    protected override ITaskItem[]? ProcessSourceItem(ITaskItem sourceItem) => ProcessFunc(sourceItem);
    protected override void SetOutputItems(ITaskItem[] outputItems) => SetOutputFunc(outputItems);

    public new Dictionary<string, string>? GetVcvarsEnvironment(string arch) => base.GetVcvarsEnvironment(arch);
    public new void SafeDeleteFiles(params string[] paths) => base.SafeDeleteFiles(paths);
}
