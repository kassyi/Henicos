namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// One run of a kernel of <see cref="PrivateFrameTypeKernels"/> on the input <c>[1, 2, 3, 4]</c> with one block of four
/// threads, and the values C# gives.
/// </summary>
/// <param name="Launch">Launches the kernel with the values and the result array.</param>
/// <param name="Expected">The values the result array must hold.</param>
internal sealed record PrivateFrameTypeCase(Action<int[], int[]> Launch, int[] Expected);

/// <summary>
/// The cases that <see cref="PrivateFrameTypeNativeTests"/> and <c>PrivateFrameTypeManagedTests</c> (CPU only) share.
/// </summary>
internal static class PrivateFrameTypeCases
{
    // 2v + 1
    public static readonly PrivateFrameTypeCase SameClass = new(
        static (data, result) => PrivateFrameTypeKernels.LaunchSameClassKernel(data, result, blockDimX: 4),
        [3, 5, 7, 9]);

    // (2v + 1) + 10v
    public static readonly PrivateFrameTypeCase SameClassChain = new(
        static (data, result) => PrivateFrameTypeKernels.LaunchSameClassChainKernel(data, result, blockDimX: 4),
        [13, 25, 37, 49]);

    // 3v - 1
    public static readonly PrivateFrameTypeCase NestedClass = new(
        static (data, result) => PrivateFrameTypeKernels.LaunchNestedClassKernel(data, result, blockDimX: 4),
        [2, 5, 8, 11]);

    // 3v + 100
    public static readonly PrivateFrameTypeCase GenericClass = new(
        static (data, result) => PrivateFrameTypeKernels.LaunchGenericClassKernel(data, result, blockDimX: 4),
        [103, 106, 109, 112]);
}
