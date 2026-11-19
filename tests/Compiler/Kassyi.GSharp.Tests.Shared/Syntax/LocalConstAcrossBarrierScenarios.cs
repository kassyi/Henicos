using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Launches <see cref="LocalConstAcrossBarrierKernels"/> and checks the values that C# defines, for the native CPU, the
/// managed CPU and the CUDA paths.
/// </summary>
internal static class LocalConstAcrossBarrierScenarios
{
    public const int WarpSize = 32;

    private const int ThreadCount = 2;

    public static void ConstAcrossBarrier(Action<Action> run)
    {
        var result = new uint[WarpSize];

        run(() => LocalConstAcrossBarrierKernels.LaunchConstAcrossBarrierKernel(result, blockDimX: WarpSize));

        Assert.All(result, static value => Assert.Equal(2u, value));
    }

    public static void LocalConstInLoop(Action<Action> run)
    {
        var result = new int[WarpSize];

        run(() => LocalConstAcrossBarrierKernels.LaunchLocalConstInLoopKernel(result, blockDimX: WarpSize));

        for (int i = 0; i < WarpSize; i++)
        {
            Assert.Equal(Math.Min(i + 2, 31), result[i]);
        }
    }

    public static void ConstConversionChangesOperator(Action<Action> run)
    {
        var result = new long[ThreadCount];

        run(() => LocalConstAcrossBarrierKernels.LaunchConstConversionChangesOperatorKernel(
            result,
            blockDimX: ThreadCount));

        Assert.All(result, static value => Assert.Equal(4294967295L, value));
    }

    public static void TypedLocalConstsAcrossBarrier(Action<Action> run)
    {
        var intResults = new int[WarpSize * 8];
        var uintResults = new uint[WarpSize];
        var ulongResults = new ulong[WarpSize];

        run(() => LocalConstAcrossBarrierKernels.LaunchTypedLocalConstsAcrossBarrierKernel(
            intResults,
            uintResults,
            ulongResults,
            blockDimX: WarpSize));

        for (int i = 0; i < WarpSize; i++)
        {
            int baseIdx = i * 8;
            Assert.Equal(short.MinValue, intResults[baseIdx + 0]);
            Assert.Equal(4, intResults[baseIdx + 1]);
            Assert.Equal('Z', intResults[baseIdx + 2]);
            Assert.Equal(1, intResults[baseIdx + 3]);
            Assert.Equal((int)LocalConstMode.Beta, intResults[baseIdx + 4]);
            Assert.Equal(255, intResults[baseIdx + 5]);
            Assert.Equal(1, intResults[baseIdx + 6]);
            Assert.Equal(131, intResults[baseIdx + 7]);

            Assert.Equal(49u, uintResults[i]);
            Assert.Equal((1UL << 40) | (uint)i, ulongResults[i]);
        }
    }

    public static void ZeroFloatConstDivisorsAcrossBarrier(Action<Action> run)
    {
        var doubles = new double[ThreadCount * 3];
        var floats = new float[ThreadCount * 2];

        run(() => LocalConstAcrossBarrierKernels.LaunchZeroFloatConstDivisorsAcrossBarrierKernel(
            doubles,
            floats,
            blockDimX: ThreadCount));

        for (int i = 0; i < ThreadCount; i++)
        {
            Assert.Equal(double.NegativeInfinity, doubles[i * 3 + 0]);
            Assert.Equal(double.PositiveInfinity, doubles[i * 3 + 1]);
            Assert.True(double.IsNaN(doubles[i * 3 + 2]));
            Assert.Equal(float.NegativeInfinity, floats[i * 2 + 0]);
            Assert.Equal(BitConverter.SingleToInt32Bits(-0f), BitConverter.SingleToInt32Bits(floats[i * 2 + 1]));
        }
    }

    public static void SpecialFloatConstsAcrossBarrier(Action<Action> run)
    {
        var result = new double[ThreadCount * 4];

        run(() => LocalConstAcrossBarrierKernels.LaunchSpecialFloatConstsAcrossBarrierKernel(
            result,
            blockDimX: ThreadCount));

        for (int i = 0; i < ThreadCount; i++)
        {
            Assert.True(double.IsNaN(result[i * 4 + 0]));
            Assert.Equal(double.PositiveInfinity, result[i * 4 + 1]);
            Assert.Equal(BitConverter.DoubleToInt64Bits(1e-45f), BitConverter.DoubleToInt64Bits(result[i * 4 + 2]));
            Assert.Equal(BitConverter.DoubleToInt64Bits(-0.0), BitConverter.DoubleToInt64Bits(result[i * 4 + 3]));
        }
    }

