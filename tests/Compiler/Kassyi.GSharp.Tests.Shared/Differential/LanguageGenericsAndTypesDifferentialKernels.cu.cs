#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public struct DiffGenBox<T>
    where T : unmanaged
{
    public T Value;
    public int Tag;

    public DiffGenBox(T val, int tag)
    {
        Value = val;
        Tag = tag;
    }

    public bool IsPositiveTag => Tag > 0;

    public int TagPlusTen => Tag + 10;
}

public struct DiffGenScope<T> : IDisposable
    where T : unmanaged
{
    public T Value;
    public int Tag;
    public int[] Log;
    public int LogSlot;

    public DiffGenScope(T val, int tag, int[] log, int logSlot)
    {
        Value = val;
        Tag = tag;
        Log = log;
        LogSlot = logSlot;
    }

    public bool IsPositiveTag => Tag > 0;

    public int TagPlusTen => Tag + 10;

    public void Dispose()
    {
        Log[LogSlot] = Tag + 1000;
    }
}

public struct DiffGenPair<TFirst, TSecond>
    where TFirst : unmanaged
    where TSecond : unmanaged
{
    public TFirst First;
    public TSecond Second;

    public DiffGenPair(TFirst first, TSecond second)
    {
        First = first;
        Second = second;
    }
}

public readonly struct DiffReadonlyCell
{
    public readonly int X;
    public readonly int Y;

    public DiffReadonlyCell(int x, int y)
    {
        X = x;
        Y = y;
    }

    public int WeightedSum => (X * 3) + Y;
}

public struct DiffReadonlyBoxHolder
{
    public readonly DiffGenBox<int> ReadonlyBox;

    public DiffReadonlyBoxHolder(DiffGenBox<int> box)
    {
        ReadonlyBox = box;
    }
}

public readonly record struct DiffReadonlyRecordPair(int Left, int Right)
{
    public int Bias { get; init; } = 5;

    public int Combined => (Left * 2) + Right + Bias;
}

public static partial class OuterTypeScope
{
    public static partial class MidTypeScope
    {
        public enum NestedMode
        {
            Add = 0,
            Xor = 1,
            Scale = 2,
        }

        public struct NestedPayload
        {
            public int A;
            public int B;
            public NestedMode Mode;

            public NestedPayload(int a, int b, NestedMode mode)
            {
                A = a;
                B = b;
                Mode = mode;
            }
        }

        public static partial class InnerHelper
        {
            [GpuDeviceFunction]
            public static int EvaluatePayload(NestedPayload payload) =>
                payload.Mode switch
                {
                    NestedMode.Add => payload.A + payload.B,
                    NestedMode.Xor => payload.A ^ payload.B,
                    NestedMode.Scale => (payload.A * 3) - payload.B,
                    _ => 0,
                };
        }
    }
}

internal static partial class DiffGenBoxExtensions
{
    [GpuDeviceFunction]
    public static int AddValueScaled(this DiffGenBox<int> box, int factor)
    {
        box.Value += 999;
        return (box.Value - 999) * factor + box.Tag;
    }

    [GpuDeviceFunction]
    public static int ReadInWeighted(in this DiffGenBox<int> box, int offset) =>
        (box.Value * 2) + box.Tag + offset;

    [GpuDeviceFunction]
    public static void AccumulateInPlace(ref this DiffGenBox<int> box, int deltaValue, int deltaTag)
    {
        box.Value += deltaValue;
        box.Tag += deltaTag;
    }

    [GpuDeviceFunction]
    public static int AddOffsetFromArray(this int[] arr, int index) =>
        arr[index] + 40;

    [GpuDeviceFunction]
    public static int ReadTagPlusTenOnIn(in this DiffGenBox<int> box) =>
        box.TagPlusTen + (box.IsPositiveTag ? 5 : 0);
}

/// <summary>
/// Differential tests for C# generics, nested types, extension methods, reduced extension method groups,
/// tuple deconstruction, <c>record struct</c> / <c>readonly struct</c>, and pattern matching.
/// </summary>
public static partial class LanguageGenericsAndTypesDifferentialKernels
{
    [GpuDeviceFunction]
    public static int CombineStaticForDelegate(int a, int b) => (a * 5) - (b * 3) + 7;

