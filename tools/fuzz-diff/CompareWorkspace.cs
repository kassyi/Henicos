using System.Diagnostics;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>Thrown when a build or a git command that the comparison depends on fails.</summary>
internal sealed class CompareInfrastructureException(string message) : Exception(message);

/// <summary>Runs <c>git</c> and <c>dotnet</c> for the comparison and builds the G# compiler of a repository tree.</summary>
internal static class CompareTools
{
    private const int BuildTimeoutMs = 600_000;

    private static readonly string[] s_prerequisiteProjects =
    [
        "src/Core/Kassyi.GSharp.Core/Kassyi.GSharp.Core.csproj",
        "src/Runtime/Kassyi.GSharp.Runtime/Kassyi.GSharp.Runtime.csproj",
        "src/Compiler/Kassyi.GSharp.Compiler/Kassyi.GSharp.Compiler.csproj",
        "src/Compiler/Kassyi.GSharp.Compiler.Tasks/Kassyi.GSharp.Compiler.Tasks.csproj",
    ];

    /// <summary>
    /// Builds Core, Runtime, the compiler and its MSBuild tasks of <paramref name="treeRoot"/> every time: the build is
    /// incremental, and a DLL left by an earlier build may be older than the sources (for example an attribute the
    /// compiler reads, added to Core on this branch).
    /// </summary>
    internal static void BuildCompilerPrerequisites(string treeRoot)
    {
        foreach (string project in s_prerequisiteProjects)
        {
            string projectPath = Path.Combine(treeRoot, project);
            var args = new List<string> { "build", projectPath, "-c", "Debug", "-m:1", "-nodeReuse:false" };
            int exitCode = Run("dotnet", args, treeRoot, BuildTimeoutMs, out string output);
            if (exitCode != 0)
            {
                throw new CompareInfrastructureException($"Failed to build {projectPath}:{Environment.NewLine}{output}");
            }
        }
    }

    /// <summary>Runs <paramref name="fileName"/> in <paramref name="workingDirectory"/> and returns its exit code; 124 on a timeout.</summary>
    internal static int Run(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        int timeoutMs,
        out string output)
    {
        var psi = new ProcessStartInfo(fileName) { WorkingDirectory = workingDirectory };
        foreach (string argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        int exitCode = FuzzWorkspaceReducer.RunProcessWithStartInfo(psi, out string stdout, out string stderr, out bool timedOut, timeoutMs);
        output = timedOut
            ? $"{fileName} timed out after {timeoutMs / 1000} s.{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}"
            : $"{stdout}{Environment.NewLine}{stderr}";
        return exitCode;
    }
}

/// <summary>
/// A detached git worktree of the base ref, created for one comparison run and removed when the run ends. Only the
/// directory this instance created is removed.
/// </summary>
internal sealed class BaseWorktree : IDisposable
{
    private readonly string _repoRoot;
    private bool _removed;

    private BaseWorktree(string repoRoot, string root)
    {
        _repoRoot = repoRoot;
        Root = root;
    }

    /// <summary>Gets the root of the worktree.</summary>
    internal string Root { get; }

    /// <summary>Checks out <paramref name="baseRef"/> into the new directory <paramref name="path"/>.</summary>
    internal static BaseWorktree Create(string repoRoot, string baseRef, string path)
    {
        if (Directory.Exists(path))
        {
            throw new CompareInfrastructureException($"The base worktree directory already exists: {path}");
        }

        int exitCode = CompareTools.Run("git", ["worktree", "add", "--detach", path, baseRef], repoRoot, 300_000, out string output);
        if (exitCode != 0)
        {
            throw new CompareInfrastructureException($"Failed to create a git worktree for '{baseRef}':{Environment.NewLine}{output}");
        }

        return new BaseWorktree(repoRoot, path);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_removed)
        {
            return;
        }

        _removed = true;
        int exitCode = CompareTools.Run("git", ["worktree", "remove", "--force", Root], _repoRoot, 300_000, out string output);
        if (exitCode != 0)
        {
            Console.Error.WriteLine($"[fuzz-compare] git worktree remove failed for {Root}: {output}");
        }

        if (!Directory.Exists(Root))
        {
            return;
        }

        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"[fuzz-compare] Could not delete the base worktree {Root}; remove it by hand: {ex.Message}");
            return;
        }

        CompareTools.Run("git", ["worktree", "prune"], _repoRoot, 60_000, out _);
    }
}