    public static void SiblingScopeConsts(Action<Action> run)
    {
        var result = new double[ThreadCount];

        run(() => LocalConstAcrossBarrierKernels.LaunchSiblingScopeConstsKernel(result, blockDimX: ThreadCount));

        for (int i = 0; i < ThreadCount; i++)
        {
            Assert.Equal(3.5 + i, result[i]);
        }
    }

    public static void SwitchScopedBlockConstsAcrossSections(Action<Action> run, int flag)
    {
        var result = new int[WarpSize];
        Array.Fill(result, -1);

        run(() => LocalConstAcrossBarrierKernels.LaunchSwitchScopedBlockConstsAcrossSectionsKernel(
            result,
            flag,
            blockDimX: WarpSize));

        for (int i = 0; i < WarpSize; i++)
        {
            int expected = flag switch
            {
                0 => 10 + i,
                1 => 20 + i,
                _ => -1
            };
            Assert.Equal(expected, result[i]);
        }
    }

    public static void SwitchSameSectionTwoBlocksConsts(Action<Action> run, int flag)
    {
        var result = new int[WarpSize];

        run(() => LocalConstAcrossBarrierKernels.LaunchSwitchSameSectionTwoBlocksConstsKernel(
            result,
            flag,
            blockDimX: WarpSize));

        for (int i = 0; i < WarpSize; i++)
        {
            int expected = flag == 0 ? 210 + i : -1;
            Assert.Equal(expected, result[i]);
        }
    }

    public static void ConstInitializerAfterBarrier(Action<Action> run)
    {
        var result = new uint[ThreadCount];

        run(() => LocalConstAcrossBarrierKernels.LaunchConstInitializerAfterBarrierKernel(
            result,
            blockDimX: ThreadCount));

        Assert.All(result, static value => Assert.Equal(4u, value));
    }

    public static void MovedLocalFunctionConst(Action<Action> run)
    {
        var result = new uint[ThreadCount];

        run(() => LocalConstAcrossBarrierKernels.LaunchMovedLocalFunctionConstKernel(result, blockDimX: ThreadCount));

        Assert.All(result, static value => Assert.Equal(4u, value));
    }

    public static void NameofConstAcrossBarrier(Action<Action> run)
    {
        var result = new int[ThreadCount];

        run(() => LocalConstAcrossBarrierKernels.LaunchNameofConstAcrossBarrierKernel(result, blockDimX: ThreadCount));

        Assert.All(result, static value => Assert.Equal(25, value));
    }

    public static void EnumConstAcrossBarrier(Action<Action> run)
    {
        var result = new int[ThreadCount];

        run(() => LocalConstAcrossBarrierKernels.LaunchEnumConstAcrossBarrierKernel(result, blockDimX: ThreadCount));

        Assert.All(result, static value => Assert.Equal(400, value));
    }

    public static void MultiDeclaratorConst(Action<Action> run)
    {
        var result = new uint[ThreadCount];

        run(() => LocalConstAcrossBarrierKernels.LaunchMultiDeclaratorConstKernel(result, blockDimX: ThreadCount));

        Assert.All(result, static value => Assert.Equal(3u, value));
    }

    public static void LabeledConst(Action<Action> run)
    {
        var result = new uint[ThreadCount];

        run(() => LocalConstAcrossBarrierKernels.LaunchLabeledConstKernel(result, blockDimX: ThreadCount));

        Assert.All(result, static value => Assert.Equal(2u, value));
    }

    public static void ConstInsideLocalFunction(Action<Action> run)
    {
        var result = new int[ThreadCount];

        run(() => LocalConstAcrossBarrierKernels.LaunchConstInsideLocalFunctionKernel(result, blockDimX: ThreadCount));

        for (int i = 0; i < ThreadCount; i++)
        {
            Assert.Equal(8 + i, result[i]);
        }
    }

    public static void DeviceFunctionLocalConst(Action<Action> run)
    {
        var result = new uint[WarpSize];

        run(() => LocalConstAcrossBarrierKernels.LaunchDeviceFunctionLocalConstKernel(result, blockDimX: WarpSize));

        for (int i = 0; i < WarpSize; i++)
        {
            Assert.Equal(5u + (uint)i, result[i]);
        }
    }
}
