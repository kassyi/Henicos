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

internal class MockBuildEngine : IBuildEngine9
{
    private readonly object _sync = new();

    public bool ContinueOnError => false;
    public int LineNumberOfTaskNode => 0;
    public int ColumnNumberOfTaskNode => 0;
    public string ProjectFileOfTaskNode => "";
    public bool IsRunningMultipleNodes => true;

    public bool AllowFailureWithoutError { get; set; }
    public int AvailableCores { get; set; } = 4;
    public int RequestedCores { get; private set; }
    public int GrantedCores { get; private set; }
    public int ReleasedCores { get; private set; }
    public int HeldCores { get; private set; }
    public int RequestCoreCalls { get; private set; }
    public int ReleaseCoreCalls { get; private set; }

    public List<string> LoggedErrors { get; } = new();
    public List<string> LoggedErrorCodes { get; } = new();
    public List<string> LoggedWarnings { get; } = new();
    public List<string> LoggedWarningCodes { get; } = new();
    public List<string> LoggedMessages { get; } = new();

    public virtual bool BuildProjectFile(string projectFileName, string[] targetNames,
        IDictionary globalProperties, IDictionary targetOutputs) => true;

    public virtual bool BuildProjectFile(string projectFileName, string[] targetNames,
        IDictionary globalProperties, IDictionary targetOutputs, string toolsVersion) => true;

    public virtual BuildEngineResult BuildProjectFilesInParallel(string[] projectFileNames, string[] targetNames,
        IDictionary[] globalProperties, IList<string>[] removeGlobalProperties, string[] toolsVersion,
        bool returnTargetOutputs) => new(true, []);

    public virtual bool BuildProjectFilesInParallel(string[] projectFileNames, string[] targetNames,
        IDictionary[] globalProperties, IDictionary[] targetOutputsPerProject, string[] toolsVersion,
        bool useResultsCache, bool unloadProjectsOnCompletion) => true;

    public virtual void Yield()
    {
    }

    public virtual void Reacquire()
    {
    }

    public virtual void RegisterTaskObject(object key, object obj, RegisteredTaskObjectLifetime lifetime,
        bool allowEarlyCollection)
    {
    }

    // MSBuild IBuildEngine4+ contract allows null when no object is registered; test harness never registers objects.
    public virtual object GetRegisteredTaskObject(object key, RegisteredTaskObjectLifetime lifetime) => null!;

    // MSBuild IBuildEngine4+ contract allows null when no object is registered; test harness never registers objects.
    public virtual object UnregisterTaskObject(object key, RegisteredTaskObjectLifetime lifetime) => null!;

    public virtual void LogTelemetry(string eventName, IDictionary<string, string> properties)
    {
    }

    public virtual IReadOnlyDictionary<string, string> GetGlobalProperties() =>
        new Dictionary<string, string>();

    public virtual bool ShouldTreatWarningAsError(string warningCode) => false;

    public virtual int RequestCores(int requestedCores)
    {
        lock (_sync)
        {
            RequestCoreCalls++;
            RequestedCores += requestedCores;
            var grant = Math.Max(0, Math.Min(requestedCores, AvailableCores - HeldCores));
            GrantedCores += grant;
            HeldCores += grant;
            return grant;
        }
    }

    public virtual void ReleaseCores(int coresToRelease)
    {
        lock (_sync)
        {
            ReleaseCoreCalls++;
            ReleasedCores += coresToRelease;
            HeldCores = Math.Max(0, HeldCores - coresToRelease);
        }
    }

    public virtual void LogCustomEvent(CustomBuildEventArgs e)
    {
    }

    public virtual void LogErrorEvent(BuildErrorEventArgs e)
    {
        lock (_sync)
        {
            if (e.Message != null) LoggedErrors.Add(e.Message);
            if (e.Code != null) LoggedErrorCodes.Add(e.Code);
        }
    }