/// <summary>Whether a compare project built, and what the generator wrote when it did.</summary>
internal sealed record GeneratorBuildResult(GeneratorOutputs? Outputs, string Log)
{
    /// <summary>Gets whether the build succeeded and wrote the native module.</summary>
    [MemberNotNullWhen(true, nameof(Outputs))]
    internal bool Succeeded => Outputs is not null;
}

/// <summary>
/// One side of the comparison: a pair of compare projects (<c>FuzzCompareCpu</c>, <c>FuzzCompareCuda</c>) that compile a
/// kernel file with the G# compiler of one repository tree, up to and including the generator, without compiling
/// the native code.
/// </summary>
internal sealed class CompareSide
{
    private const string KernelsFileName = "CompareTargetKernels.cu.cs";
    private const int BuildTimeoutMs = 240_000;

    private static readonly UTF8Encoding s_utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _directory;

    /// <summary>Writes the compare projects into <paramref name="directory"/>, referencing the compiler of <paramref name="repoRoot"/>.</summary>
    internal CompareSide(string directory, string repoRoot)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
        string relRepo = FuzzBatchWorkspaceWriter.NormalizeSlashes(Path.GetRelativePath(directory, repoRoot));
        File.WriteAllText(Path.Combine(directory, "Directory.Build.props"), FuzzBatchWorkspaceWriter.BuildDirectoryBuildProps(), s_utf8NoBom);
        File.WriteAllText(Path.Combine(directory, "FuzzCompareCpu.csproj"), BuildProjectXml("Cpu", relRepo), s_utf8NoBom);
        File.WriteAllText(Path.Combine(directory, "FuzzCompareCuda.csproj"), BuildProjectXml("Cuda", relRepo), s_utf8NoBom);
    }

    /// <summary>
    /// Builds <paramref name="kernelsSource"/> for <paramref name="dialect"/> and reads the generator output. The
    /// output of the previous build is deleted first; a file that cannot be deleted stops the comparison, because the
    /// build might not rewrite it and the comparison would read the previous batch.
    /// </summary>
    internal GeneratorBuildResult Build(string dialect, string kernelsSource)
    {
        string projectName = $"FuzzCompare{dialect}";
        string objDir = Path.Combine(_directory, "obj", FuzzHostOs.Name, projectName, "Debug", "net10.0");
        string binDir = Path.Combine(_directory, "bin", FuzzHostOs.Name, projectName, "Debug", "net10.0");
        string extension = string.Equals(dialect, "Cuda", StringComparison.Ordinal) ? "cu" : "cpp";
        string nativeModulePath = Path.Combine(objDir, $"Kassyi.GSharp.Kernels_{projectName}.g.{extension}");
        string generatedDir = Path.Combine(objDir, "generated", "Kassyi.GSharp.Compiler");

        DeleteStaleFile(nativeModulePath);
        DeleteStaleFile(Path.Combine(objDir, $"{projectName}.dll"));
        DeleteStaleFile(Path.Combine(binDir, $"{projectName}.dll"));
        DeleteStaleDirectory(generatedDir);

        File.WriteAllText(Path.Combine(_directory, KernelsFileName), kernelsSource, s_utf8NoBom);

        var args = new List<string>
        {
            "build", Path.Combine(_directory, $"{projectName}.csproj"), "-c", "Debug", "-m:1", "-nodeReuse:false",
            "--no-dependencies", "-p:GSharpSkipCpuCompilation=true", "-p:GSharpSkipCudaCompilation=true",
        };
        if (File.Exists(Path.Combine(_directory, "obj", FuzzHostOs.Name, projectName, "project.assets.json")))
        {
            args.Add("--no-restore");
        }

        int exitCode = CompareTools.Run("dotnet", args, _directory, BuildTimeoutMs, out string log);
        if (exitCode != 0 || !File.Exists(nativeModulePath))
        {
            return new GeneratorBuildResult(null, log);
        }

        var managedFiles = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (Directory.Exists(generatedDir))
        {
            foreach (string file in Directory.EnumerateFiles(generatedDir, "*.g.cs", SearchOption.AllDirectories))
            {
                managedFiles[Path.GetFileName(file)] = File.ReadAllText(file);
            }
        }

        return new GeneratorBuildResult(new GeneratorOutputs(File.ReadAllText(nativeModulePath), managedFiles), log);
    }

    private static void DeleteStaleFile(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new CompareInfrastructureException($"Cannot delete the previous build output {path}: {ex.Message}");
        }

        if (File.Exists(path))
        {
            throw new CompareInfrastructureException($"The previous build output is still there after deleting it: {path}");
        }
    }

    private static void DeleteStaleDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new CompareInfrastructureException($"Cannot delete the previous generator output {path}: {ex.Message}");
        }

        if (Directory.Exists(path))
        {
            throw new CompareInfrastructureException($"The previous generator output is still there after deleting it: {path}");
        }
    }

    private static string BuildProjectXml(string dialect, string relRepo)
    {
        string defineConstant = string.Equals(dialect, "Cuda", StringComparison.Ordinal) ? "GSHARP_CUDA" : "GSHARP_CPU";
        return $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Library</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
                <GSharpTargetDialect>{{dialect}}</GSharpTargetDialect>
                <GSharpEmitSource>true</GSharpEmitSource>
                <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
                <GSharpExportDeviceCode>false</GSharpExportDeviceCode>
                <GSharpGenerateSnapshotTestCases>false</GSharpGenerateSnapshotTestCases>
                <GSharpSkipCpuCompilation>true</GSharpSkipCpuCompilation>
                <GSharpSkipCudaCompilation>true</GSharpSkipCudaCompilation>
                <GSharpCompilerTaskAssembly>$(MSBuildThisFileDirectory){{relRepo}}/src/Compiler/Kassyi.GSharp.Compiler.Tasks/bin/$(GSharpHostOs)/$(Configuration)/netstandard2.0/Kassyi.GSharp.Compiler.Tasks.dll</GSharpCompilerTaskAssembly>
                <DefineConstants>$(DefineConstants);{{defineConstant}}</DefineConstants>
              </PropertyGroup>
              <ItemGroup>
                <CompilerVisibleProperty Include="GSharpTargetDialect" />
                <CompilerVisibleProperty Include="GSharpEmitSource" />
                <CompilerVisibleProperty Include="GSharpGenerateSnapshotTestCases" />
                <Compile Include="{{KernelsFileName}}" />
              </ItemGroup>
              <ItemGroup>
                <ProjectReference Include="{{relRepo}}/src/Core/Kassyi.GSharp.Core/Kassyi.GSharp.Core.csproj" />
                <ProjectReference Include="{{relRepo}}/src/Runtime/Kassyi.GSharp.Runtime/Kassyi.GSharp.Runtime.csproj" />
                <ProjectReference Include="{{relRepo}}/src/Compiler/Kassyi.GSharp.Compiler/Kassyi.GSharp.Compiler.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="true" />
                <ProjectReference Include="{{relRepo}}/src/Compiler/Kassyi.GSharp.Compiler.Tasks/Kassyi.GSharp.Compiler.Tasks.csproj" ReferenceOutputAssembly="false" />
              </ItemGroup>
              <UsingTask TaskName="Kassyi.GSharp.Compiler.Tasks.CompileCudaToFatbinTask" AssemblyFile="$(GSharpCompilerTaskAssembly)" />
              <UsingTask TaskName="Kassyi.GSharp.Compiler.Tasks.CompileCppToNativeTask" AssemblyFile="$(GSharpCompilerTaskAssembly)" />
              <Import Project="{{relRepo}}/src/Compiler/Kassyi.GSharp.Compiler/build/Kassyi.GSharp.Compiler.targets" />
            </Project>

            """;
    }
}

/// <summary>The <c>bin/&lt;os&gt;</c> and <c>obj/&lt;os&gt;</c> folder name of this host, as <c>Directory.Build.props</c> sets <c>GSharpHostOs</c>.</summary>
internal static class FuzzHostOs
{
    /// <summary>Gets <c>win</c>, <c>linux</c>, <c>osx</c> or <c>unix</c>.</summary>
    internal static string Name { get; } =
        OperatingSystem.IsWindows() ? "win" :
        OperatingSystem.IsLinux() ? "linux" :
        OperatingSystem.IsMacOS() ? "osx" : "unix";
}
