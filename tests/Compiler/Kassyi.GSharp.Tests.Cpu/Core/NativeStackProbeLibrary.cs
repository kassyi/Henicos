#nullable enable
using System.Runtime.InteropServices;
using Kassyi.GSharp.Compiler.Tasks;
using Kassyi.GSharp.Compiler.Tasks.Helpers;
using Kassyi.GSharp.Compiler.Tasks.Toolchains;
using Kassyi.GSharp.Tests.Shared.Tasks;
using Microsoft.Build.Utilities;

namespace Kassyi.GSharp.Tests.Cpu.Core;

/// <summary>
/// A shared library compiled from the C++ prelude, the stack threads header and a probe from
/// <c>TestCases/Tasks/NativeStack</c>, loaded for the test and deleted with <see cref="Dispose"/>.
/// </summary>
internal sealed class NativeStackProbeLibrary : IDisposable
{
    private readonly string _directory;
    private readonly nint _handle;

    private NativeStackProbeLibrary(string directory, string libraryPath, nint handle)
    {
        _directory = directory;
        LibraryPath = libraryPath;
        _handle = handle;
    }

    internal string LibraryPath { get; }

    /// <summary>
    /// Compiles the probe with the GCC / Clang toolchain and loads it; skips the test where that is not an x86-64
    /// Linux machine with the toolchain. <paramref name="stem"/> names the temporary directory and the files.
    /// </summary>
    public static NativeStackProbeLibrary BuildOnLinuxX64(string testCase, string stem, string notLinuxReason)
    {
        Skip.IfNot(OperatingSystem.IsLinux(), notLinuxReason);
        Skip.IfNot(RuntimeInformation.ProcessArchitecture == Architecture.X64, "Checked on x86-64 Linux.");

        var engine = new MockBuildEngine();
        var log = new TaskLoggingHelper(engine, nameof(NativeStackToolchainTests));
        var toolchain = new GccClangToolchain(ProcessRunner.Default);
        Skip.If(
            toolchain.PrepareEnvironment(["x64"], log) != ToolchainPreparationResult.Ready,
            "GCC/Clang toolchain is not available.");

        var directory = CreateTempDirectory("gsharp-" + stem + "-");
        try
        {
            var sourcePath = Path.Combine(directory, stem + ".cpp");
            File.WriteAllText(
                sourcePath,
                GSharpPreludeResources.Cpp + GSharpPreludeResources.CppStackThreads + "\n" +
                RoslynTestUtility.ReadTestCase(testCase));
            var libraryPath = Path.Combine(directory, "lib" + stem + ".so");

            Assert.True(
                toolchain.TryCompileCppToSharedLibrary(sourcePath, libraryPath, "x64", log, runsOnNativeStacks: true),
                string.Join(Environment.NewLine, engine.LoggedErrors));

            return new NativeStackProbeLibrary(directory, libraryPath, NativeLibrary.Load(libraryPath));
        }
        catch
        {
            Directory.Delete(directory, recursive: true);
            throw;
        }
    }