    public virtual void LogMessageEvent(BuildMessageEventArgs e)
    {
        lock (_sync)
        {
            if (e.Message != null) LoggedMessages.Add(e.Message);
        }
    }

    public virtual void LogWarningEvent(BuildWarningEventArgs e)
    {
        lock (_sync)
        {
            if (e.Message != null) LoggedWarnings.Add(e.Message);
            if (e.Code != null) LoggedWarningCodes.Add(e.Code);
        }
    }
}

internal sealed class LegacyMockBuildEngine : IBuildEngine
{
    private readonly object _sync = new();

    public bool ContinueOnError => false;
    public int LineNumberOfTaskNode => 0;
    public int ColumnNumberOfTaskNode => 0;
    public string ProjectFileOfTaskNode => "";
    public bool IsRunningMultipleNodes => true;

    public List<string> LoggedErrors { get; } = new();
    public List<string> LoggedWarnings { get; } = new();
    public List<string> LoggedMessages { get; } = new();

    public bool BuildProjectFile(string projectFileName, string[] targetNames,
        IDictionary globalProperties, IDictionary targetOutputs) => true;

    public void LogCustomEvent(CustomBuildEventArgs e)
    {
    }

    public void LogErrorEvent(BuildErrorEventArgs e)
    {
        lock (_sync)
        {
            if (e.Message != null) LoggedErrors.Add(e.Message);
        }
    }

    public void LogMessageEvent(BuildMessageEventArgs e)
    {
        lock (_sync)
        {
            if (e.Message != null) LoggedMessages.Add(e.Message);
        }
    }

    public void LogWarningEvent(BuildWarningEventArgs e)
    {
        lock (_sync)
        {
            if (e.Message != null) LoggedWarnings.Add(e.Message);
        }
    }
}

public enum CompileTaskKind
{
    Cuda,
    CudaCombined,
    Cpp,
    KernelTaskBase
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
        var dir = Path.GetDirectoryName(p);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

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

