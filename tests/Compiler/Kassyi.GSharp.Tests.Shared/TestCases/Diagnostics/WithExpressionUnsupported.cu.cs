using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

public record WithRecordClass(int X);

// A property whose init accessor has a body: the device struct emits a setter member function for it (#482)
public readonly struct WithBodyInitStruct
{
    private readonly int _value;

    public int Value
    {
        get => _value;
        init => _value = value < 0 ? 0 : value;
    }

    public int Other { get; init; }
}

public struct WithReadOnlyMemberStruct
{
    [GpuReadOnly] public int Fixed;
    public int Free;
}

public struct WithSetBodyStruct
{
    private int _scaled;

    public int Scaled
    {
        get => _scaled;
        set => _scaled = value * 2;
    }
}

// GSH0003: 'with' that the device cannot emit. A struct 'with' that assigns a field or a property is supported.
public static partial class WithExpressionUnsupportedKernels
{
    [GpuDeviceFunction]
    public static int ClassRecord(WithRecordClass source)
    {
        var copy = source with { X = 1 };
        return copy.X;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void BodyInitKernel(int[] data)
    {
        var source = new WithBodyInitStruct { Other = 1 };
        var copy = source with { Other = 2, Value = data[0] };
        data[1] = copy.Value + copy.Other;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void ReadOnlyMemberKernel(int[] data)
    {
        var source = new WithReadOnlyMemberStruct { Free = 1 };
        var copy = source with { Free = 2, Fixed = 3 };
        data[0] = copy.Free + copy.Fixed;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void ComputedPropertyMutationKernel(int[] data)
    {
        var s = new WithSetBodyStruct();
        s.Scaled += 1;
        s.Scaled++;
        int x = s.Scaled = 5;
        data[0] = x;
    }
}
