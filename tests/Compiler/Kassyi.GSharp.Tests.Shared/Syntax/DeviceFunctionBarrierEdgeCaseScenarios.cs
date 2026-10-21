using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Launches and checks the kernels of <see cref="DeviceFunctionBarrierEdgeCaseKernels"/>. The native and managed test
/// classes share these scenarios and differ only in how a launch runs.
/// </summary>
internal static class DeviceFunctionBarrierEdgeCaseScenarios
{
    public const int Length = 64;

    public static void CallerAndSuspendingCalleeSharedMemory(Action<Action> run)
    {
        var output = new int[Length];

        run(() => DeviceFunctionBarrierEdgeCaseKernels.LaunchCallerAndSuspendingCalleeSharedMemoryKernel(output,
            blockDimX: Length));

        for (int i = 0; i < Length; i++)
        {
            Assert.Equal(i + 100 + i * 2, output[i]);
        }
    }

    public static void CallerAndPlainCalleeSharedMemory(Action<Action> run)
    {
        var output = new int[Length];

        run(() => DeviceFunctionBarrierEdgeCaseKernels.LaunchCallerAndPlainCalleeSharedMemoryKernel(output,
            blockDimX: Length));

        for (int i = 0; i < Length; i++)
        {
            Assert.Equal(i + 100 + i + 1, output[i]);
        }
    }

    public static void SharedMemoryPerDeclaration(Action<Action> run)
    {
        var output = new int[Length];

        run(() => DeviceFunctionBarrierEdgeCaseKernels.LaunchSharedMemoryPerDeclarationKernel(output, blockDimX: Length));

        for (int i = 0; i < Length; i++)
        {
            Assert.Equal(i + 1, output[i]);
        }
    }

    public static void ShortCircuitAnd(Action<Action> run, int enabled)
    {
        var input = CreateSignedSequence();
        var output = new int[Length];
        var calls = new int[Length];

        run(() => DeviceFunctionBarrierEdgeCaseKernels.LaunchShortCircuitAndKernel(input, output, calls, enabled,
            blockDimX: Length));

        for (int i = 0; i < Length; i++)
        {
            Assert.Equal(enabled != 0 ? 1 : 0, calls[i]);
            Assert.Equal(enabled != 0 && input[i ^ 1] > 0 ? 1 : 0, output[i]);
        }
    }

    public static void ShortCircuitOr(Action<Action> run, int skip)
    {
        var input = CreateSignedSequence();
        var output = new int[Length];
        var calls = new int[Length];

        run(() => DeviceFunctionBarrierEdgeCaseKernels.LaunchShortCircuitOrKernel(input, output, calls, skip,
            blockDimX: Length));

        for (int i = 0; i < Length; i++)
        {
            Assert.Equal(skip != 0 ? 0 : 1, calls[i]);
            Assert.Equal(skip != 0 || input[i ^ 1] > 0 ? 1 : 0, output[i]);
        }
    }

    public static void ConditionalOperator(Action<Action> run, int useShuffle)
    {
        var input = CreateSignedSequence();
        var output = new int[Length];
        var calls = new int[Length];

        run(() => DeviceFunctionBarrierEdgeCaseKernels.LaunchConditionalOperatorKernel(input, output, calls, useShuffle,
            blockDimX: Length));

        for (int i = 0; i < Length; i++)
        {
            Assert.Equal(useShuffle != 0 ? 1 : 0, calls[i]);
            Assert.Equal(useShuffle != 0 ? input[i ^ 1] : input[i] + 1000, output[i]);
        }
    }

    public static void WhileCondition(Action<Action> run, int limit)
    {
        var output = new int[Length];

        run(() => DeviceFunctionBarrierEdgeCaseKernels.LaunchWhileConditionKernel(output, limit, blockDimX: Length));

        Assert.All(output, count => Assert.Equal(Math.Max(limit, 0), count));
    }

    public static void ForIncrementor(Action<Action> run, int limit)
    {
        var output = new int[Length];

        run(() => DeviceFunctionBarrierEdgeCaseKernels.LaunchForIncrementorKernel(output, limit, blockDimX: Length));

        Assert.All(output, steps => Assert.Equal(Math.Max(limit, 0), steps));
    }