    /// <summary>
    /// Compiles the probe on x86-64 Linux without <c>STB_GNU_UNIQUE</c> (<c>-fno-gnu-unique</c> on GCC, or default
    /// Clang) and loads it; skips where the host is not x86-64 Linux or lacks such a compiler.
    /// </summary>
    public static NativeStackProbeLibrary BuildOnLinuxX64WithoutGnuUnique(
        string testCase,
        string stem,
        string notLinuxReason)
    {
        Skip.IfNot(OperatingSystem.IsLinux(), notLinuxReason);
        Skip.IfNot(RuntimeInformation.ProcessArchitecture == Architecture.X64, "Checked on x86-64 Linux.");

        var engine = new MockBuildEngine();
        var log = new TaskLoggingHelper(engine, nameof(NativeStackToolchainTests));
        var toolchain = new GccClangToolchain(ProcessRunner.Default);
        Skip.If(
            toolchain.PrepareEnvironment(["x64"], log) != ToolchainPreparationResult.Ready,
            "GCC/Clang toolchain is not available.");

        var directory = CreateTempDirectory("gsharp-" + stem + "-");
        try
        {
            var sourcePath = Path.Combine(directory, stem + ".cpp");
            File.WriteAllText(
                sourcePath,
                GSharpPreludeResources.Cpp + GSharpPreludeResources.CppStackThreads + "\n" +
                RoslynTestUtility.ReadTestCase(testCase));
            var objPath = Path.Combine(directory, stem + ".o");
            var libraryPath = Path.Combine(directory, "lib" + stem + ".so");

            var identity = toolchain.GetCompilerIdentity("x64");
            var compiler = string.IsNullOrWhiteSpace(identity.Path) ? "g++" : identity.Path;
            var compileArgs = "-fno-gnu-unique " + toolchain.BuildCompileObjectArguments(
                sourcePath, objPath, "x64", runsOnNativeStacks: true);
            var compileResult = ProcessRunner.Default.Run(
                new System.Diagnostics.ProcessStartInfo
                {
                    FileName = compiler,
                    Arguments = compileArgs
                });
            if (compileResult.ExitCode != 0)
            {
                if (!identity.Version.Contains("clang", StringComparison.OrdinalIgnoreCase))
                {
                    Assert.Fail(
                        $"Expected {compiler} to compile with -fno-gnu-unique (exit {compileResult.ExitCode}): " +
                        compileResult.StandardError);
                }

                // Clang omits STB_GNU_UNIQUE by default and rejects GCC's -fno-gnu-unique flag.
                Assert.True(
                    toolchain.TryCompileToObject(sourcePath, objPath, "x64", log, runsOnNativeStacks: true),
                    string.Join(Environment.NewLine, engine.LoggedErrors));
            }

            Assert.True(
                toolchain.TryLinkSharedLibrary([objPath], libraryPath, "x64", log, runsOnNativeStacks: true),
                string.Join(Environment.NewLine, engine.LoggedErrors));

            return new NativeStackProbeLibrary(directory, libraryPath, NativeLibrary.Load(libraryPath));
        }
        catch
        {
            Directory.Delete(directory, recursive: true);
            throw;
        }
    }

    /// <summary>
    /// Compiles the probe for the current x86-64 host OS (MSVC on Windows, GCC/Clang on Linux) and loads it;
    /// skips the test when the OS or toolchain is unavailable.
    /// </summary>
    public static NativeStackProbeLibrary BuildForHostX64(string testCase, string stem, bool runsOnNativeStacks = true)
    {
        Skip.IfNot(
            OperatingSystem.IsWindows() || OperatingSystem.IsLinux(),
            "Native stack probes run on Windows or Linux.");
        Skip.IfNot(RuntimeInformation.ProcessArchitecture == Architecture.X64, "Checked on x86-64.");

        var engine = new MockBuildEngine();
        var log = new TaskLoggingHelper(engine, nameof(NativeStackToolchainTests));
        INativeToolchain toolchain = OperatingSystem.IsWindows()
            ? new MsvcToolchain(ProcessRunner.Default)
            : new GccClangToolchain(ProcessRunner.Default);
        Skip.If(
            toolchain.PrepareEnvironment(["x64"], log) != ToolchainPreparationResult.Ready,
            "C++ toolchain is not available.");

        var directory = CreateTempDirectory("gsharp-" + stem + "-");
        try
        {
            var sourcePath = Path.Combine(directory, stem + ".cpp");
            File.WriteAllText(
                sourcePath,
                GSharpPreludeResources.Cpp + GSharpPreludeResources.CppStackThreads + "\n" +
                RoslynTestUtility.ReadTestCase(testCase));
            var libraryFileName = OperatingSystem.IsWindows() ? stem + ".dll" : "lib" + stem + ".so";
            var libraryPath = Path.Combine(directory, libraryFileName);

            Assert.True(
                toolchain.TryCompileCppToSharedLibrary(
                    sourcePath, libraryPath, "x64", log, runsOnNativeStacks: runsOnNativeStacks),
                string.Join(Environment.NewLine, engine.LoggedErrors));

            return new NativeStackProbeLibrary(directory, libraryPath, NativeLibrary.Load(libraryPath));
        }
        catch
        {
            Directory.Delete(directory, recursive: true);
            throw;
        }
    }

    /// <summary>
    /// The address of the exported function <paramref name="name"/>, to be cast to its function pointer type.
    /// </summary>
    public nint GetExport(string name) => NativeLibrary.GetExport(_handle, name);

    /// <summary>Creates an empty directory under the temporary directory, named by <paramref name="prefix"/>.</summary>
    public static string CreateTempDirectory(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), prefix + Path.GetRandomFileName());
        Directory.CreateDirectory(path);
        return path;
    }

    public void Dispose()
    {
        NativeLibrary.Free(_handle);
        Directory.Delete(_directory, recursive: true);
    }
}
