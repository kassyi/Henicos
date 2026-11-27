using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// A value whose <c>operator true</c> and <c>operator false</c> take <see langword="in"/> parameters, verifying
/// by-reference truth-test lowering alongside <see cref="TruthValue"/>.
/// </summary>
public struct TruthInValue
{
    public int Value;

    public TruthInValue(int value) => Value = value;

    [GpuOperatorMacro(typeof(OperatorMacroConditionalSplitKernels), nameof(OperatorMacroConditionalSplitKernels.AndIn))]
    public static TruthInValue operator &(TruthInValue left, TruthInValue right) => throw new GpuOnlyException();

    [GpuOperatorMacro(typeof(OperatorMacroConditionalSplitKernels), nameof(OperatorMacroConditionalSplitKernels.OrIn))]
    public static TruthInValue operator |(TruthInValue left, TruthInValue right) => throw new GpuOnlyException();

    public static bool operator true(in TruthInValue value) => value.Value > 0;

    public static bool operator false(in TruthInValue value) => value.Value < 0;
}

/// <summary>
/// User-defined <c>&amp;&amp;</c> and <c>||</c> over operator macros whose right (or left) operand reaches a barrier or
/// <c>Warp.ActiveMask()</c> split point (<see cref="OperatorMacroConditionalSplitTests"/>).
/// </summary>
[SkipSnapshotConsistency("Verified by OperatorMacroConditionalSplitTests on every path; uses TruthValue from OperatorMacroConditionalKernels")]
public static partial class OperatorMacroConditionalSplitKernels
{
    [GpuDeviceFunction]
    public static TruthValue MaskValue(int[] counter, int index)
    {
        counter[index] += 1;
        return new TruthValue((int)(Warp.ActiveMask() & 0xFFu));
    }

    [GpuDeviceFunction]
    public static TruthValue BarrierValue(int[] counter, int index)
    {
        counter[index] += 1;
        Gpu.SyncThreads();
        return new TruthValue(2);
    }

    [GpuDeviceFunction]
    public static void AndIn(ref TruthInValue result, TruthInValue left, TruthInValue right)
    {
        result.Value = left.Value * 10 + right.Value;
    }

    [GpuDeviceFunction]
    public static void OrIn(ref TruthInValue result, TruthInValue left, TruthInValue right)
    {
        result.Value = left.Value * 100 + right.Value;
    }

    [GpuKernel]
    public static void AndBarrierKernel(int[] input, int[] output, int[] count)
    {
        int t = ThreadIndex.X;
        TruthValue c = default;
        c = new TruthValue(input[t]) && BarrierValue(count, t);
        output[t] = c.Value;
    }

    [GpuKernel]
    public static void OrMaskKernel(int[] input, int[] output, int[] count)
    {
        int t = ThreadIndex.X;
        TruthValue c = default;
        c = new TruthValue(input[t]) || MaskValue(count, t);
        output[t] = c.Value;
    }

    [GpuKernel]
    public static void AndMaskKernel(int[] input, int[] output, int[] count)
    {
        int t = ThreadIndex.X;
        TruthValue c = default;
        c = new TruthValue(input[t]) && MaskValue(count, t);
        output[t] = c.Value;
    }

    [GpuKernel]
    public static void OperandOfMacroKernel(int[] input, int[] output, int[] count)
    {
        int t = ThreadIndex.X;
        TruthValue a = new TruthValue(input[t]);
        TruthValue two = new TruthValue(2);
        TruthValue e = default;
        e = (a && MaskValue(count, t)) | two;
        output[t] = e.Value;
    }

    [GpuKernel]
    public static void RightIsMacroKernel(int[] input, int[] output, int[] count)
    {
        int t = ThreadIndex.X;
        TruthValue a = new TruthValue(input[t]);
        TruthValue two = new TruthValue(2);
        TruthValue c = default;
        c = a && (MaskValue(count, t) & two);
        output[t] = c.Value;
    }

    [GpuKernel]
    public static void ChainKernel(int[] input, int[] output, int[] count)
    {
        int t = ThreadIndex.X;
        TruthValue a = new TruthValue(input[t]);
        TruthValue b = new TruthValue(t - 3);
        TruthValue c = default;
        c = a || b && MaskValue(count, t);
        output[t] = c.Value;
    }

    [GpuKernel]
    public static void LeftOnceKernel(int[] input, int[] output, int[] count)
    {
        int t = ThreadIndex.X;
        TruthValue c = default;
        c = OperatorMacroConditionalKernels.CountLeft(count, t, input[t]) && MaskValue(count, t);
        output[t] = c.Value;
    }

    [GpuKernel]
    public static void LeftAndRightSplitKernel(int[] output, int[] count)
    {
        int t = ThreadIndex.X;
        TruthValue c = default;
        c = BarrierValue(count, t) && MaskValue(count, t);
        output[t] = c.Value;
    }

    [GpuKernel]
    public static void TargetOrderSplitKernel(int[] input, TruthValue[] values, int[] index, int[] count)
    {
        int i = ThreadIndex.X;
        values[2 * i + index[i]] = OperatorMacroConditionalKernels.Advance(index, i, input[i]) && MaskValue(count, i);
    }

    [GpuKernel]
    public static void AndInMaskKernel(int[] input, int[] output, int[] count)
    {
        int t = ThreadIndex.X;
        TruthInValue c = default;
        c = new TruthInValue(input[t]) && new TruthInValue(MaskValue(count, t).Value);
        output[t] = c.Value;
    }
}
