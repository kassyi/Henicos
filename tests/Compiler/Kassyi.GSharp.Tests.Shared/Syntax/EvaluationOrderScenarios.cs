using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Launches and checks the kernels of <see cref="EvaluationOrderKernels"/>. The expected values follow the C# rules
/// (C# spec §12.4.1, §12.6.2.3, §12.21.2, §12.21.4); the native and managed test classes share these scenarios and
/// differ only in how a launch runs.
/// </summary>
internal static class EvaluationOrderScenarios
{
    public static void CallArguments(Action<Action> run)
    {
        int[] buffer = [3, 5, 8, 13];
        var scratch = new int[1];
        var result = new int[8];

        run(() => EvaluationOrderKernels.LaunchCallArgumentsKernel(buffer, scratch, result, blockDimX: 1));

        // Sub(Next(ref i), Next(ref i)) = 0 - 1
        Assert.Equal(-1, result[0]);
        // new EoPair(buffer[j++], buffer[j++]): X gets the first element
        Assert.Equal(3, result[1]);
        Assert.Equal(5, result[2]);
        // Sub(t, Next(ref t)) reads t = 7 before Next returns 7
        Assert.Equal(0, result[3]);
        // Sub(scratch[0], Store(scratch, 5)) reads 40 before Store writes 5
        Assert.Equal(35, result[4]);
        // SplitOut(out product, Next(ref k), k): a = 0, then b = k = 1
        Assert.Equal(1, result[5]);
        Assert.Equal(0, result[6]);
        // ReadIn(in buffer[m++], buffer[m++]) = buffer[0] * 10 + buffer[1]
        Assert.Equal(35, result[7]);
    }

    public static void VectorArguments(Action<Action> run)
    {
        float[] buffer = [1f, 2f, 3f];
        var result = new float[6];

        run(() => EvaluationOrderKernels.LaunchVectorArgumentsKernel(buffer, result, blockDimX: 1));

        Assert.Equal([1f, 2f, 3f, 1f, 2f, 3f], result);
    }

    public static void NoSpillCalls(Action<Action> run)
    {
        int[] a = [4];
        int[] b = [9];
        var result = new int[3];

        run(() => EvaluationOrderKernels.LaunchNoSpillCallsKernel(a, b, result, blockDimX: 1));

        Assert.Equal(13 * 10 + 4, result[0]);
        Assert.Equal(4 * 10 + 9, result[1]);
        Assert.Equal(5, result[2]);
    }

    public static void SuspendingCallArguments(Action<Action> run)
    {
        var result = new int[2];

        run(() => EvaluationOrderKernels.LaunchSuspendingCallArgumentsKernel(result, blockDimX: 1));

        // CombineAcrossBarrier(a, Next(ref a)) reads a = 0 before Next increments it
        Assert.Equal(0, result[0]);
        Assert.Equal(1, result[1]);
    }

    public static void ReadsAndAliases(Action<Action> run)
    {
        var result = new int[4];

        run(() => EvaluationOrderKernels.LaunchReadsAndAliasesKernel(result, blockDimX: 1));

        // Sub(ReadRef(ref i), Next(ref i)) = 0 - 0; Sub(y, SetThrough(&y, 9)) = 5 - 0;
        // ReadInLong(1, BumpShort(ref s)) = 1 * 10 + 0; Combine(Pure(0), 1) = 1 * 10 + 1
        Assert.Equal([0, 5, 10, 11], result);
    }

    public static void RefReturn(Action<Action> run)
    {
        var a = new int[4];
        var b = new int[4];
        var result = new int[1];

        run(() => EvaluationOrderKernels.LaunchRefReturnKernel(a, b, result, blockDimX: 1));

        // Pick(a, 0, 1) = 5; Pick(b, 0, 1) = 1 (the target is found before the value runs Next again)
        Assert.Equal([0, 5, 0, 0], a);
        Assert.Equal([0, 1, 0, 0], b);
        // Sub(x, Next(ref x)) = 5 - 5
        Assert.Equal(0, result[0]);
    }

    public static void Conversions(Action<Action> run)
    {
        var floats = new float[4];
        byte[] bytes = [10, 0, 0, 0];
        var result = new int[1];

        run(() => EvaluationOrderKernels.LaunchConversionsKernel(floats, bytes, result, blockDimX: 1));

        // floats[1] = 1, converted to float
        Assert.Equal([0f, 1f, 0f, 0f], floats);
        // bytes[0] = (byte)(10 + (byte)(1 + 250)) = (byte)261 = 5
        Assert.Equal([5, 0, 0, 0], bytes);
        // flags = A | B: flags is read before Replace sets it to C
        Assert.Equal((int)(EoFlags.A | EoFlags.B), result[0]);
    }

