namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// The parameters that G# appends to the native signature of a kernel after the user's parameters, in this order: the
/// pointer to the result buffer of a kernel that returns a value, the scratch of a grid aggregate of
/// <c>AsParallel()</c> (CUDA), and the epoch of the launch (CUDA).
/// </summary>
/// <remarks>
/// <see cref="MethodSignatureTranspiler"/> writes them from this value, and the check of the CUDA parameter space
/// (GSH0064) counts them from it, so the two agree on what the kernel takes.
/// </remarks>
/// <param name="ResultBuffer">Whether the kernel takes the pointer to its result buffer.</param>
/// <param name="GridScratch">Whether the kernel takes the scratch of a grid aggregate.</param>
/// <param name="LaunchEpoch">Whether the kernel takes the epoch of the launch.</param>
internal readonly record struct KernelHiddenParameters(bool ResultBuffer, bool GridScratch, bool LaunchEpoch)
{
    private const int PointerBytes = 8;
    private const int EpochBytes = 4;

    /// <summary>The hidden parameters of <paramref name="kernel"/> in the given dialect.</summary>
    public static KernelHiddenParameters For(MethodDeclarationSyntax method, IMethodSymbol kernel,
        SemanticModel semanticModel, bool isCuda) =>
        new(
            !kernel.ReturnsVoid,
            isCuda && ParallelAggregatePlanner.GetGridScratchSlotBytes(method, semanticModel) > 0,
            isCuda
        );

    /// <summary>
    /// The size of a parameter list of <paramref name="userBytes"/> bytes once these parameters follow it, each aligned
    /// to its own size as C++ places them.
    /// </summary>
    public int AppendTo(int userBytes)
    {
        var offset = userBytes;
        if (ResultBuffer)
        {
            offset = TypeLayoutCalculator.AlignUp(offset, PointerBytes) + PointerBytes;
        }

        if (GridScratch)
        {
            offset = TypeLayoutCalculator.AlignUp(offset, PointerBytes) + PointerBytes;
        }

        if (LaunchEpoch)
        {
            offset = TypeLayoutCalculator.AlignUp(offset, EpochBytes) + EpochBytes;
        }

        return offset;
    }
}
