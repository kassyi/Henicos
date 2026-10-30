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
/// How a user struct parameter, or the user struct element of an array-like parameter, crosses to one dialect's device:
/// as it is (<see cref="TransferClass.Identical"/>) or converted element by element through the native shadow struct.
/// </summary>
/// <param name="IsIdenticalStruct">Whether the parameter is a user struct whose layout is <see cref="TransferClass.Identical"/>.</param>
/// <param name="ConvertedElementAotStructPath">
/// The <c>AotContexts</c> path of the element of an array-like parameter whose element is a user struct that is not
/// <see cref="TransferClass.Identical"/>, so that the launcher converts it element by element through a shadow array;
/// otherwise <see langword="null"/>.
/// </param>
/// <param name="ConvertedElementMarshalMethodName"><c>AotContexts.Marshal_{key}</c> of that element, otherwise <see langword="null"/>.</param>
/// <param name="ConvertedElementUnmarshalMethodName"><c>AotContexts.Unmarshal_{key}</c> of that element, otherwise <see langword="null"/>.</param>
/// <param name="ConvertedElementHasArrayFields">
/// Whether the converted element has array fields (<see cref="TypeLayoutCalculator.TryFindArrayField"/>), which
/// <c>Marshal_</c> handles even for an <c>[Out]</c> array.
/// </param>
/// <param name="ConvertedElementCopyArraysMethodName">
/// <c>AotContexts.CopyArrays_{key}</c> of that element when it has array fields, on the CPU only (the CUDA text has no
/// such method), otherwise <see langword="null"/>.
/// </param>
internal readonly record struct ParameterTransfer(
    bool IsIdenticalStruct,
    string? ConvertedElementAotStructPath,
    string? ConvertedElementMarshalMethodName,
    string? ConvertedElementUnmarshalMethodName,
    bool ConvertedElementHasArrayFields,
    string? ConvertedElementCopyArraysMethodName)
{
    /// <summary>
    /// The transfer of <paramref name="marshaledType"/> (a user struct or an array-like type) under
    /// <paramref name="dialect"/>.
    /// </summary>
    public static ParameterTransfer Create(ITypeSymbol marshaledType, ITranspilerDialect dialect)
    {
        if (TypeLayoutCalculator.IsUserStruct(marshaledType))
        {
            return new ParameterTransfer(
                marshaledType is INamedTypeSymbol named &&
                TypeLayoutCalculator.GetStructLayout(named, dialect).Transfer == TransferClass.Identical,
                null,
                null,
                null,
                false,
                null);
        }

        if (!marshaledType.TryGetArrayLikeElementType(out var element) ||
            !KernelParameterShape.IsConverted(element, dialect))
        {
            return default;
        }

        var hasArrayFields = element is INamedTypeSymbol namedElement &&
            TypeLayoutCalculator.TryFindArrayField(namedElement, out _);
        return new ParameterTransfer(
            false,
            AotHostNames.GetAotStructPath(element),
            AotHostNames.GetQualifiedMarshalMethodName(element),
            AotHostNames.GetQualifiedUnmarshalMethodName(element),
            hasArrayFields,
            hasArrayFields && !dialect.IsCuda ? AotHostNames.GetQualifiedCopyArraysMethodName(element) : null);
    }
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

    /// <summary>
    /// Whether the parameter is a user struct with array fields at any depth
    /// (<see cref="TypeLayoutCalculator.TryFindArrayField"/>), which its <c>Marshal_</c> copies.
    /// </summary>
    public bool HasArrayFields { get; init; }

    /// <summary>How the parameter crosses to the CUDA device.</summary>
    public ParameterTransfer Cuda { get; init; }

    /// <summary>How the parameter crosses to the CPU (native C++) launcher.</summary>
    public ParameterTransfer Cpu { get; init; }

    public bool IsArray => Kind == KernelParameterKind.Array;

    public bool IsCustomStruct => Kind == KernelParameterKind.CustomStruct;

    /// <summary>
    /// Whether the CUDA launcher converts the elements of this array-like parameter
    /// (<see cref="ParameterTransfer.ConvertedElementAotStructPath"/> of <see cref="Cuda"/>).
    /// </summary>
    public bool IsCudaConvertedArray => IsArray && Cuda.ConvertedElementAotStructPath is not null;

    /// <summary>
    /// Whether the parameter is a <c>T[]</c> passed by value, which another such argument or a struct field may hold as
    /// well: the launchers then give every holder the same array (one device region on CUDA, #721). Only a <c>T[]</c> has
    /// an identity to compare; a span or a segment over an array is a slice, whose overlaps are #857.
    /// </summary>
    public bool IsSharableArray => IsArray && Container == ArrayContainerKind.Array && !IsByRef;

    /// <summary>
    /// Whether the CUDA launcher's <c>Marshal_</c> calls for this parameter copy arrays: a user struct with
    /// <see cref="HasArrayFields"/>, or a converted array whose element has array fields.
    /// </summary>
    public bool MarshalsCudaArrays =>
        (IsCustomStruct && HasArrayFields) || (IsCudaConvertedArray && Cuda.ConvertedElementHasArrayFields);

    /// <summary>Whether the parameter is <c>ref</c> or <c>out</c>.</summary>
    public bool IsByRef => RefKind is RefKind.Ref or RefKind.Out;

    /// <summary>Whether the parameter is a by-value user struct with <see cref="TransferClass.Identical"/> on CUDA, passed directly via <c>&amp;host</c>.</summary>
    public bool IsDirectCudaValueStruct => IsCustomStruct && !IsByRef && Cuda.IsIdenticalStruct;

    /// <summary>
    /// Whether the parameter is a user struct passed by value (<c>in</c> and <c>ref readonly</c> included), which the CPU
    /// launcher passes as a pointer, to the host value when it is <see cref="TransferClass.Identical"/> on the CPU dialect
    /// and to its native shadow struct otherwise (<see cref="IsCpuConvertedStruct"/>); the native launcher copies it into
    /// a local, so that the runtime marshaller never converts it.
    /// </summary>
    public bool IsCpuValueStruct => IsCustomStruct && !IsByRef;

    /// <summary>
    /// Whether the parameter is a user struct (by value or by reference) that is not <see cref="TransferClass.Identical"/>
    /// on the CPU dialect, which the CPU launcher converts into its native shadow struct (<c>Marshal_</c>) and, for
    /// <c>ref</c> / <c>out</c>, back (<c>Unmarshal_</c>).
    /// </summary>
    public bool IsCpuConvertedStruct => IsCustomStruct && !Cpu.IsIdenticalStruct;

    /// <summary>
    /// Whether the CPU launcher converts the elements of this array-like parameter through a shadow array
    /// (<see cref="ParameterTransfer.ConvertedElementAotStructPath"/> of <see cref="Cpu"/>).
    /// </summary>
    public bool IsCpuConvertedArray => IsArray && Cpu.ConvertedElementAotStructPath is not null;

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
                ? FormatDefaultValue(parameter.Type, parameter.ExplicitDefaultValue)
                : null,
            CopyIn = copyIn,
            CopyOut = copyOut
        };

        // A user struct wins over by-reference: ref MyStruct goes through the native shadow struct as well
        if (TypeLayoutCalculator.IsUserStruct(marshaledType))
        {
            return shape with
            {
                Kind = KernelParameterKind.CustomStruct,
                AotStructPath = AotHostNames.GetAotStructPath(marshaledType),
                MarshalMethodName = AotHostNames.GetQualifiedMarshalMethodName(marshaledType),
                UnmarshalMethodName = AotHostNames.GetQualifiedUnmarshalMethodName(marshaledType),
                BufferElementTypeName = shape.IsByRef ? GetBufferElementTypeName(parameter.Type) : null,
                HasArrayFields = marshaledType is INamedTypeSymbol named &&
                    TypeLayoutCalculator.TryFindArrayField(named, out _),
                Cuda = ParameterTransfer.Create(marshaledType, HostDialects.Cuda),
                Cpu = ParameterTransfer.Create(marshaledType, HostDialects.Cpu)
            };
        }

        if (marshaledType.TryGetArrayLikeElementType(out var elementType))
        {
            return shape with
            {
                Kind = KernelParameterKind.Array,
                Container = GetContainer(marshaledType),
                ElementTypeName = elementType.GetFullyQualifiedCsharpName(),
                BufferElementTypeName = GetBufferElementTypeName(elementType),
                Cuda = ParameterTransfer.Create(marshaledType, HostDialects.Cuda),
                Cpu = ParameterTransfer.Create(marshaledType, HostDialects.Cpu)
            };
        }

        return shape.IsByRef ? shape with { BufferElementTypeName = GetBufferElementTypeName(parameter.Type) } : shape;
    }

    /// <summary>
    /// Whether an array of <paramref name="element"/> cannot be copied as one block on CUDA: a user struct whose host
    /// and device layouts differ (any class but <see cref="TransferClass.Identical"/>).
    /// </summary>
    public static bool IsConvertedOnCuda(ITypeSymbol element) => IsConverted(element, HostDialects.Cuda);

    /// <summary>
    /// Whether an array of <paramref name="element"/> cannot be copied as one block under <paramref name="dialect"/>: a
    /// user struct whose host and device layouts differ (any class but <see cref="TransferClass.Identical"/>).
    /// </summary>
    public static bool IsConverted(ITypeSymbol element, ITranspilerDialect dialect) =>
        element is INamedTypeSymbol named &&
        TypeLayoutCalculator.IsUserStruct(named) &&
        TypeLayoutCalculator.GetStructLayout(named, dialect).Transfer != TransferClass.Identical;

    // A user struct lives on the device in its native shadow layout, which the wrapper class declares as AotContexts.*
    private static string? GetBufferElementTypeName(ITypeSymbol element) =>
        !element.IsUnmanagedType ? null
        : TypeLayoutCalculator.IsUserStruct(element) ? AotHostNames.GetAotStructPath(element)
        : element.GetFullyQualifiedCsharpName();

    // The default as C# in the launcher's signature, whatever the build host's culture
    private static string FormatDefaultValue(ITypeSymbol type, object? value) => value switch
    {
        null => "default",
        string s => SymbolDisplay.FormatLiteral(s, true),
        _ when ConstexprLiteral.TryFormatConstant(type, value, out var literal) => literal.CSharp,
        // No device literal: the launcher still compiles, and GSH0003 reports the decimal in the kernel
        decimal number => number.ToString(CultureInfo.InvariantCulture) + "M",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "default"
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
