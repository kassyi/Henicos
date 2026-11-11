namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// A <see cref="GpuBigUIntArrayView"/> as plain data: a view is a <see langword="ref struct"/> and cannot wait on the
/// stacks of <see cref="VectorWalk"/>. Converting back gives the same view, the default one included.
/// </summary>
internal readonly struct StoredView
{
    private readonly GpuWorkspace? _workspace;
    private readonly GpuBigIntMemoryHandle _handle;
    private readonly int _length;
    private readonly int _wordsPerElement;

    private StoredView(GpuWorkspace workspace, GpuBigIntMemoryHandle handle, int length, int wordsPerElement)
    {
        _workspace = workspace;
        _handle = handle;
        _length = length;
        _wordsPerElement = wordsPerElement;
    }

    /// <summary>Gets the view again.</summary>
    public GpuBigUIntArrayView View =>
        _workspace is null ? default : new GpuBigUIntArrayView(_workspace, _handle, _length, _wordsPerElement);

    /// <summary>Stores a view; only the default view has no workspace.</summary>
    public static StoredView Of(GpuBigUIntArrayView view) =>
        view.Workspace is null
            ? default
            : new StoredView(view.Workspace, view.Handle, view.Length, view.WordsPerElement);
}
