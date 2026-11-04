#nullable enable
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Transpiler.PendingBugs;

// #1084: ContextHoister hoists explicit-type `SharedMemory<int> s = new(8);` to outer state-machine scope
// while leaving sibling `var s = new SharedMemory<int>(8);` in an inner scope, causing CS0136 in the generated C#.
[SkipSnapshotConsistency("Pending bug verification")]
public static partial class SharedMemorySiblingScopeHoistKernels
{
    [GpuKernel]
    public static void SiblingExplicitAndVarSharedMemoryKernel(int[] output, int branch)
    {
        if (branch > 0)
        {
            SharedMemory<int> s = new(8);
            s[0] = 10;
            output[0] = s[0];
        }
        else
        {
            var s = new SharedMemory<int>(8);
            s[0] = 20;
            output[0] = s[0];
        }

        Gpu.SyncThreads();
    }

    [GpuKernel]
    public static void FlattenedSiblingExplicitAndVarSharedMemoryKernel(int[] output, int branch)
    {
        if (branch > 0)
        {
            SharedMemory<int> s = new(8);
            s[0] = 10;
            Gpu.SyncThreads();
            output[0] = s[0];
        }
        else
        {
            var s = new SharedMemory<int>(8);
            s[0] = 20;
            output[0] = s[0];
        }
    }
}