    public static void ConditionalBranches(Action<Action> run, int selector)
    {
        int[] input = [selector];
        var result = new int[4];

        run(() => EvaluationOrderKernels.LaunchConditionalBranchesKernel(input, result, blockDimX: 1));

        Assert.Equal(selector > 0 ? -1 : 100, result[0]);
        Assert.Equal(selector > 0 ? 2 : 0, result[1]);
        Assert.Equal(selector > 0 ? 1 : 0, result[2]);
        Assert.Equal(selector > 0 ? 2 : 0, result[3]);
    }

    public static void OperatorMacro(Action<Action> run)
    {
        var result = new int[1];

        run(() => EvaluationOrderKernels.LaunchOperatorMacroKernel(result, blockDimX: 1));

        // c = Take(ref i) - Take(ref i) calls CombineInto(ref c, 0, 1)
        Assert.Equal(1, result[0]);
    }

    public static void NoSpillOperatorMacro(Action<Action> run)
    {
        int[] input = [4, 9];
        var result = new int[1];

        run(() => EvaluationOrderKernels.LaunchNoSpillOperatorMacroKernel(input, result, blockDimX: 1));

        Assert.Equal(49, result[0]);
    }

    public static void Assignment(Action<Action> run)
    {
        var a = new int[4];
        var b = new int[4];
        var e = new int[4];

        run(() => EvaluationOrderKernels.LaunchAssignmentKernel(a, b, e, blockDimX: 1));

        // a[i] = Next(ref i) with i = 1: the element is chosen before Next increments i
        Assert.Equal([0, 1, 0, 0], a);
        // b[c++] = c: the index is evaluated (and c incremented) before the value is read
        Assert.Equal([1, 0, 0, 0], b);
        // e[++n] = n
        Assert.Equal([0, 1, 0, 0], e);
    }

    public static void CompoundAssignment(Action<Action> run)
    {
        var d = new int[4];
        var result = new int[1];

        run(() => EvaluationOrderKernels.LaunchCompoundAssignmentKernel(d, result, blockDimX: 1));

        // d[Next(ref i)] += Next(ref i): d[0] += 1
        Assert.Equal([1, 0, 0, 0], d);
        // sum += Accumulate(ref sum): sum is read (5) before Accumulate adds 10, then 5 + 1 is stored
        Assert.Equal(6, result[0]);
    }

    public static void NoSpillAssignments(Action<Action> run)
    {
        var output = new int[1];
        var log = new int[4];
        var result = new int[1];

        run(() => EvaluationOrderKernels.LaunchNoSpillAssignmentsKernel(output, log, result, blockDimX: 1));

        Assert.Equal(0, output[0]);
        Assert.Equal([2, 1, 1, 1], log);
        Assert.Equal(0 + 2 + 4 + 6, result[0]);
    }

    public static void BinaryOperator(Action<Action> run)
    {
        var data = new int[1];
        var result = new int[5];

        run(() => EvaluationOrderKernels.LaunchBinaryOperatorKernel(data, result, blockDimX: 1));

        // Next(ref i) - Next(ref i) = 0 - 1
        Assert.Equal(-1, result[0]);
        // Next(ref j) * 10 + Next(ref j) = 0 * 10 + 1
        Assert.Equal(1, result[1]);
        // data[0] + Bump(data) reads 7 before Bump adds 100
        Assert.Equal(7, result[2]);
        Assert.Equal(107, data[0]);
        // (Next(ref k) << 4) | Next(ref k) = (0 << 4) | 1
        Assert.Equal(1, result[3]);
        // Next(ref m) < Next(ref m) is 0 < 1
        Assert.Equal(1, result[4]);
    }

    public static void NoSpillBinary(Action<Action> run)
    {
        uint[] input = [0x12345678u];
        int[] a = [20];
        int[] b = [22];
        var rotated = new uint[1];
        var sums = new int[1];

        run(() => EvaluationOrderKernels.LaunchNoSpillBinaryKernel(input, a, b, rotated, sums, blockDimX: 1));

        Assert.Equal(uint.RotateRight(0x12345678u, 2) ^ uint.RotateRight(0x12345678u, 13), rotated[0]);
        Assert.Equal(42, sums[0]);
    }
}
