namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>How a kernel parameter crosses from the host to the device.</summary>
internal enum KernelParameterKind
{
    /// <summary>Passed by value.</summary>
    Scalar,

    /// <summary>
    /// A <c>ref</c> / <c>out</c> parameter that is not a user struct: copied through a device allocation.
    /// </summary>
    RefScalar,

    /// <summary>
    /// An array-like parameter (see <see cref="ArrayContainerKind"/>): copied as a <c>GSharpArray</c>.
    /// </summary>
    Array,

    /// <summary>A user struct, by value or by reference, marshaled through its native shadow struct.</summary>
    CustomStruct
}

/// <summary>The C# container of an array-like parameter.</summary>
internal enum ArrayContainerKind
{
    None,
    Array,
    Span,
    ReadOnlySpan,
    Memory,
    ReadOnlyMemory,
    ArraySegment,
    ImmutableArray
}

/// <summary>
/// The host ABI classification of one kernel parameter, which every host emitter (CUDA launcher, CPU delegate, argument
/// checks, typed pointer wrappers) builds on.
/// </summary>
/// <remarks>Built per emission from the symbol; it is not part of the cached pipeline model.</remarks>
internal readonly struct KernelParameterShape
{
    private KernelParameterShape(IParameterSymbol parameter, ITypeSymbol marshaledType, KernelParameterKind kind,
        ArrayContainerKind container, ITypeSymbol? elementType)
    {
        Parameter = parameter;
        MarshaledType = marshaledType;
        Kind = kind;
        Container = container;
        ElementType = elementType;
    }

    public IParameterSymbol Parameter { get; }

    /// <summary>The parameter type with a nullable array-like value type (<c>Memory&lt;T&gt;?</c>) unwrapped.</summary>
    public ITypeSymbol MarshaledType { get; }

    public KernelParameterKind Kind { get; }

    public ArrayContainerKind Container { get; }

    /// <summary>The element type of an array-like parameter, otherwise <see langword="null"/>.</summary>
    public ITypeSymbol? ElementType { get; }

    public bool IsArray => Kind == KernelParameterKind.Array;

    public bool IsCustomStruct => Kind == KernelParameterKind.CustomStruct;

    /// <summary>Whether the parameter is <c>ref</c> or <c>out</c>.</summary>
    public bool IsByRef => Parameter.RefKind is RefKind.Ref or RefKind.Out;

    /// <summary>
    /// Whether the <c>IntPtr</c> launch wrapper takes the parameter as a raw device pointer: an array-like or
    /// <c>ref</c> / <c>out</c> parameter.
    /// </summary>
    public bool IsDevicePointer => IsArray || IsByRef;

    /// <summary>A nullable reference type such as <c>float[]?</c>.</summary>
    public bool IsNullableReference =>
        Parameter.Type.IsReferenceType && Parameter.NullableAnnotation == NullableAnnotation.Annotated;

    /// <summary>A nullable value type such as <c>Memory&lt;float&gt;?</c>.</summary>
    public bool IsNullableValue => Parameter.Type.IsNullableArrayLike();

    /// <summary>Whether the caller may pass <c>null</c>.</summary>
    public bool IsOptional => IsNullableReference || IsNullableValue;

    /// <summary>
    /// Whether the device never writes the data back: <c>ReadOnlySpan</c>, <c>ReadOnlyMemory</c> or
    /// <c>ImmutableArray</c>.
    /// </summary>
    public bool IsReadOnlyContainer =>
        Container is ArrayContainerKind.ReadOnlySpan
            or ArrayContainerKind.ReadOnlyMemory
            or ArrayContainerKind.ImmutableArray;

    /// <summary>The parameter name as a C# identifier.</summary>
    public string Identifier => Parameter.Name.ToCSharpIdentifier();

    /// <summary>
    /// The member that holds the element count: <c>Count</c> for <c>ArraySegment</c>, <c>Length</c> otherwise.
    /// </summary>
    public string LengthMember => Container == ArrayContainerKind.ArraySegment ? "Count" : "Length";

    /// <summary>The parameter value, with a nullable value type unwrapped by <c>GetValueOrDefault()</c>.</summary>
    public string ValueExpression => IsNullableValue ? $"{Identifier}.GetValueOrDefault()" : Identifier;

    /// <summary>The element count, 0 for a <c>null</c> array.</summary>
    public string LengthExpression =>
        IsNullableReference ? $"({Identifier}?.Length ?? 0)" : $"{ValueExpression}.{LengthMember}";

    /// <summary>An expression that a <c>fixed</c> statement can pin.</summary>
    public string SpanExpression => Container switch
    {
        ArrayContainerKind.Memory or ArrayContainerKind.ReadOnlyMemory => $"{ValueExpression}.Span",
        ArrayContainerKind.ArraySegment or ArrayContainerKind.ImmutableArray => $"{ValueExpression}.AsSpan()",
        _ => ValueExpression
    };

    /// <summary>Classifies <paramref name="parameter"/>.</summary>
    public static KernelParameterShape Classify(IParameterSymbol parameter)
    {
        var marshaledType = parameter.Type.UnwrapNullableArrayLike() ?? parameter.Type;

        // A user struct wins over by-reference: ref MyStruct goes through the native shadow struct as well
        if (CudaLayoutRules.IsCustomStruct(marshaledType))
        {
            return new KernelParameterShape(parameter, marshaledType, KernelParameterKind.CustomStruct,
                ArrayContainerKind.None, null);
        }

        if (marshaledType.TryGetArrayLikeElementType(out var elementType))
        {
            return new KernelParameterShape(parameter, marshaledType, KernelParameterKind.Array,
                GetContainer(marshaledType), elementType);
        }

        var kind = parameter.RefKind is RefKind.Ref or RefKind.Out
            ? KernelParameterKind.RefScalar
            : KernelParameterKind.Scalar;
        return new KernelParameterShape(parameter, marshaledType, kind, ArrayContainerKind.None, null);
    }

    // Only reached for an array-like type, which TryGetArrayLikeElementType has already checked by namespace
#pragma warning disable HNT0003 // The namespace check is in TryGetArrayLikeElementType, the caller's guard
    private static ArrayContainerKind GetContainer(ITypeSymbol arrayLike) => arrayLike switch
    {
        IArrayTypeSymbol => ArrayContainerKind.Array,
        { Name: TypeConstants.SpanType } => ArrayContainerKind.Span,
        { Name: TypeConstants.ReadOnlySpanType } => ArrayContainerKind.ReadOnlySpan,
        { Name: "Memory" } => ArrayContainerKind.Memory,
        { Name: "ReadOnlyMemory" } => ArrayContainerKind.ReadOnlyMemory,
        { Name: "ArraySegment" } => ArrayContainerKind.ArraySegment,
        { Name: "ImmutableArray" } => ArrayContainerKind.ImmutableArray,
        _ => throw new InvalidOperationException($"'{arrayLike.Name}' is not an array-like type.")
    };
#pragma warning restore HNT0003
}
