#nullable enable
#if GSHARP_CPU
using System;
using Kassyi.GSharp.Runtime.Interop;
using Xunit;
using Kernels = Kassyi.GSharp.Tests.Shared.Marshaling.CpuPointerLaunchKernels;

namespace Kassyi.GSharp.Tests.Shared.Marshaling;

/// <summary>
/// Calls <c>Launch{Kernel}Ptr</c> of the CPU dialect through the unmanaged function pointer, so a wrong width of a
/// scalar, a wrong argument order or a wrong copy of a proxy shows in the values the native kernel reads. Each test
/// compares the pointer launcher with the array launcher of the same kernel and with the values written out.
/// </summary>
[Category(TestCategory.Runtime)]
[TestTarget(ExecutionTarget.CpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public sealed unsafe class CpuPointerLaunchTests
{
    private static GSharpArrayProxy Proxy<T>(T* data, int length) where T : unmanaged =>
        new() { Data = (IntPtr)data, Length = length };

    // An empty array on the CPU path: no address to read, but not the null array of an optional parameter
    private static GSharpArrayProxy EmptyProxy() =>
        new() { Data = (IntPtr)GSharpArrayProxy.EmptyArrayData, Length = 0 };

    [Theory]
    [InlineData(true, '\u0101', PointerLaunchLevel.Deep)]
    [InlineData(false, '\uFFFF', PointerLaunchLevel.Shallow)]
    [InlineData(true, '\0', PointerLaunchLevel.Deep)]
    [InlineData(false, 'A', PointerLaunchLevel.Shallow)]
    [Scenario(TestScenario.Boundary)]
    public void ScalarsKernelPtr_EveryScalarKind_MatchesTheArrayLauncherAndTheValuesWritten(
        bool flag, char letter, PointerLaunchLevel level)
    {
        const sbyte s8 = -5;
        const byte u8 = 250;
        const short s16 = -300;
        const ushort u16 = 60000;
        const int s32 = -123456789;
        const uint u32 = 4000000000u;
        const long s64 = long.MinValue;
        const ulong u64 = ulong.MaxValue;
        long[] expectedIntegers =
        [
            flag ? 1 : 0, letter, s8, u8, s16, u16, s32, u32, s64, -1L, (long)level,
        ];
        double[] expectedReals = [1.5, -2.25];

        long[] arrayIntegers = new long[11];
        double[] arrayReals = new double[2];
        Kernels.LaunchScalarsKernel(flag, letter, s8, u8, s16, u16, s32, u32, s64, u64, level, 1.5f, -2.25,
            arrayIntegers, arrayReals);

        long[] ptrIntegers = new long[11];
        double[] ptrReals = new double[2];
        fixed (long* integers = ptrIntegers)
        fixed (double* reals = ptrReals)
        {
            Kernels.LaunchScalarsKernelPtr(flag, letter, s8, u8, s16, u16, s32, u32, s64, u64, level, 1.5f, -2.25,
                Proxy(integers, ptrIntegers.Length), Proxy(reals, ptrReals.Length));
        }

        Assert.Equal(expectedIntegers, arrayIntegers);
        Assert.Equal(expectedReals, arrayReals);
        Assert.Equal(arrayIntegers, ptrIntegers);
        Assert.Equal(arrayReals, ptrReals);
    }

    [Theory]
    [InlineData('\u0101', '\u0102')]
    [InlineData('\uFFFF', '\0')]
    [Scenario(TestScenario.Boundary)]
    public void RefAndOutKernelPtr_ScalarsByAddress_WritesThroughTheAddresses(char letter, char expectedLetter)
    {
        int[] data = new int[4];
        int arrayCounter = 10;
        bool arrayFlag = true;
        char arrayLetter = letter;
        Kernels.LaunchRefAndOutKernel(ref arrayCounter, out long arrayBig, ref arrayFlag, ref arrayLetter, data);

        int counter = 10;
        long big = 0;
        bool flag = true;
        char ptrLetter = letter;
        fixed (int* pinned = data)
        {
            Kernels.LaunchRefAndOutKernelPtr((IntPtr)(&counter), (IntPtr)(&big), (IntPtr)(&flag),
                (IntPtr)(&ptrLetter), Proxy(pinned, data.Length));
        }

        Assert.Equal((14, long.MinValue + 3, false, expectedLetter), (arrayCounter, arrayBig, arrayFlag, arrayLetter));
        Assert.Equal((arrayCounter, arrayBig, arrayFlag, arrayLetter), (counter, big, flag, ptrLetter));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void StructsKernelPtr_StructsByValue_PassesEveryMember()
    {
        long[] expected = [21, long.MinValue + 5, 200, 1, -9876543210L, -300];

        long[] arrayOutput = new long[6];
        Kernels.LaunchStructsKernel(
            new PointerLaunchPair { First = 7, Second = long.MinValue + 5, Tag = 200 },
            new PointerLaunchShuffled { On = 1, Value = -9876543210L, Small = -300 }, 3, arrayOutput);

        long[] ptrOutput = new long[6];
        fixed (long* output = ptrOutput)
        {
            Kernels.LaunchStructsKernelPtr(
                new Kernels.AotContexts.PointerLaunchPair { First = 7, Second = long.MinValue + 5, Tag = 200 },
                new Kernels.AotContexts.PointerLaunchShuffled { On = 1, Value = -9876543210L, Small = -300 }, 3,
                Proxy(output, ptrOutput.Length));
        }

        Assert.Equal(expected, arrayOutput);
        Assert.Equal(arrayOutput, ptrOutput);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void HolderKernelPtr_StructHoldingAnArray_ReadsTheArrayThroughItsProxy()
    {
        int[] data = [1, 2, 3, 4];
        int[] expected = [10, 20, 30, 40, 0, 0];

        int[] arrayOutput = new int[6];
        Kernels.LaunchHolderKernel(new PointerLaunchHolder { Data = data, Scale = 10 }, arrayOutput, blockDimX: 6);

        int[] ptrOutput = new int[6];
        fixed (int* pinnedData = data)
        fixed (int* output = ptrOutput)
        {
            Kernels.LaunchHolderKernelPtr(
                new Kernels.AotContexts.PointerLaunchHolder { Data = Proxy(pinnedData, data.Length), Scale = 10 },
                Proxy(output, ptrOutput.Length), blockDimX: 6);
        }

        Assert.Equal(expected, arrayOutput);
        Assert.Equal(arrayOutput, ptrOutput);
    }

    [Theory]
    [InlineData(0, new[] { -1, -1, 0 })]
    [InlineData(1, new[] { 0, 0, 0 })]
    [InlineData(2, new[] { 2, 3, 42 })]
    [Scenario(TestScenario.Boundary)]
    public void OptionalKernelPtr_NullEmptyAndFilledArrays_StayApart(int variant, int[] expected)
    {
        int[]? optional = variant switch { 0 => null, 1 => [], _ => [42, 43] };
        int[] memoryBuffer = new int[3];
        Memory<int>? memory = null;
        if (variant == 1)
        {
            memory = default(Memory<int>);
        }
        else if (variant == 2)
        {
            memory = new Memory<int>(memoryBuffer);
        }

        int[] arrayOutput = new int[3];
        Kernels.LaunchOptionalKernel(optional, memory, arrayOutput);

        int[] ptrOutput = new int[3];
        fixed (int* optionalData = optional)
        fixed (int* memoryData = memoryBuffer)
        fixed (int* output = ptrOutput)
        {
            GSharpArrayProxy optionalProxy = optional is null ? default
                : optional.Length == 0 ? EmptyProxy() : Proxy(optionalData, optional.Length);
            GSharpArrayProxy? memoryProxy = !memory.HasValue ? null
                : memory.Value.Length == 0 ? EmptyProxy() : Proxy(memoryData, memory.Value.Length);
            Kernels.LaunchOptionalKernelPtr(optionalProxy, memoryProxy, Proxy(output, ptrOutput.Length));
        }

        Assert.Equal(expected, arrayOutput);
        Assert.Equal(arrayOutput, ptrOutput);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void AliasedKernelPtr_OneBufferForEveryArgument_ReadsAndWritesTheSameMemory()
    {
        int[] expected = [2, 4, 6, 8];

        int[] arrayBuffer = [1, 2, 3, 4];
        Kernels.LaunchAliasedKernel(arrayBuffer, arrayBuffer, arrayBuffer, blockDimX: 4);

        int[] ptrBuffer = [1, 2, 3, 4];
        fixed (int* buffer = ptrBuffer)
        {
            var proxy = Proxy(buffer, ptrBuffer.Length);
            Kernels.LaunchAliasedKernelPtr(proxy, proxy, proxy, blockDimX: 4);
        }

        Assert.Equal(expected, arrayBuffer);
        Assert.Equal(arrayBuffer, ptrBuffer);
    }
}
#endif
