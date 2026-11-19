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

    private NativeStackProbeLibrary(string directory, nint handle)
    {
        _directory = directory;
        _handle = handle;
    }

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

        var directory = CreateTempDirectory("gsharp-" + stem.Replace('_', '-') + "-");
        try
        {
            var sourcePath = Path.Combine(directory, stem + ".cpp");
            File.WriteAllText(
                sourcePath,
                GSharpPreludeResources.Cpp + GSharpPreludeResources.CppStackThreads + "\n" +
                RoslynTestUtility.ReadTestCase(testCase));
            var libraryPath = Path.Combine(directory, "lib" + stem + ".so");

            Assert.True(
                toolchain.TryCompileCppToSharedLibrary(sourcePath, libraryPath, "x64", log),
                string.Join(Environment.NewLine, engine.LoggedErrors));

            return new NativeStackProbeLibrary(directory, NativeLibrary.Load(libraryPath));
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
