using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

// GSH0003: syntax that no transpiler rule handles, and function pointer types
public static unsafe partial class UnsupportedSyntaxNodeKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void CheckedKernel(int[] data)
    {
        data[0] = checked(data[0] + 1);
        unchecked
        {
            data[1] = data[1] + 1;
        }
    }

    [GpuDeviceFunction]
    public static int CallPointer(delegate*<int, int> fn, int value)
    {
        return value;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void FunctionPointerVariableKernel(int[] data)
    {
        delegate*<int, int> fn = null;
        data[0] = CallPointer(fn, data[0]);
    }

    // GSH0002: an implicitly typed string variable
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void StringVariableKernel(int[] data)
    {
        var text = "abc";
        data[0] = 1;
    }
}
