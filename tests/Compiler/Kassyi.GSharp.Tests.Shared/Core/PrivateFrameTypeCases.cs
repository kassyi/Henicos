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

    // 4v + 7, from a kernel of another class
    public static readonly PrivateFrameTypeCase OtherClass = new(
        static (data, result) => PrivateFrameTypeCallers.LaunchOtherClassKernel(data, result, blockDimX: 4),
        [11, 15, 19, 23]);

    // (4v + 7) * 2 + v, through a device function of a third class
    public static readonly PrivateFrameTypeCase OtherClassChain = new(
        static (data, result) => PrivateFrameTypeCallers.LaunchOtherClassChainKernel(data, result, blockDimX: 4),
        [23, 32, 41, 50]);

    // 11 * (the neighbour's v), from two frames of the same function, each meeting its own shuffle
    public static readonly PrivateFrameTypeCase OtherClassWarp = new(
        static (data, result) => PrivateFrameTypeCallers.LaunchOtherClassWarpKernel(data, result, blockDimX: 4),
        [22, 11, 44, 33]);

    // 6v + 1, a protected function of a base class from a kernel of a derived class
    public static readonly PrivateFrameTypeCase DerivedClass = new(
        static (data, result) => PrivateFrameTypeDerived.LaunchDerivedClassKernel(data, result, blockDimX: 4),
        [7, 13, 19, 25]);

    // 5v + 3, an unsafe function's struct frame from a kernel that is not unsafe
    public static readonly PrivateFrameTypeCase PointerFrame = new(
        static (data, result) => PrivateFrameTypeCallers.LaunchPointerFrameKernel(data, result, blockDimX: 4),
        [8, 13, 18, 23]);

    // 6v + 1, a private struct returned by one suspending function and passed to another
    public static readonly PrivateFrameTypeCase StructParameter = new(
        static (data, result) => PrivateFrameTypeKernels.LaunchStructParameterKernel(data, result, blockDimX: 4),
        [7, 13, 19, 25]);

    // 2v + v, a private struct that only a spilled temporary of the function holds
    public static readonly PrivateFrameTypeCase SpillSameClass = new(
        static (data, result) => PrivateFrameTypeKernels.LaunchSpillSameClassKernel(data, result, blockDimX: 4),
        [3, 6, 9, 12]);

    // The same function from a kernel of another class
    public static readonly PrivateFrameTypeCase SpillOtherClass = new(
        static (data, result) => PrivateFrameTypeCallers.LaunchSpillOtherClassKernel(data, result, blockDimX: 4),
        [3, 6, 9, 12]);
}
