using Kassyi.GSharp.Tests.Shared.Core.Qualification;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// One run of a kernel of <see cref="ManagedQualificationKernels"/> that names a device function and constants through
/// <c>using static</c> inside a loop macro or a StackLinq lambda: how to launch it on the input <c>[1, 2, 3, 4]</c>, and
/// the C# result it must give.
/// </summary>
/// <param name="Launch">Launches the kernel with the values and the result array.</param>
/// <param name="Expected">The values the result array must hold, written in plain C# without the macros.</param>
internal sealed record QualificationCase(Action<int[], int[]> Launch, Func<int[], int[]> Expected);

/// <summary>
/// The cases that <see cref="ManagedQualificationNativeTests"/> and <see cref="ManagedQualificationManagedTests"/> share,
/// one per kernel. Each loop or query form has a kernel and a device-function variant with the same body; the expected
/// values are those of C#: <c>Thrice(x) = 3x</c>, and the constants come from <see cref="QualifyConstants"/>.
/// </summary>
internal static class ManagedQualificationCases
{
    public static readonly QualificationCase GridStrideBlockBody = new(
        static (values, result) => ManagedQualificationKernels.LaunchGridStrideBlockBodyKernel(
            values, result, values.Length, blockDimX: 4),
        ExpectedGridStrideBlockBody);

    public static readonly QualificationCase GridStrideBlockBodyInDeviceFunction = new(
        static (values, result) => ManagedQualificationKernels.LaunchGridStrideBlockBodyDeviceFunctionKernel(
            values, result, values.Length, blockDimX: 4),
        ExpectedGridStrideBlockBody);

    public static readonly QualificationCase GridStrideExpressionBody = new(
        static (values, result) => ManagedQualificationKernels.LaunchGridStrideExpressionBodyKernel(
            values, result, values.Length, blockDimX: 4),
        ExpectedGridStrideExpressionBody);

    public static readonly QualificationCase GridStrideExpressionBodyInDeviceFunction = new(
        static (values, result) => ManagedQualificationKernels.LaunchGridStrideExpressionBodyDeviceFunctionKernel(
            values, result, values.Length, blockDimX: 4),
        ExpectedGridStrideExpressionBody);

    public static readonly QualificationCase SingleThread = new(
        static (values, result) => ManagedQualificationKernels.LaunchSingleThreadKernel(values, result, blockDimX: 4),
        ExpectedSingleThread);

    public static readonly QualificationCase SingleThreadInDeviceFunction = new(
        static (values, result) => ManagedQualificationKernels.LaunchSingleThreadDeviceFunctionKernel(
            values, result, blockDimX: 4),
        ExpectedSingleThread);

    public static readonly QualificationCase ForUnroll = new(
        static (values, result) => ManagedQualificationKernels.LaunchForUnrollKernel(values, result, blockDimX: 4),
        ExpectedForUnroll);

    public static readonly QualificationCase ForUnrollInDeviceFunction = new(
        static (values, result) => ManagedQualificationKernels.LaunchForUnrollDeviceFunctionKernel(
            values, result, blockDimX: 4),
        ExpectedForUnroll);

    public static readonly QualificationCase StackLinq = new(
        static (values, result) => ManagedQualificationKernels.LaunchStackLinqKernel(values, result, blockDimX: 4),
        ExpectedStackLinq);

    public static readonly QualificationCase StackLinqInDeviceFunction = new(
        static (values, result) => ManagedQualificationKernels.LaunchStackLinqDeviceFunctionKernel(
            values, result, blockDimX: 4),
        ExpectedStackLinq);

    // result[i] = Thrice(values[i]) + Offset + Thrice(Offset)
    private static int[] ExpectedGridStrideBlockBody(int[] values)
    {
        var expected = new int[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            expected[i] = 3 * values[i] + QualifyConstants.Offset + 3 * QualifyConstants.Offset;
        }

        return expected;
    }

    // result[i] = Thrice(values[i]) + Count + Thrice(Offset)
    private static int[] ExpectedGridStrideExpressionBody(int[] values)
    {
        var expected = new int[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            expected[i] = 3 * values[i] + QualifyConstants.Count + 3 * QualifyConstants.Offset;
        }

        return expected;
    }

    // result[0] = Thrice(values[0]) + Offset, by one thread; the others stay 0
    private static int[] ExpectedSingleThread(int[] values)
    {
        var expected = new int[values.Length];
        expected[0] = 3 * values[0] + QualifyConstants.Offset;
        return expected;
    }

    // Thrice(Offset) + the sum over k < Count of Thrice(values[tid] + k) + Offset
    private static int[] ExpectedForUnroll(int[] values)
    {
        var expected = new int[values.Length];
        for (var tid = 0; tid < values.Length; tid++)
        {
            var sum = 3 * QualifyConstants.Offset;
            for (var k = 0; k < QualifyConstants.Count; k++)
            {
                sum += 3 * (values[tid] + k) + QualifyConstants.Offset;
            }

            expected[tid] = sum;
        }

        return expected;
    }

    // The sum over the values with Thrice(x) > Offset of x + Count, plus tid and Thrice(Count)
    private static int[] ExpectedStackLinq(int[] values)
    {
        var total = 0;
        foreach (var x in values)
        {
            if (3 * x > QualifyConstants.Offset)
            {
                total += x + QualifyConstants.Count;
            }
        }

        var expected = new int[values.Length];
        for (var tid = 0; tid < values.Length; tid++)
        {
            expected[tid] = total + tid + 3 * QualifyConstants.Count;
        }

        return expected;
    }
}
