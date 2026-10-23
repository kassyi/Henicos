using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

public struct WithPoint
{
    public int X;
    public int Y;
}

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

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void WithExpressionKernel(int[] data)
    {
        var point = new WithPoint { X = data[0], Y = data[1] };
        var moved = point with { X = 5 };
        data[0] = moved.X;
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
}
