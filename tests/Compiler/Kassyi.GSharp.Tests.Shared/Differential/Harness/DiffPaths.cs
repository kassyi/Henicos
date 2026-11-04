#nullable enable
using System;
using System.Collections.Generic;
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

    /// <summary>Alias for <see cref="Managed"/>.</summary>
    CpuManaged = Managed,

    /// <summary>The native CPU build of Tests.Cpu: the transpiled C++.</summary>
    Native = 2,

    /// <summary>Alias for <see cref="Native"/>.</summary>
    CpuNative = Native,

    /// <summary>The CUDA build of Tests.Cuda.</summary>
    Cuda = 4,
}

/// <summary>
/// The host operating systems a <see cref="KnownDivergence"/> appears on. The native path is built by the host C++
/// compiler (MSVC on Windows, GCC or Clang elsewhere), so undefined behavior and overload choices can differ between them.
/// </summary>
[Flags]
public enum DiffHost
{
    /// <summary>Windows (MSVC).</summary>
    Windows = 1,

    /// <summary>Every other host (GCC or Clang).</summary>
    Unix = 2,

    /// <summary>Every host.</summary>
    Any = Windows | Unix,
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
    public string Name => DiffPaths.Format(Path);
}

/// <summary>
/// The paths of this test process (design D6: each process compares its own paths with the oracle). Tests.Cpu runs the
/// managed and the native path, Tests.Cuda the CUDA path.
/// </summary>
public static class DiffPaths
{
    /// <summary>Gets the shared <see cref="RunDirect"/> delegate instance.</summary>
    public static Action<Action> DirectRunner { get; } = RunDirect;

    /// <summary>Gets the paths this build runs.</summary>
    public static IReadOnlyList<DiffPathRunner> Available { get; } =
#if GSHARP_CPU
        [new(DiffPath.Managed, ManagedKernelRun.Run), Native(NativeLibraryPath(typeof(DiffPaths).Assembly))];
#elif GSHARP_CUDA
        [new(DiffPath.Cuda, DirectRunner)];
#else
        [];
#endif

    /// <summary>Gets a value indicating whether the current test assembly was built with <c>GSharpCheckedBuild=true</c>.</summary>
    public static bool IsCudaCheckedBuild { get; } = ResolveIsCudaCheckedBuild();

    /// <summary>
    /// Returns the execution paths on which runtime checks are expected to fire.
    /// When <paramref name="checkedBuildOnlyOnCuda"/> is <see langword="true"/> (e.g. for <c>[GpuUnchecked]</c> kernels),
    /// <see cref="DiffPath.Cuda"/> is included only when <see cref="IsCudaCheckedBuild"/> is <see langword="true"/>.
    /// </summary>
    public static DiffPath CheckedPaths(bool checkedBuildOnlyOnCuda = false) =>
        (!checkedBuildOnlyOnCuda || IsCudaCheckedBuild)
            ? (DiffPath.Managed | DiffPath.Native | DiffPath.Cuda)
            : (DiffPath.Managed | DiffPath.Native);

    /// <summary>Runs the launch on the calling thread: the launcher picks the native or CUDA module itself.</summary>
    /// <param name="launch">The launch.</param>
    public static void RunDirect(Action launch) => launch();

    /// <summary>Formats <paramref name="paths"/> using canonical lowercase path names (<c>managed</c>, <c>native</c>, <c>cuda</c>).</summary>
    public static string Format(DiffPath paths)
    {
        if (paths == DiffPath.None)
        {
            return "none";
        }

        var parts = new List<string>(3);
        if ((paths & DiffPath.Managed) != 0)
        {
            parts.Add("managed");
        }

        if ((paths & DiffPath.Native) != 0)
        {
            parts.Add("native");
        }

        if ((paths & DiffPath.Cuda) != 0)
        {
            parts.Add("cuda");
        }

        return string.Join(", ", parts);
    }

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

    private static bool ResolveIsCudaCheckedBuild()
    {
        foreach (var attr in typeof(DiffPaths).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (string.Equals(attr.Key, "GSharpCheckedBuild", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(attr.Value, "true", StringComparison.OrdinalIgnoreCase);
            }
        }

        return false;
    }
}