    public GccClangToolchain CreateGccClangToolchain(string? compilerExecutable = "g++",
        string? hostArchitecture = null) =>
        new(Runner, compilerExecutable, hostArchitecture);

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
        INativeToolchain? toolchain = null,
        string? objectDirectory = null)
    {
        return new CompileCppToNativeTask
        {
            BuildEngine = BuildEngine,
            CppSources = CreateTaskItems(sourceFiles),
            OutputDirectory = TempDir,
            ObjectDirectory = objectDirectory ?? string.Empty,
            ProcessRunner = Runner,
            Toolchain = toolchain ?? CreateMsvcToolchain(disableCache: true),
            MockCompilation = false,
            DisableVcvarsCache = true,
            GSharpMultiTarget = false
        };
    }

    public CompileKernelTaskBase CreateTask(CompileTaskKind kind, string[] sourceFiles)
    {
        switch (kind)
        {
            case CompileTaskKind.Cuda:
                return CreateConfiguredCudaTask(sourceFiles, combinedName: "");
            case CompileTaskKind.CudaCombined:
                return CreateConfiguredCudaTask(sourceFiles, combinedName: "combined_fatbin");
            case CompileTaskKind.Cpp:
                return CreateCppTask(sourceFiles);
            default:
                return new TestCompileKernelTask();
        }
    }

    private CompileCudaToFatbinTask CreateConfiguredCudaTask(string[] sourceFiles, string combinedName)
    {
        var task = CreateCudaTask(sourceFiles, combinedName);
        task.TargetArchitectures = "sm_86";
        return task;
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
    private readonly object _sync = new();
    private readonly string _mockVsPath;
    private int _activeCount;
    private int _peakConcurrency;

    public MockProcessRunner(string tempDir)
    {
        _mockVsPath = Path.Combine(tempDir, "VS");
        var vcvarsDir = Path.Combine(_mockVsPath, "VC", "Auxiliary", "Build");
        Directory.CreateDirectory(vcvarsDir);
        File.WriteAllText(Path.Combine(vcvarsDir, "vcvarsall.bat"), "@echo off");
    }

    public List<ProcessStartInfo> Invocations { get; } = new();
    public List<CancellationToken> ObservedTokens { get; } = new();
    public int PeakConcurrency => Volatile.Read(ref _peakConcurrency);
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
    public string VersionCheckOutput { get; set; } = "compiler version check ok";

    public ProcessResult Run(ProcessStartInfo startInfo, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            Invocations.Add(startInfo);
            ObservedTokens.Add(cancellationToken);
        }

        var current = Interlocked.Increment(ref _activeCount);
        UpdatePeakConcurrency(current);
        try
        {
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
                    VersionCheckExitCode == 0 ? VersionCheckOutput : "",
                    VersionCheckExitCode != 0 ? "compiler not found" : "");
            }

            if (NextException != null)
            {
                throw NextException;
            }

            if (NextExitCode == 0 && SimulateCompilationOutput && IsCompilerExecutable(startInfo.FileName))
            {
                WriteSimulatedOutputs(startInfo.Arguments);
            }

            return new ProcessResult(NextExitCode, NextStandardOutput, NextStandardError);
        }
        finally
        {
            Interlocked.Decrement(ref _activeCount);
        }
    }

    public Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Run(startInfo, cancellationToken));
    }

    private void UpdatePeakConcurrency(int observed)
    {
        while (true)
        {
            var prev = Volatile.Read(ref _peakConcurrency);
            if (observed <= prev)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _peakConcurrency, observed, prev) == prev)
            {
                return;
            }
        }
    }

    private static bool IsCompilerExecutable(string fileName) =>
        fileName.Contains("nvcc") ||
        fileName.Contains("cl.exe") ||
        fileName.Contains("g++") ||
        fileName.Contains("clang++") ||
        fileName.Contains("c++");

    private static void WriteSimulatedOutputs(string args)
    {
        if (TryExtractFlagPath(args, "-o ", out var outFile) ||
            TryExtractFlagPath(args, "/Fe:", out outFile) ||
            TryExtractFlagPath(args, "/Fo:", out outFile))
        {
            WriteMockArtifact(outFile);
        }

        if (TryExtractFlagPath(args, "/Fd:", out var pdbFile) &&
            pdbFile.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
        {
            WriteMockArtifact(pdbFile);
        }
    }

    private static void WriteMockArtifact(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        if (path.EndsWith(".gfatbin", StringComparison.OrdinalIgnoreCase))
        {
            File.WriteAllText(path, "Mock Fatbin Content from MockProcessRunner");
        }
        else if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                 path.EndsWith(".so", StringComparison.OrdinalIgnoreCase))
        {
            File.WriteAllText(path, "Mock DLL Content from MockProcessRunner");
        }
        else if (path.EndsWith(".obj", StringComparison.OrdinalIgnoreCase) ||
                 path.EndsWith(".o", StringComparison.OrdinalIgnoreCase))
        {
            File.WriteAllText(path, "Mock Object Content from MockProcessRunner");
        }
        else if (path.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
        {
            File.WriteAllText(path, "Mock PDB Content from MockProcessRunner");
        }
    }

    private static bool TryExtractFlagPath(string args, string prefix, out string path)
    {
        var idx = args.IndexOf(prefix, StringComparison.Ordinal);
        if (idx < 0)
        {
            path = string.Empty;
            return false;
        }

        var span = args.AsSpan(idx + prefix.Length).TrimStart();
        if (span.Length == 0)
        {
            path = string.Empty;
            return false;
        }

        if (span[0] == '"')
        {
            var rest = span.Slice(1);
            var endQuote = rest.IndexOf('"');
            path = (endQuote >= 0 ? rest.Slice(0, endQuote) : rest).ToString();
            return path.Length > 0;
        }

        var endSpace = span.IndexOf(' ');
        path = (endSpace >= 0 ? span.Slice(0, endSpace) : span).ToString();
        return path.Length > 0;
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
