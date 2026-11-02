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
/// tuple deconstruction, <c>record struct</c> / <c>readonly struct</c>, and pattern matching (Lane D6 Stage 1).
/// </summary>
public static partial class LanguageGenericsAndTypesDifferentialKernels
{
    public static readonly string[] GenericStructsAndNestedTypesColumns =
    [
        "int_box_val",
        "int_box_tag_plus_ten",
        "int_box_disposed_marker",
        "uint_box_val",
        "short_box_val",
        "nested_pair_first_val",
        "nested_pair_first_tag",
        "nested_pair_second",
        "ro_cell_weighted_sum",
        "nested_scope_add",
        "nested_scope_xor",
        "nested_scope_scale",
    ];

    public static readonly string[] ExtensionMethodsAndDelegatesColumns =
    [
        "val_ext_ret",
        "val_ext_caller_unchanged",
        "in_ext_ret",
        "ref_ext_mutated_val",
        "ref_ext_mutated_tag",
        "static_delegate_ret",
        "reduced_ext_delegate_captured_receiver",
        "caller_tag_before_in_mut",
        "in_ext_defensive_copy_after",
        "ro_field_defensive_copy_after",
    ];

    public static readonly string[] DeconstructionRecordsAndPatternsColumns =
    [
        "swap_a",
        "swap_b",
        "rot_x",
        "rot_y",
        "rot_z",
        "arr_target_order",
        "dup_target_last_wins",
        "widened_deconstruct_sum",
        "ro_record_with_combined",
        "ro_record_with_side_effect_once",
        "null_and_not_null_pattern_flags",
        "switch_expr_val_and_single_eval",
    ];

    [GpuDeviceFunction]
    public static int CombineStaticForDelegate(int a, int b) => (a * 5) - (b * 3) + 7;

    [GpuDeviceFunction]
    public static int StepCounter(ref int counter, int value)
    {
        counter++;
        return value;
    }

