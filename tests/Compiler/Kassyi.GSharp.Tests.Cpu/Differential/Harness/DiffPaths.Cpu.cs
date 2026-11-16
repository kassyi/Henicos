namespace Kassyi.GSharp.Tests.Shared.Differential;

public static partial class DiffPaths
{
    private static partial IReadOnlyList<DiffPathRunner> GetAvailablePaths() =>
        [new(DiffPath.Managed, ManagedKernelRun.Run), Native(NativeLibraryPath(typeof(DiffPaths).Assembly))];
}
