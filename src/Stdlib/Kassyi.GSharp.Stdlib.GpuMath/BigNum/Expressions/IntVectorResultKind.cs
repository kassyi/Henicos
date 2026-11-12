namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>An integer result: the target holds the values, the signs stay in the node's cache.</summary>
internal readonly struct IntVectorResultKind
    : IVectorResultKind<GpuBigIntArrayView, GpuBigUIntArrayView>, IVectorCompactKind<GpuBigIntArrayView>
{
    public static bool Overlaps(GpuBigUIntArrayView target, GpuBigIntArrayView input) =>
        DeviceRanges.Overlaps(target, input);

    public static bool Overlaps(GpuBigUIntArrayView target, GpuBigIntMemoryHandle scalar) =>
        DeviceRanges.Overlaps(target.Handle, scalar);

    public static GpuBigIntArrayView Destination(
        VectorResultCache cache,
        GpuWorkspace workspace,
        int count,
        int wordsPerElement,
        GpuBigUIntArrayView target) =>
        cache.Signed(workspace, count, wordsPerElement, target);

    public static GpuBigIntArrayView Deliver(
        GpuWorkspace workspace,
        GpuBigIntArrayView computed,
        GpuBigUIntArrayView target,
        DeviceElementCount written) =>
        VectorResultCache.Deliver(workspace, computed, target, written);

    public static int ComponentCount => 2;

    public static GpuBigIntArrayView Selected(
        VectorResultCache cache,
        GpuWorkspace workspace,
        int count,
        int wordsPerElement) =>
        Destination(cache, workspace, count, wordsPerElement, default);

    public static int Length(GpuBigIntArrayView view) => view.Length;

    public static GpuBigIntArrayView Slice(GpuBigIntArrayView view, int elementOffset, int elementCount) =>
        view.Slice(elementOffset, elementCount);

    public static (IntPtr Pointer, int WordsPerElement) Component(GpuBigIntArrayView view, int index) =>
        index == 0 ? (view.Values.DevicePointer, view.WordsPerElement) : (view.Signs.DevicePointer, 1);

    public static void LaunchScatter(
        GpuWorkspace workspace,
        GpuBigIntArrayView output,
        GpuBigIntArrayView input,
        IntPtr predicates,
        IntPtr scan,
        DeviceElementCount count,
        uint gridDimX,
        uint blockDimX) =>
        GpuStreamCompactionKernels.LaunchCompactSignedVectorPtr(
            output.Values.ToProxy(),
            output.Signs.ToProxy(),
            input.Values.ToProxy(),
            input.Signs.ToProxy(),
            new GSharpArrayProxy(predicates, input.Length),
            new GSharpArrayProxy(scan, input.Length),
            input.Length,
            input.WordsPerElement,
            count.ToScanContext(),
            gridDimX: gridDimX,
            blockDimX: blockDimX,
            hStream: workspace.Stream
        );
}
