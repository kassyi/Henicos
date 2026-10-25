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
/// <remarks>
/// A value of the cached pipeline model: <see cref="Classify"/> reads the symbol once, in the generator's transform, and
/// keeps only names and flags, so that a cached kernel never refers to an earlier compilation.
/// </remarks>
internal readonly record struct KernelParameterShape
{
    private const string GpuBufferType = "global::Kassyi.GSharp.Runtime.GpuBuffer";
    private static readonly ITranspilerDialect s_cudaDialect = new GSharpCudaDialect();

    /// <summary>The parameter name as declared, which the launcher's locals (<c>d_</c>, <c>p_</c>, <c>proxy_</c>, ...) build on.</summary>
    public string Name { get; init; }

    /// <summary>The parameter name as a C# identifier, which every reference to the argument uses.</summary>
    public string Identifier { get; init; }

    public RefKind RefKind { get; init; }

    public KernelParameterKind Kind { get; init; }

    public ArrayContainerKind Container { get; init; }

    /// <summary>The fully qualified parameter type without a nullable annotation (<c>global::System.Single[]</c>).</summary>
    public string TypeName { get; init; }

    /// <summary>The <c>AotContexts</c> path of the native shadow struct of a user struct, otherwise <see langword="null"/>.</summary>
    public string? AotStructPath { get; init; }

    /// <summary><c>AotContexts.Marshal_{key}</c> of a user struct, otherwise <see langword="null"/>.</summary>
    public string? MarshalMethodName { get; init; }

    /// <summary><c>AotContexts.Unmarshal_{key}</c> of a user struct, otherwise <see langword="null"/>.</summary>
    public string? UnmarshalMethodName { get; init; }

    /// <summary>The fully qualified element type of an array-like parameter, otherwise <see langword="null"/>.</summary>
    public string? ElementTypeName { get; init; }

    /// <summary>
    /// The <c>T</c> of the <c>GpuBuffer&lt;T&gt;</c> that the typed pointer wrapper takes for a device pointer
    /// (array-like, <c>ref</c> or <c>out</c>) whose element is unmanaged, otherwise <see langword="null"/>.
    /// </summary>
    public string? BufferElementTypeName { get; init; }

    /// <summary>A nullable reference type such as <c>float[]?</c>.</summary>
    public bool IsNullableReference { get; init; }

    /// <summary>A nullable value type such as <c>Memory&lt;float&gt;?</c>.</summary>
    public bool IsNullableValue { get; init; }

    /// <summary>A <c>[GpuConstexpr]</c> parameter.</summary>
    public bool IsConstexpr { get; init; }

    /// <summary>Whether the parameter declares a default value.</summary>
    public bool HasExplicitDefault { get; init; }

    /// <summary>The C# literal of the default value, or <see langword="null"/> without one.</summary>
    public string? DefaultValueLiteral { get; init; }

    /// <summary>Whether the launcher copies an array argument to the device (see <see cref="ArrayCopyDirection"/>).</summary>
    public bool CopyIn { get; init; }

    /// <summary>Whether the launcher copies an array argument back to the host (see <see cref="ArrayCopyDirection"/>).</summary>
    public bool CopyOut { get; init; }

    /// <summary>Whether the parameter is a user struct whose layout is <see cref="TransferClass.Identical"/> on CUDA.</summary>
    public bool IsCudaIdenticalStruct { get; init; }

    public bool IsArray => Kind == KernelParameterKind.Array;

    public bool IsCustomStruct => Kind == KernelParameterKind.CustomStruct;

    /// <summary>Whether the parameter is <c>ref</c> or <c>out</c>.</summary>
    public bool IsByRef => RefKind is RefKind.Ref or RefKind.Out;

    /// <summary>Whether the parameter is a by-value user struct with <see cref="TransferClass.Identical"/> on CUDA, passed directly via <c>&amp;host</c>.</summary>
    public bool IsDirectCudaValueStruct => IsCustomStruct && !IsByRef && IsCudaIdenticalStruct;

    /// <summary>
    /// Whether the <c>IntPtr</c> launch wrapper takes the parameter as a raw device pointer: an array-like or
    /// <c>ref</c> / <c>out</c> parameter.
    /// </summary>
    public bool IsDevicePointer => IsArray || IsByRef;

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

    /// <summary>The parameter type as a launch wrapper declares it: <see cref="TypeName"/> with <c>?</c> for a nullable reference.</summary>
    public string DeclaredTypeName => IsNullableReference ? TypeName + "?" : TypeName;

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

    /// <summary>Appends <c>GpuBuffer&lt;T&gt;</c> when the parameter has a <see cref="BufferElementTypeName"/>.</summary>
    public bool TryAppendTypedBuffer(SourceWriter writer)
    {
        if (BufferElementTypeName is not { } element)
        {
            return false;
        }

        writer.Append($"{GpuBufferType}<{element}>");
        return true;
    }

    /// <summary>Classifies <paramref name="parameter"/>.</summary>
    public static KernelParameterShape Classify(IParameterSymbol parameter)
    {
        var marshaledType = parameter.Type.UnwrapNullableArrayLike() ?? parameter.Type;
        var (copyIn, copyOut) = ArrayCopyDirection.Get(parameter);
        var shape = new KernelParameterShape
        {
            Name = parameter.Name,
            Identifier = parameter.Name.ToCSharpIdentifier(),
            RefKind = parameter.RefKind,
            Kind = parameter.RefKind is RefKind.Ref or RefKind.Out
                ? KernelParameterKind.RefScalar
                : KernelParameterKind.Scalar,
            Container = ArrayContainerKind.None,
            TypeName = parameter.Type.GetFullyQualifiedCsharpName(),
            IsNullableReference =
                parameter.Type.IsReferenceType && parameter.NullableAnnotation == NullableAnnotation.Annotated,
            IsNullableValue = parameter.Type.IsNullableArrayLike(),
            IsConstexpr = parameter.IsConstexprParameter(),
            HasExplicitDefault = parameter.HasExplicitDefaultValue,
            DefaultValueLiteral = parameter.HasExplicitDefaultValue
                ? FormatDefaultValue(parameter.ExplicitDefaultValue)
                : null,
            CopyIn = copyIn,
            CopyOut = copyOut
        };

        // A user struct wins over by-reference: ref MyStruct goes through the native shadow struct as well
        if (TypeLayoutCalculator.IsUserStruct(marshaledType))
        {
            var isCudaIdentical = marshaledType is INamedTypeSymbol namedStruct &&
                                  TypeLayoutCalculator.GetStructLayout(namedStruct, s_cudaDialect).Transfer == TransferClass.Identical;
            return shape with
            {
                Kind = KernelParameterKind.CustomStruct,
                AotStructPath = AotHostNames.GetAotStructPath(marshaledType),
                MarshalMethodName = AotHostNames.GetQualifiedMarshalMethodName(marshaledType),
                UnmarshalMethodName = AotHostNames.GetQualifiedUnmarshalMethodName(marshaledType),
                BufferElementTypeName = shape.IsByRef ? GetBufferElementTypeName(parameter.Type) : null,
                IsCudaIdenticalStruct = isCudaIdentical
            };
        }

        if (marshaledType.TryGetArrayLikeElementType(out var elementType))
        {
            return shape with
            {
                Kind = KernelParameterKind.Array,
                Container = GetContainer(marshaledType),
                ElementTypeName = elementType.GetFullyQualifiedCsharpName(),
                BufferElementTypeName = GetBufferElementTypeName(elementType)
            };
        }

        return shape.IsByRef ? shape with { BufferElementTypeName = GetBufferElementTypeName(parameter.Type) } : shape;
    }

    // A user struct lives on the device in its native shadow layout, which the wrapper class declares as AotContexts.*
    private static string? GetBufferElementTypeName(ITypeSymbol element) =>
        !element.IsUnmanagedType ? null
        : TypeLayoutCalculator.IsUserStruct(element) ? AotHostNames.GetAotStructPath(element)
        : element.GetFullyQualifiedCsharpName();

    private static string? FormatDefaultValue(object? value) => value switch
    {
        null => "default",
        bool b => b ? "true" : "false",
        float f => f.ToString(CultureInfo.InvariantCulture) + "f",
        double d => d.ToString(CultureInfo.InvariantCulture) + "d",
        string s => SymbolDisplay.FormatLiteral(s, true),
        _ => value.ToString()
    };

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
