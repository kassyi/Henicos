// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// A value with operator macros for <c>&amp;</c> and <c>|</c> and its own <c>operator true</c> and
/// <c>operator false</c>, so <c>&amp;&amp;</c> and <c>||</c> apply to it (C# spec §12.14.3). The tests do not
/// complement each other: 0 is neither true nor false, so a test of the wrong operator shows.
/// </summary>
public struct TruthValue
{
    public int Value;

    public TruthValue(int value) => Value = value;

    [GpuOperatorMacro(typeof(OperatorMacroConditionalKernels), nameof(OperatorMacroConditionalKernels.And))]
    public static TruthValue operator &(TruthValue left, TruthValue right) => throw new GpuOnlyException();

    [GpuOperatorMacro(typeof(OperatorMacroConditionalKernels), nameof(OperatorMacroConditionalKernels.Or))]
    public static TruthValue operator |(TruthValue left, TruthValue right) => throw new GpuOnlyException();

    public static bool operator true(TruthValue value) => value.Value > 0;

    public static bool operator false(TruthValue value) => value.Value < 0;
}

/// <summary>
/// <c>&amp;&amp;</c> and <c>||</c> over operator macros: <c>a &amp;&amp; b</c> is <c>T.false(a) ? a : T.&amp;(a, b)</c>
/// and <c>a || b</c> is <c>T.true(a) ? a : T.|(a, b)</c>, with <c>a</c> evaluated once and <c>b</c> only when the test
/// fails. Each thread reads its left and right values from <c>input[2 * i]</c> and <c>input[2 * i + 1]</c>, and writes
/// its result to <c>result[2 * i]</c> and the number of times the right operand was evaluated to
/// <c>result[2 * i + 1]</c>. <see cref="OperatorMacroConditionalTests"/> checks the values.
/// </summary>
public static partial class OperatorMacroConditionalKernels
{
    // Not symmetric in left and right, so swapped operands show
    [GpuDeviceFunction]
    public static void And(ref TruthValue result, TruthValue left, TruthValue right)
    {
        result.Value = left.Value * 10 + right.Value;
    }

    [GpuDeviceFunction]
    public static void Or(ref TruthValue result, TruthValue left, TruthValue right)
    {
        result.Value = left.Value * 100 + right.Value;
    }

    // The right operand: counts its evaluations in counter[index]
    [GpuDeviceFunction]
    public static TruthValue Count(int[] counter, int index, int value)
    {
        counter[index] += 1;
        return new TruthValue(value);
    }

    // The left operand: counts its evaluations in counter[index] by 100, so evaluating it twice shows
    [GpuDeviceFunction]
    public static TruthValue CountLeft(int[] counter, int index, int value)
    {
        counter[index] += 100;
        return new TruthValue(value);
    }

    [GpuKernel]
    public static void AndKernel(int[] input, int[] result)
    {
        int i = ThreadIndex.X;
        TruthValue c = default;
        c = CountLeft(result, 2 * i + 1, input[2 * i]) && Count(result, 2 * i + 1, input[2 * i + 1]);
        result[2 * i] = c.Value;
    }

    [GpuKernel]
    public static void OrKernel(int[] input, int[] result)
    {
        int i = ThreadIndex.X;
        TruthValue c = default;
        c = CountLeft(result, 2 * i + 1, input[2 * i]) || Count(result, 2 * i + 1, input[2 * i + 1]);
        result[2 * i] = c.Value;
    }

    // The right operand is an operator macro itself (computed only when the test fails), the left operand is one
    // (computed before the test), and the conditional operator is an operand of an outer operator macro. Per thread:
    // result[4 * i] to result[4 * i + 2] are c, d and e, result[4 * i + 3] counts the right operands
    [GpuKernel]
    public static void NestedKernel(int[] input, int[] result)
    {
        int i = ThreadIndex.X;
        TruthValue a = new TruthValue(input[2 * i]);
        TruthValue two = new TruthValue(2);
        TruthValue c = default;
        c = a && (Count(result, 4 * i + 3, input[2 * i + 1]) & two);
        result[4 * i] = c.Value;

        // On the CPU dialects the barrier turns the kernel into a state machine: the expansion reads the lowered tree
        Gpu.SyncThreads();
        TruthValue d = default;
        d = (a | two) || Count(result, 4 * i + 3, input[2 * i + 1]);
        result[4 * i + 1] = d.Value;
        TruthValue e = default;
        e = (a && Count(result, 4 * i + 3, input[2 * i + 1])) | two;
        result[4 * i + 2] = e.Value;
    }

    // a && b && x is (a && b) && x; a || b && x is a || (b && x). Per thread: result[3 * i] and result[3 * i + 1] are
    // c and d, result[3 * i + 2] counts the right operands
    [GpuKernel]
    public static void ChainKernel(int[] input, int[] result)
    {
        int i = ThreadIndex.X;
        TruthValue a = new TruthValue(input[2 * i]);
        TruthValue b = new TruthValue(input[2 * i + 1]);
        TruthValue c = default;
        c = a && b && Count(result, 3 * i + 2, 3);
        result[3 * i] = c.Value;
        TruthValue d = default;
        d = a || b && Count(result, 3 * i + 2, 4);
        result[3 * i + 1] = d.Value;
    }

    // The target is an element whose index C# computes before the left operand, which writes the index: the result
    // goes to values[2 * i], and values[2 * i + 1] keeps its value
    [GpuKernel]
    public static void TargetOrderKernel(int[] input, TruthValue[] values, int[] index)
    {
        int i = ThreadIndex.X;
        values[2 * i + index[i]] = Advance(index, i, input[2 * i]) && new TruthValue(input[2 * i + 1]);
    }

    // Writes index[i] = 1 and returns value
    [GpuDeviceFunction]
    public static TruthValue Advance(int[] index, int i, int value)
    {
        index[i] = 1;
        return new TruthValue(value);
    }
}
