using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Launches and checks the kernels of <see cref="SwitchSectionLocalKernels"/>. The native and managed test classes share
/// these scenarios and differ only in how a launch runs.
/// </summary>
internal static class SwitchSectionLocalScenarios
{
    public const int WarpSize = 32;

    public static void InitializedInFirstSection(Action<Action> run, int flag)
    {
        var output = new int[1];

        run(() => SwitchSectionLocalKernels.LaunchInitializedInFirstSectionKernel(output, flag, blockDimX: 1));

        Assert.Equal(flag == 0 ? 1 : 6, output[0]);
    }

    public static void SharedStructAndVar(Action<Action> run)
    {
        var data = CreateSequence();

        run(() => SwitchSectionLocalKernels.LaunchSharedStructAndVarKernel(data, blockDimX: WarpSize));

        for (int i = 0; i < WarpSize; i++)
        {
            int expected = (i % 3) switch
            {
                // pair = (i, 2), scaled = 10i, shared = 10i + 7, later = shared + 2
                0 => 10 * i + 9,
                // pair = (1, i), scaled = 100i, shared = scaled + 3
                1 => 100 * i + 3,
                _ => i + 1000
            };
            Assert.Equal(expected, data[i]);
        }
    }

    public static void GotoCaseAndNestedSwitch(Action<Action> run)
    {
        var data = CreateSequence();

        run(() => SwitchSectionLocalKernels.LaunchGotoCaseAndNestedSwitchKernel(data, blockDimX: WarpSize));

        for (int i = 0; i < WarpSize; i++)
        {
            // Even: case 0 writes 2i, case 1 reads it back and adds 3. Odd: i plus 4 * 10
            Assert.Equal(i % 2 == 0 ? 2 * i + 3 : i + 40, data[i]);
        }
    }

    public static void SectionConstant(Action<Action> run, int flag)
    {
        var output = new int[1];

        run(() => SwitchSectionLocalKernels.LaunchSectionConstantKernel(output, flag, blockDimX: 1));

        Assert.Equal(flag == 0 ? 41 : 42, output[0]);
    }

    public static void ByteConstant(Action<Action> run, int flag)
    {
        var output = new int[1];

        run(() => SwitchSectionLocalKernels.LaunchByteConstantKernel(output, flag, blockDimX: 1));

        // byte 255 + 1 wraps to 0; the default section adds 255 to it
        Assert.Equal(flag == 0 ? 0 : 255, output[0]);
    }

    public static void ConstantInArgument(Action<Action> run, int flag)
    {
        var output = new int[1];

        run(() => SwitchSectionLocalKernels.LaunchConstantInArgumentKernel(output, flag, blockDimX: 1));

        Assert.Equal(flag == 0 ? 40 : 41, output[0]);
    }

    public static void ArrayFieldStruct(Action<Action> run, int flag)
    {
        int[] data = [10];
        int[] other = [20];

        run(() => SwitchSectionLocalKernels.LaunchArrayFieldStructKernel(data, other, flag, blockDimX: 1));

        // case 0 writes data[0] + 2 to other; the default section writes other[0] + 5 to data
        Assert.Equal(flag == 0 ? [10, 12] : new[] { 25, 20 }, new[] { data[0], other[0] });
    }

    public static void GotoPastArrayFieldStruct(Action<Action> run, int flag)
    {
        int[] data = [10, 0];

        run(() => SwitchSectionLocalKernels.LaunchGotoPastArrayFieldStructKernel(data, flag, blockDimX: 1));

        Assert.Equal(flag == 0 ? [10, 7] : new[] { 13, 7 }, data);
    }

    public static void GotoPastArrayAlias(Action<Action> run, int flag)
    {
        int[] data = [10, 0];

        run(() => SwitchSectionLocalKernels.LaunchGotoPastArrayAliasKernel(data, flag, blockDimX: 1));

        // flag 1 writes 9 through the alias and adds 5 through the span
        Assert.Equal(flag == 0 ? [10, 7] : new[] { 14, 7 }, data);
    }

    public static void GotoInSharedSection(Action<Action> run, int flag)
    {
        int[] data = [10, 0];

        run(() => SwitchSectionLocalKernels.LaunchGotoInSharedSectionKernel(data, flag, blockDimX: 1));

        Assert.Equal(flag == 0 ? 11 : 12, data[1]);
    }

    public static void ShuffleBeforeDeclaration(Action<Action> run, int flag)
    {
        var output = new int[WarpSize];

        run(() => SwitchSectionLocalKernels.LaunchShuffleBeforeDeclarationKernel(output, flag, blockDimX: WarpSize));

        for (int i = 0; i < WarpSize; i++)
        {
            // ShuffleXorSync(.., 1) reads the neighbour lane i ^ 1
            Assert.Equal(flag == 0 ? (i ^ 1) + 1 : i + 2, output[i]);
        }
    }

    private static int[] CreateSequence()
    {
        var data = new int[WarpSize];
        for (int i = 0; i < WarpSize; i++)
        {
            data[i] = i;
        }

        return data;
    }
}
