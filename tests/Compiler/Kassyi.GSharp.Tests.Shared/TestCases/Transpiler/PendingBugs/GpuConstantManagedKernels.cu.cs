#nullable enable
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Transpiler.PendingBugs;

// #641: Gpu.Constant("42") is transpiled to 42 in C++, but ManagedExecutionRewriter leaves Gpu.Constant("42")
// in the generated Managed C# output.
[SkipSnapshotConsistency("Pending bug verification")]
public static partial class GpuConstantManagedKernels
{
    [GpuKernel]
    public static void ReadConstantMacroKernel(int[] output)
    {
        int value = Gpu.Constant("42");
        output[0] = value;
    }
}
