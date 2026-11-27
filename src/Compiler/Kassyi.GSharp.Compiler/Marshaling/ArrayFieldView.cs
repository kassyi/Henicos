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
/// <param name="ElementCopy">
/// The element's CPU <c>CopyArrays_</c> step when the element holds arrays or views, otherwise <see langword="null"/>.
/// </param>
internal sealed record ArrayFieldView(
    ArrayContainerKind Container,
    string ElementTypeName,
    string? ElementShadowPath,
    string? ElementMarshalMethodName,
    string? ElementUnmarshalMethodName,
    CopyArraysStep? ElementCopy)
{
    /// <summary>
    /// Whether the kernel cannot write through the view: <c>ReadOnlyMemory</c> or <c>ImmutableArray</c>.
    /// </summary>
    public bool IsReadOnly => Container is ArrayContainerKind.ReadOnlyMemory or ArrayContainerKind.ImmutableArray;

    /// <summary>
    /// Whether the container requires a CLR array on the CPU (<c>ArraySegment</c> or <c>ImmutableArray</c>).
    /// </summary>
    public bool RequiresTypedArray =>
        Container is ArrayContainerKind.ArraySegment or ArrayContainerKind.ImmutableArray;

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
            !dialect.IsCuda && StructArrayHelperEmitter.HoldsArrays(element)
                ? PlanCopyArraysStep(element)
                : null);
    }

    /// <summary>Plans the CPU <c>CopyArrays_</c> step for a value of <paramref name="type"/>, or <see langword="null"/> when none is needed.</summary>
    internal static CopyArraysStep? PlanCopyArraysStep(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol arrayType)
        {
            var elementStep = StructArrayHelperEmitter.HoldsArrays(arrayType.ElementType)
                ? PlanCopyArraysStep(arrayType.ElementType)
                : null;
            return new CopyArraysArrayStep(elementStep);
        }

        if (type.IsArrayLikeType() && type.TryGetArrayLikeElementType(out _))
        {
            var view = Create(type, HostDialects.Cpu);
            return new CopyArraysViewStep(view, view.ElementCopy);
        }

        if (HostShadowStructs.HoldsArrayFields(type) || type.IsNullableArrayLike())
        {
            return new CopyArraysMethodStep(AotHostNames.GetCopyArraysMethodName(type));
        }

        return null;
    }
}

/// <summary>A planned CPU <c>CopyArrays_</c> step for a value that holds arrays or views.</summary>
internal abstract record CopyArraysStep;

/// <summary>Clones a <c>T[]</c> through <c>HostArrayCopies.Copy</c> and walks its elements when needed.</summary>
/// <param name="Element">The step for each element when it holds arrays or views, otherwise <see langword="null"/>.</param>
internal sealed record CopyArraysArrayStep(CopyArraysStep? Element) : CopyArraysStep;

/// <summary>Copies a non-array view through <c>HostArrayCopies.CopyView</c> and walks its elements when needed.</summary>
/// <param name="View">The view access plan.</param>
/// <param name="Element">The step for each element when it holds arrays or views, otherwise <see langword="null"/>.</param>
internal sealed record CopyArraysViewStep(ArrayFieldView View, CopyArraysStep? Element) : CopyArraysStep;

/// <summary>Calls the generated <c>CopyArrays_</c> helper of a user struct or <c>Nullable&lt;T&gt;</c>.</summary>
/// <param name="MethodName">The <c>CopyArrays_</c> method name.</param>
internal sealed record CopyArraysMethodStep(string MethodName) : CopyArraysStep;