    [GpuKernel]
    public static void GenericStructsAndNestedTypesKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length)
        {
            return;
        }

        int x = xs[tid];
        int y = ys[tid];
        int o = tid * 12;

        int intBoxVal;
        int intBoxTagPlusTen;
        using (var intBox = new DiffGenScope<int>(x, y & 0xFF, r, o + 2))
        {
            intBoxVal = intBox.Value + (intBox.IsPositiveTag ? 1 : 0);
            intBoxTagPlusTen = intBox.TagPlusTen;
        }

        var uintBox = new DiffGenBox<uint>((uint)(x & 0x7FFF), 3);
        var shortBox = new DiffGenBox<short>((short)(y & 0x3FF), 7);
        var nestedPair = new DiffGenPair<DiffGenBox<short>, int>(shortBox, x - y);
        var roCell = new DiffReadonlyCell(x, y);

        var pAdd = new OuterTypeScope.MidTypeScope.NestedPayload(
            x,
            y,
            OuterTypeScope.MidTypeScope.NestedMode.Add);
        var pXor = new OuterTypeScope.MidTypeScope.NestedPayload(
            x,
            y,
            OuterTypeScope.MidTypeScope.NestedMode.Xor);
        var pScale = new OuterTypeScope.MidTypeScope.NestedPayload(
            x,
            y,
            OuterTypeScope.MidTypeScope.NestedMode.Scale);

        r[o + 0] = intBoxVal;
        r[o + 1] = intBoxTagPlusTen;
        // r[o + 2] is written by intBox.Dispose() upon exiting the using block above.
        r[o + 3] = (int)uintBox.Value + uintBox.Tag;
        r[o + 4] = shortBox.Value + shortBox.Tag;
        r[o + 5] = nestedPair.First.Value;
        r[o + 6] = nestedPair.First.Tag;
        r[o + 7] = nestedPair.Second;
        r[o + 8] = roCell.WeightedSum;
        r[o + 9] = OuterTypeScope.MidTypeScope.InnerHelper.EvaluatePayload(pAdd);
        r[o + 10] = OuterTypeScope.MidTypeScope.InnerHelper.EvaluatePayload(pXor);
        r[o + 11] = OuterTypeScope.MidTypeScope.InnerHelper.EvaluatePayload(pScale);
    }

    [GpuKernel]
    public static void ExtensionMethodsAndDelegatesKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length)
        {
            return;
        }

        int x = xs[tid];
        int y = ys[tid];
        int o = tid * 10;

        var box = new DiffGenBox<int>(x, y);

        // 1. Value-receiver extension method does not mutate caller's struct
        int valExtRet = box.AddValueScaled(3);
        int valExtUnchanged = box.Value;

        // 2. in-receiver extension method
        int inExtRet = box.ReadInWeighted(11);

        // 3. ref-receiver extension method mutates caller's struct in place
        box.AccumulateInPlace(5, 2);
        int refMutVal = box.Value;
        int refMutTag = box.Tag;

        // 4. Static [GpuDeviceFunction] method group conversion
        Func<int, int, int> staticFn = CombineStaticForDelegate;
        int staticDelegateRet = staticFn(x, y);

        // 5. Reduced extension method group (#940): receiver array reference is captured at delegate creation
        int[] receiverArr = xs;
        Func<int, int> boundExtFn = receiverArr.AddOffsetFromArray;
        receiverArr = ys;
        int boundDelegateRet = boundExtFn(tid);

        // 6. Readonly property access via `in` extension parameter and `readonly` struct field
        var probeBox = new DiffGenBox<int>(x, y);
        int tagBefore = probeBox.Tag;
        int tagAfterInExt = probeBox.ReadTagPlusTenOnIn();

        var roHolder = new DiffReadonlyBoxHolder(new DiffGenBox<int>(x, y));
        int roFieldTagAfter = roHolder.ReadonlyBox.TagPlusTen + roHolder.ReadonlyBox.Value;

        r[o + 0] = valExtRet;
        r[o + 1] = valExtUnchanged;
        r[o + 2] = inExtRet;
        r[o + 3] = refMutVal;
        r[o + 4] = refMutTag;
        r[o + 5] = staticDelegateRet;
        r[o + 6] = boundDelegateRet;
        r[o + 7] = tagBefore;
        r[o + 8] = tagAfterInExt;
        r[o + 9] = roFieldTagAfter;
    }

    [GpuKernel]
    public static void DeconstructionRecordsAndPatternsKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length)
        {
            return;
        }

        int x = xs[tid];
        int y = ys[tid];
        int o = tid * 12;

        // 1. Tuple deconstruction swap and 3-way rotation
        int a = x;
        int b = y;
        (a, b) = (b, a + b);

        int rx = x;
        int ry = y;
        int rz = x - y;
        (rx, ry, rz) = (ry, rz, rx + 1);

        // 2. Side-effecting array element targets in deconstruction (left-to-right target evaluation)
        Span<int> scratch = stackalloc int[2];
        int idx = 0;
        (scratch[idx++], scratch[idx++]) = (x + 10, y + 20);
        int arrTargetOrder = (scratch[0] * 3) + scratch[1] + (idx * 100);

        // 3. Duplicate target in deconstruction: later element overwrites earlier
        int dup = 0;
        (dup, dup) = (x + 11, y + 22);

        // 4. Implicit widening conversion during deconstruction
        short narrowA = (short)x;
        int narrowB = y;
        (int wideA, long wideB) = (narrowA, narrowB);
        int widenedSum = wideA + (int)wideB;

        // 5. readonly record struct with-expression and side-effect count
        var rec0 = new DiffReadonlyRecordPair(x, y);
        int sideCount = 0;
        var rec1 = rec0 with
        {
            Left = StepCounter(ref sideCount, x + 3),
            Bias = StepCounter(ref sideCount, 9),
        };
        var rec2 = rec1 with { Right = y - 4 };

        // 6. Pattern matching: is null / is not null on array and nullable array, plus switch expression with constant & discard arms
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

        r[o + 0] = a;
        r[o + 1] = b;
        r[o + 2] = rx;
        r[o + 3] = ry;
        r[o + 4] = rz;
        r[o + 5] = arrTargetOrder;
        r[o + 6] = dup;
        r[o + 7] = widenedSum;
        r[o + 8] = rec2.Combined;
        r[o + 9] = sideCount;
        r[o + 10] = nullFlags;
        r[o + 11] = bucket + (switchEvalCount * 1000);
    }
}