    public static void DoWhileCondition(Action<Action> run, int limit)
    {
        var output = new int[Length];

        run(() => DeviceFunctionBarrierEdgeCaseKernels.LaunchDoWhileConditionKernel(output, limit, blockDimX: Length));

        Assert.All(output, count => Assert.Equal(Math.Max(limit, 1), count));
    }

    public static void ExpressionBodyShiftUp(Action<Action> run)
    {
        var input = CreateSequence();
        var output = new int[Length];

        run(() => DeviceFunctionBarrierEdgeCaseKernels.LaunchExpressionBodyShiftUpKernel(input, output, blockDimX: Length));

        for (int i = 0; i < Length; i++)
        {
            Assert.Equal(i % 32 == 0 ? input[i] : input[i - 1], output[i]);
        }
    }

    public static void ExpressionBodiedKernel(Action<Action> run)
    {
        var input = CreateSequence();
        var output = new int[Length];

        run(() => DeviceFunctionBarrierEdgeCaseKernels.LaunchExpressionBodiedKernel(input, output, blockDimX: Length));

        for (int i = 0; i < Length; i++)
        {
            Assert.Equal(input[i - i % 32], output[i]);
        }
    }

    public static void RefParameter(Action<Action> run)
    {
        var input = CreateSequence();
        var output = new int[Length];
        var published = new int[Length];

        run(() => DeviceFunctionBarrierEdgeCaseKernels.LaunchRefParameterKernel(input, output, published,
            blockDimX: Length));

        for (int i = 0; i < Length; i++)
        {
            Assert.Equal(input[i] + 1, published[i]);
            Assert.Equal(input[i] + 11, output[i]);
        }
    }

    public static void RefParameterChain(Action<Action> run)
    {
        var input = CreateSequence();
        var output = new int[Length];
        var published = new int[Length];

        run(() => DeviceFunctionBarrierEdgeCaseKernels.LaunchRefParameterChainKernel(input, output, published,
            blockDimX: Length));

        for (int i = 0; i < Length; i++)
        {
            Assert.Equal(input[i] + 12, published[i]);
            Assert.Equal(input[i] + 22, output[i]);
        }
    }

    public static void RefElementArgument(Action<Action> run)
    {
        var data = new int[Length * 2];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = i * 5;
        }

        var cursors = new int[Length];
        var published = new int[Length];

        run(() => DeviceFunctionBarrierEdgeCaseKernels.LaunchRefElementArgumentKernel(data, cursors, published,
            blockDimX: Length));

        for (int i = 0; i < Length; i++)
        {
            Assert.Equal(i * 2 + 1, cursors[i]);
            Assert.Equal(i * 2 * 5 + 1, published[i]);
            Assert.Equal(i * 2 * 5 + 11, data[i * 2]);
            Assert.Equal((i * 2 + 1) * 5, data[i * 2 + 1]);
        }
    }

    public static void OutParameterInLoop(Action<Action> run, int rounds)
    {
        var input = CreateSequence();
        var output = new int[Length];

        run(() => DeviceFunctionBarrierEdgeCaseKernels.LaunchOutParameterInLoopKernel(input, output, rounds,
            blockDimX: Length));

        var expected = (int[])input.Clone();
        for (int round = 0; round < rounds; round++)
        {
            var next = new int[Length];
            for (int i = 0; i < Length; i++)
            {
                next[i] = expected[i] + expected[i ^ 1];
            }

            expected = next;
        }

        Assert.Equal(expected, output);
    }

    private static int[] CreateSequence()
    {
        var values = new int[Length];
        for (int i = 0; i < Length; i++)
        {
            values[i] = i * 7 + 3;
        }

        return values;
    }

    // Mixed signs, so that a comparison with zero depends on which lane was read
    private static int[] CreateSignedSequence()
    {
        var values = new int[Length];
        for (int i = 0; i < Length; i++)
        {
            values[i] = i % 3 == 0 ? -(i + 1) : i + 1;
        }

        return values;
    }
}
