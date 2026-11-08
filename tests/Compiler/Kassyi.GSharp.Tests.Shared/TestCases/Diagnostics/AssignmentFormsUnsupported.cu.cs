using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

public struct DeconstructedPoint
{
    public int X;
    public int Y;

    public void Deconstruct(out int x, out int y)
    {
        x = X;
        y = Y;
    }
}

public struct NullableComputed
{
    private int _stored;

    public int? Value
    {
        get => _stored < 0 ? null : _stored;
        set => _stored = value ?? -1;
    }
}

// GSH0003 at the assignment forms that the device cannot write: each is reported at the construct, not as a C++ error
public static partial class AssignmentFormsUnsupportedKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void DeconstructNonTupleKernel(int[] data)
    {
        var point = new DeconstructedPoint { X = data[0], Y = data[1] };
        int x = 0, y = 0;
        (x, y) = point;
        data[2] = x + y;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void DeconstructionValueUsedKernel(int[] data)
    {
        int a = data[0], b = data[1];
        data[2] = ((a, b) = (b, a)).Item1;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void LiftedComputedPropertyKernel(int[] data)
    {
        var s = new NullableComputed();
        s.Value += 1;
        s.Value ??= 3;
        data[0] = s.Value ?? 0;
    }

    [GpuDeviceFunction]
    public static void SwapIf<T>(ref T a, ref T b, bool swap)
    {
        (a, b) = swap ? (b, a) : (a, b);
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void GenericConditionalDeconstructionKernel(int[] data)
    {
        int a = data[0], b = data[1];
        SwapIf(ref a, ref b, a > b);
        data[2] = a;
    }
}
