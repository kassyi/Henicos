namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// How the marshaling methods reach the elements of a struct field whose array-like type is not an array
/// (<c>Memory&lt;T&gt;</c>, <c>ReadOnlyMemory&lt;T&gt;</c>, <c>ArraySegment&lt;T&gt;</c>,
/// <c>ImmutableArray&lt;T&gt;</c>), for one dialect (<see cref="ArrayViewFieldEmitter"/>, #1267).
/// </summary>
/// <remarks>
/// The view covers a slice of its memory, so the marshaling works on its span (offset and length) rather than on an
/// array object, and copies back only what a writable view covers.
/// </remarks>
/// <param name="Container">The container of the field.</param>
/// <param name="ElementTypeName">The <c>global::</c>-qualified element type.</param>
/// <param name="ElementShadowPath">
/// The <c>AotContexts</c> path of the element's shadow when the dialect converts each element, otherwise
/// <see langword="null"/>.
/// </param>
/// <param name="ElementMarshalMethodName">The element's <c>Marshal_</c> when it is converted.</param>
/// <param name="ElementUnmarshalMethodName">The element's <c>Unmarshal_</c> when it is converted.</param>
/// <param name="ElementCopyArraysMethodName">
/// The element's <c>CopyArrays_</c> on the CPU when the element holds arrays, otherwise <see langword="null"/>.
/// </param>
internal sealed record ArrayFieldView(
    ArrayContainerKind Container,
    string ElementTypeName,
    string? ElementShadowPath,
    string? ElementMarshalMethodName,
    string? ElementUnmarshalMethodName,
    string? ElementCopyArraysMethodName)
{
    /// <summary>
    /// Whether the kernel cannot write through the view: <c>ReadOnlyMemory</c> or <c>ImmutableArray</c>.
    /// </summary>
    public bool IsReadOnly => Container is ArrayContainerKind.ReadOnlyMemory or ArrayContainerKind.ImmutableArray;

    /// <summary>Whether each element goes through its shadow struct.</summary>
    public bool IsConverted => ElementShadowPath is not null;

    /// <summary>The span type of the view.</summary>
    public string SpanTypeName =>
        $"{(IsReadOnly ? HostTypeNames.ReadOnlySpan : HostTypeNames.Span)}<{ElementTypeName}>";

    /// <summary>The span of the view <paramref name="host"/>.</summary>
    public string SpanExpression(string host) => Container switch
    {
        ArrayContainerKind.ArraySegment => $"global::System.MemoryExtensions.AsSpan({host})",
        ArrayContainerKind.ImmutableArray => $"{host}.AsSpan()",
        _ => $"{host}.Span"
    };

    /// <summary>The view <paramref name="host"/> as a <c>Memory</c> or <c>ReadOnlyMemory</c>.</summary>
    public string MemoryExpression(string host) => Container switch
    {
        ArrayContainerKind.ArraySegment => $"global::System.MemoryExtensions.AsMemory({host})",
        ArrayContainerKind.ImmutableArray => $"{host}.AsMemory()",
        _ => host
    };

    /// <summary>
    /// Plans the view of <paramref name="arrayLike"/> under <paramref name="dialect"/>: CUDA converts every element
    /// with a shadow struct, the CPU only an element whose layouts differ, as for an array field.
    /// </summary>
    /// <exception cref="InvalidOperationException"><paramref name="arrayLike"/> is not an array-like type.</exception>
    public static ArrayFieldView Create(ITypeSymbol arrayLike, ITranspilerDialect dialect)
    {
        if (!arrayLike.TryGetArrayLikeElementType(out var element))
        {
            throw new InvalidOperationException(
                $"'{TypeLayoutCalculator.FormatTypeName(arrayLike)}' is not an array-like type.");
        }

        var converted = dialect.IsCuda
            ? HostShadowStructs.UsesShadow(element, dialect)
            : HostShadowStructs.IsConverted(element, dialect);
        return new ArrayFieldView(
            KernelParameterShape.GetContainer(arrayLike),
            element.GetFullyQualifiedCsharpName(),
            converted ? AotHostNames.GetAotStructPath(element) : null,
            converted ? AotHostNames.GetMarshalMethodName(element) : null,
            converted ? AotHostNames.GetUnmarshalMethodName(element) : null,
            !dialect.IsCuda && HostShadowStructs.HoldsArrayFields(element)
                ? AotHostNames.GetCopyArraysMethodName(element)
                : null);
    }
}
