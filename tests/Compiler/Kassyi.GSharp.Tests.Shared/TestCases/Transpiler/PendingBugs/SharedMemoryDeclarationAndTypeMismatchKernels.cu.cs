#nullable enable
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Transpiler.PendingBugs;

// #1085: SharedMemory<T> locals not initialized by a direct `new SharedMemory<T>(...)` (parenthesized creation,
// alias copy, or helper return value) are skipped by LocalDeclarationStatementTranspilerRule and omitted in C++.
[SkipSnapshotConsistency("Pending bug verification")]
public static partial class SharedMemoryDeclarationAndTypeMismatchKernels
{
    [GpuKernel]
    public static void ParenthesizedCreationAndAliasLocalKernel(int[] output)
    {
        var s = (new SharedMemory<int>(8));
        var alias = s;
        alias[0] = 42;
        output[0] = s[0];
    }

    [GpuDeviceFunction]
    public static SharedMemory<int> IdentitySharedMemory(SharedMemory<int> s) => s;

    [GpuKernel]
    public static void PassLocalSharedMemoryToHelperKernel(int[] output)
    {
        var localSmem = new SharedMemory<int>(8);
        SharedMemory<int> returned = IdentitySharedMemory(localSmem);
        returned[0] = 7;
        output[0] = localSmem[0];
    }
}
