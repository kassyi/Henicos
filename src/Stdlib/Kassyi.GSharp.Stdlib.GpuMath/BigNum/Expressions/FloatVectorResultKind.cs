namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>A floating-point result: the target holds all three components.</summary>
internal readonly struct FloatVectorResultKind
    : IVectorResultKind<GpuBigFloatArrayView, GpuBigFloatArrayView>, IVectorCompactKind<GpuBigFloatArrayView>
{
    public static bool Overlaps(GpuBigFloatArrayView target, GpuBigFloatArrayView input) =>
        DeviceRanges.Overlaps(target, input);

    public static bool Overlaps(GpuBigFloatArrayView target, GpuBigIntMemoryHandle scalar) =>
        DeviceRanges.Overlaps(target, scalar);

    public static GpuBigFloatArrayView Destination(
        VectorResultCache cache,
        GpuWorkspace workspace,
        int count,
        int wordsPerElement,
        GpuBigFloatArrayView target) =>
        VectorResultCache.HasTarget(target) ? target : cache.Float(workspace, count, wordsPerElement);

    public static GpuBigFloatArrayView Deliver(
        GpuWorkspace workspace,
        GpuBigFloatArrayView computed,
        GpuBigFloatArrayView target,
        DeviceElementCount written) =>
        VectorResultCache.Deliver(workspace, computed, target, written);

    public static int ComponentCount => 3;

    public static GpuBigFloatArrayView Selected(
        VectorResultCache cache,
        GpuWorkspace workspace,
        int count,
        int wordsPerElement) =>
        Destination(cache, workspace, count, wordsPerElement, default);

    public static int Length(GpuBigFloatArrayView view) => view.Length;

    public static GpuBigFloatArrayView Slice(GpuBigFloatArrayView view, int elementOffset, int elementCount) =>
        view.Slice(elementOffset, elementCount);

    public static (IntPtr Pointer, int WordsPerElement) Component(GpuBigFloatArrayView view, int index) =>
        index switch
        {
            0 => (view.Significands.DevicePointer, view.WordsPerElement),
            1 => (view.Exponents.DevicePointer, 1),
            _ => (view.Signs.DevicePointer, 1)
        };

    public static void LaunchScatter(
        GpuWorkspace workspace,
        GpuBigFloatArrayView output,
        GpuBigFloatArrayView input,
        IntPtr predicates,
        IntPtr scan,
        DeviceElementCount count,
        uint gridDimX,
        uint blockDimX) =>
        GpuStreamCompactionKernels.LaunchCompactFloatVectorPtr(
            output.Significands.ToProxy(),
            output.Exponents.ToProxy(),
            output.Signs.ToProxy(),
            input.Significands.ToProxy(),
            input.Exponents.ToProxy(),
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
