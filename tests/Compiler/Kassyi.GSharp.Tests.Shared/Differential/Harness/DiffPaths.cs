#nullable enable
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Compiler.Marshaling;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>The execution paths a differential test compares with the oracle.</summary>
[Flags]
public enum DiffPath
{
    /// <summary>No path.</summary>
    None = 0,

    /// <summary>The managed fallback of Tests.Cpu: the generator's C# rewrite of the kernel.</summary>
    Managed = 1,

    /// <summary>The native CPU build of Tests.Cpu: the transpiled C++.</summary>
    Native = 2,

    /// <summary>The CUDA build of Tests.Cuda.</summary>
    Cuda = 4,
}

/// <summary>Runs a launch on one path.</summary>
/// <param name="Path">The path, as the report and <see cref="KnownDivergence"/> name it.</param>
/// <param name="Run">Runs the launch it is given on the path.</param>
/// <param name="Preflight">
/// Checks, once before any seed runs, that the path really is the one it names; throws
/// <see cref="DifferentialHarnessException"/> when it is not. <see langword="null"/> when there is nothing to check.
/// </param>
public sealed record DiffPathRunner(DiffPath Path, Action<Action> Run, Action? Preflight = null)
{
    /// <summary>Gets the name the report prints: <c>managed</c>, <c>native</c> or <c>cuda</c>.</summary>
    public string Name => Path.ToString().ToLowerInvariant();
}

/// <summary>
/// The paths of this test process (design D6: each process compares its own paths with the oracle). Tests.Cpu runs the
/// managed and the native path, Tests.Cuda the CUDA path.
/// </summary>
public static class DiffPaths
{
    /// <summary>Gets the paths this build runs.</summary>
    public static IReadOnlyList<DiffPathRunner> Available { get; } =
#if GSHARP_CPU
        [new(DiffPath.Managed, ManagedKernelRun.Run), Native(NativeLibraryPath(typeof(DiffPaths).Assembly))];
#elif GSHARP_CUDA
        [new(DiffPath.Cuda, RunDirect)];
#else
        [];
#endif

    /// <summary>Runs the launch on the calling thread: the launcher picks the native or CUDA module itself.</summary>
    /// <param name="launch">The launch.</param>
    public static void RunDirect(Action launch) => launch();

    /// <summary>
    /// Returns the native path for the kernels whose library is <paramref name="dllPath"/>. Its preflight fails when the
    /// launcher would not load that library, since the launcher then runs the managed fallback without a word and the
    /// "native" path would only repeat the managed one.
    /// </summary>
    /// <param name="dllPath">The native kernel library, as <see cref="NativeLibraryPath"/> returns it.</param>
    public static DiffPathRunner Native(string dllPath) => new(DiffPath.Native, RunDirect, () => EnsureNativeLibrary(dllPath));

    /// <summary>
    /// Returns the path of the native kernel library that the generated launchers of <paramref name="assembly"/> load:
    /// <c>Kassyi.GSharp.Kernels_{assembly}_{arch}.dll</c> next to the test assembly (the same name as
    /// <c>CpuHostLaunchEmitter.WriteDllPath</c> emits).
    /// </summary>
    /// <param name="assembly">The assembly that declares the kernels.</param>
    public static string NativeLibraryPath(Assembly assembly)
    {
        string name = assembly.GetName().Name ?? throw new ArgumentException("the assembly has no name", nameof(assembly));
        string arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "_arm64" : "_x64";
        return Path.Combine(AppContext.BaseDirectory, $"Kassyi.GSharp.Kernels_{AotHostNames.ToSafeIdentifier(name)}{arch}.dll");
    }

    /// <summary>Throws when a launch on this thread would not load <paramref name="dllPath"/>.</summary>
    /// <param name="dllPath">The native kernel library.</param>
    /// <exception cref="DifferentialHarnessException">The library is missing, or managed execution is forced on this thread.</exception>
    public static void EnsureNativeLibrary(string dllPath)
    {
        if (!CpuKernelLoader.ShouldUseManagedExecution(dllPath))
        {
            return;
        }

        throw new DifferentialHarnessException(
            CpuKernelLoader.ForceManagedExecution
                ? $"managed execution is forced on this thread ({nameof(CpuKernelLoader)}.{nameof(CpuKernelLoader.ForceManagedExecution)}); the Native path would run managed"
                : $"native library not found: {dllPath}; the Native path would run managed"
        );
    }
}
