namespace Kassyi.GSharp.Compiler;

/// <summary>
/// Answers whether a member of <c>Kassyi.GSharp.Core</c> runs only on the device: on the host, outside a managed
/// launch, it throws <c>GpuOnlyException</c>, so host code that calls or reads it fails (GSH0018).
/// </summary>
/// <remarks>
/// <para>
/// A member is GPU-only when it carries <c>[CudaIntrinsic]</c> (every stub generated from the native API YAML, and
/// <c>Gpu.SyncThreads</c> / <c>Gpu.SyncGrid</c>), when its type carries <c>[CudaIntrinsicPattern]</c>
/// (<c>ThreadIndex</c>, <c>BlockIndex</c>, <c>BlockDimension</c>, <c>GridDimension</c>), when it is a <c>Gpu</c> macro
/// other than <c>Gpu.NotNull</c> and <c>Gpu.Hint.Unroll</c>, the indexer of <c>SharedMemory&lt;T&gt;</c> (its
/// constructors give an empty buffer on the host), or a property of <c>Dim3</c>.
/// </para>
/// <para>
/// The test that walks every public member of the Core sources keeps this list and the members whose bodies throw
/// <c>GpuOnlyException</c> the same.
/// </para>
/// </remarks>
internal static class GpuOnlyMembers
{
    /// <summary>
    /// Whether <paramref name="symbol"/> (a method, an accessor or a property; a reduced extension method or a
    /// constructed generic member is taken by its definition) throws <c>GpuOnlyException</c> on the host.
    /// </summary>
    public static bool IsGpuOnly([NotNullWhen(true)] ISymbol? symbol) => symbol switch
    {
        IMethodSymbol { AssociatedSymbol: IPropertySymbol property } => IsGpuOnlyProperty(property.OriginalDefinition),
        IMethodSymbol method => IsGpuOnlyMethod((method.ReducedFrom ?? method).OriginalDefinition),
        IPropertySymbol property => IsGpuOnlyProperty(property.OriginalDefinition),
        _ => false
    };

    /// <summary>
    /// The member as a diagnostic names it: <c>ThreadIndex.X</c>, <c>Gpu.SyncThreads</c>, <c>SharedMemory.this[]</c>.
    /// </summary>
    public static string GetDisplayName(ISymbol member)
    {
        var name = member is IPropertySymbol { IsIndexer: true } ? "this[]" : member.Name;
        return member.ContainingType is { } type ? type.Name + "." + name : name;
    }

    private static bool IsGpuOnlyMethod(IMethodSymbol method) =>
        method.HasAttribute(KnownAttributes.CudaIntrinsic) ||
        HasIntrinsicPatternType(method) ||
        GpuMacroClassifier.Classify(method).Kind
            is not (GpuMacroKind.None or GpuMacroKind.NotNull or GpuMacroKind.HintUnroll);

    private static bool IsGpuOnlyProperty(IPropertySymbol property) =>
        property.HasAttribute(KnownAttributes.CudaIntrinsic) ||
        HasIntrinsicPatternType(property) ||
        IsDim3(property.ContainingType) ||
        (property.IsIndexer && property.ContainingType.IsSharedMemoryType());

    private static bool HasIntrinsicPatternType(ISymbol member) =>
        member.ContainingType?.HasAttribute(KnownAttributes.CudaIntrinsicPattern) == true;

    private static bool IsDim3(ITypeSymbol? type) =>
        type is { Name: TypeConstants.Dim3Type } && type.ContainingNamespace.IsCoreNamespace();
}