    [GpuDeviceFunction]
    public static int StepCounter(ref int counter, int value)
    {
        counter++;
        return value;
    }

    // 1. Generic Structs & Nested Types
    [GpuKernel]
    public static void GenericStructScopeUsingKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length)
        {
            return;
        }

        int x = xs[tid];
        int y = ys[tid];
        int o = tid * 3;

        using (var intBox = new DiffGenScope<int>(x, y & 0xFF, r, o + 2))
        {
            r[o + 0] = intBox.Value + (intBox.IsPositiveTag ? 1 : 0);
            r[o + 1] = intBox.TagPlusTen;
        }
    }

    [GpuKernel]
    public static void GenericStructPrimitivesAndNestingKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length)
        {
            return;
        }

        int x = xs[tid];
        int y = ys[tid];
        int o = tid * 5;

        var uintBox = new DiffGenBox<uint>((uint)(x & 0x7FFF), 3);
        var shortBox = new DiffGenBox<short>((short)(y & 0x3FF), 7);
        var nestedPair = new DiffGenPair<DiffGenBox<short>, int>(shortBox, x - y);

        r[o + 0] = (int)uintBox.Value + uintBox.Tag;
        r[o + 1] = shortBox.Value + shortBox.Tag;
        r[o + 2] = nestedPair.First.Value;
        r[o + 3] = nestedPair.First.Tag;
        r[o + 4] = nestedPair.Second;
    }

    [GpuKernel]
    public static void ReadonlyStructWeightedSumKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length)
        {
            return;
        }

        var roCell = new DiffReadonlyCell(xs[tid], ys[tid]);
        r[tid] = roCell.WeightedSum;
    }

    [GpuKernel]
    public static void NestedTypesInNamespaceAndClassKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length)
        {
            return;
        }

        int x = xs[tid];
        int y = ys[tid];
        int o = tid * 3;

        var pAdd = new OuterTypeScope.MidTypeScope.NestedPayload(x, y, OuterTypeScope.MidTypeScope.NestedMode.Add);
        var pXor = new OuterTypeScope.MidTypeScope.NestedPayload(x, y, OuterTypeScope.MidTypeScope.NestedMode.Xor);
        var pScale = new OuterTypeScope.MidTypeScope.NestedPayload(x, y, OuterTypeScope.MidTypeScope.NestedMode.Scale);

        r[o + 0] = OuterTypeScope.MidTypeScope.InnerHelper.EvaluatePayload(pAdd);
        r[o + 1] = OuterTypeScope.MidTypeScope.InnerHelper.EvaluatePayload(pXor);
        r[o + 2] = OuterTypeScope.MidTypeScope.InnerHelper.EvaluatePayload(pScale);
    }

    // 2. Extension Methods & Delegates
    [GpuKernel]
    public static void ValueReceiverExtensionMethodKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length)
        {
            return;
        }

        int o = tid * 2;
        var box = new DiffGenBox<int>(xs[tid], ys[tid]);
        int valExtRet = box.AddValueScaled(3);
        r[o + 0] = valExtRet;
        r[o + 1] = box.Value;
    }

    [GpuKernel]
    public static void InReceiverExtensionMethodKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length)
        {
            return;
        }

        var box = new DiffGenBox<int>(xs[tid], ys[tid]);
        r[tid] = box.ReadInWeighted(11);
    }

    [GpuKernel]
    public static void RefReceiverExtensionMethodKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length)
        {
            return;
        }

        int o = tid * 2;
        var box = new DiffGenBox<int>(xs[tid], ys[tid]);
        box.AccumulateInPlace(5, 2);
        r[o + 0] = box.Value;
        r[o + 1] = box.Tag;
    }

    [GpuKernel]
    public static void StaticMethodGroupConversionKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length)
        {
            return;
        }

        Func<int, int, int> staticFn = CombineStaticForDelegate;
        r[tid] = staticFn(xs[tid], ys[tid]);
    }

    [GpuKernel]
    public static void ReducedExtensionMethodGroupKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length)
        {
            return;
        }

        int[] receiverArr = xs;
        Func<int, int> boundExtFn = receiverArr.AddOffsetFromArray;
        receiverArr = ys;
        r[tid] = boundExtFn(tid);
    }

    [GpuKernel]
    public static void ReadonlyPropertyAccessOnInAndFieldKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length)
        {
            return;
        }

        int x = xs[tid];
        int y = ys[tid];
        int o = tid * 3;

        var probeBox = new DiffGenBox<int>(x, y);
        int tagBefore = probeBox.Tag;
        int tagAfterInExt = probeBox.ReadTagPlusTenOnIn();

        var roHolder = new DiffReadonlyBoxHolder(new DiffGenBox<int>(x, y));
        int roFieldTagAfter = roHolder.ReadonlyBox.TagPlusTen + roHolder.ReadonlyBox.Value;

        r[o + 0] = tagBefore;
        r[o + 1] = tagAfterInExt;
        r[o + 2] = roFieldTagAfter;
    }

    // 3. Deconstruction, Records & Patterns
    [GpuKernel]
    public static void TupleDeconstructionSwapAndRotationKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length)
        {
            return;
        }

        int x = xs[tid];
        int y = ys[tid];
        int o = tid * 5;

        int a = x;
        int b = y;
        (a, b) = (b, a + b);

        int rx = x;
        int ry = y;
        int rz = x - y;
        (rx, ry, rz) = (ry, rz, rx + 1);

        r[o + 0] = a;
        r[o + 1] = b;
        r[o + 2] = rx;
        r[o + 3] = ry;
        r[o + 4] = rz;
    }

    [GpuKernel]
    public static void DeconstructionSideEffectingTargetOrderKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length)
        {
            return;
        }

        int x = xs[tid];
        int y = ys[tid];
        Span<int> scratch = stackalloc int[2];
        int idx = 0;
        (scratch[idx++], scratch[idx++]) = (x + 10, y + 20);
        r[tid] = (scratch[0] * 3) + scratch[1] + (idx * 100);
    }

    [GpuKernel]
    public static void DeconstructionDuplicateTargetOverwriteKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length)
        {
            return;
        }

        int x = xs[tid];
        int y = ys[tid];
        int dup = 0;
        (dup, dup) = (x + 11, y + 22);
        r[tid] = dup;
    }

    [GpuKernel]
    public static void DeconstructionImplicitWideningKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length)
        {
            return;
        }

        short narrowA = (short)xs[tid];
        int narrowB = ys[tid];
        (int wideA, long wideB) = (narrowA, narrowB);
        r[tid] = wideA + (int)wideB;
    }

    [GpuKernel]
    public static void ReadonlyRecordStructWithExpressionKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length)
        {
            return;
        }

        int x = xs[tid];
        int y = ys[tid];
        int o = tid * 2;

        var rec0 = new DiffReadonlyRecordPair(x, y);
        int sideCount = 0;
        var rec1 = rec0 with
        {
            Left = StepCounter(ref sideCount, x + 3),
            Bias = StepCounter(ref sideCount, 9),
        };
        var rec2 = rec1 with { Right = y - 4 };

        r[o + 0] = rec2.Combined;
        r[o + 1] = sideCount;
    }

    [GpuKernel]
    public static void PatternMatchingAndSwitchExpressionKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length)
        {
            return;
        }

        int x = xs[tid];
        int y = ys[tid];
        int o = tid * 2;

        int[]? nullArr = default;
        int[]? nonNullArr = ys;
        int nullFlags =
            (xs is not null ? 1 : 0) |
            (ys is null ? 2 : 0) |
            (nullArr is null ? 4 : 0) |
            (nonNullArr is not null ? 8 : 0);

        int switchEvalCount = 0;
        int bucket = StepCounter(ref switchEvalCount, x & 3) switch
        {
            0 => y + 100,
            1 => y + 200,
            2 => y + 300,
            _ => y + 400,
        };

        r[o + 0] = nullFlags;
        r[o + 1] = bucket + (switchEvalCount * 1000);
    }
}
