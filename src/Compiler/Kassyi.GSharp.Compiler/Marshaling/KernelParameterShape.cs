namespace Kassyi.GSharp.Compiler.Marshaling;

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
    private const string ArrayPinningType = HostTypeNames.ArrayPinning;

    /// <summary>The zero-based position of the parameter in the kernel's parameter list, which the launcher's locals (<c>__gsharp_d_0</c>, ...) build on.</summary>
    public int Ordinal { get; init; }

    /// <summary>The parameter name as a C# identifier, which every reference to the argument uses.</summary>
    public string Identifier { get; init; }

    /// <summary>
    /// The parameter name as metadata spells it (<c>class</c> for <c>@class</c>), which the argument checks report.
    /// </summary>
    public string Name { get; init; }

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

    /// <summary>
    /// The non-nullable array fields that a by-value struct argument reaches through struct fields, each as the chain of
    /// C# field names (<c>Operands.ResultBuffer</c>): the launchers reject a null in any of them, since the kernel reads
    /// them without a check (#1409). Empty for any other parameter.
    /// </summary>
    public EquatableArray<string> NonNullableArrayFields { get; init; }

    /// <summary>A nullable value type such as <c>Memory&lt;float&gt;?</c>.</summary>
    public bool IsNullableValue { get; init; }

    /// <summary>
    /// A <c>ref struct</c> type such as <c>Span&lt;T&gt;</c>, which a lambda cannot capture (the C# execution of the
    /// launcher passes a span through a pinned pointer).
    /// </summary>
    public bool IsRefLike { get; init; }

    /// <summary>A <c>[GpuConstexpr]</c> parameter.</summary>
    public bool IsConstexpr { get; init; }

    /// <summary>Whether the parameter declares a default value.</summary>
    public bool HasExplicitDefault { get; init; }

    /// <summary>The C# literal of the default value, or <see langword="null"/> without one.</summary>
    public string? DefaultValueLiteral { get; init; }

    /// <summary>Whether the launcher copies an array argument to the device (see <see cref="ArrayCopyDirection"/>).</summary>
    public bool IsCopiedIn { get; init; }

    /// <summary>Whether the launcher copies an array argument back to the host (see <see cref="ArrayCopyDirection"/>).</summary>
    public bool IsCopiedOut { get; init; }

    /// <summary>
    /// Whether the parameter is a user struct with array fields at any depth
    /// (<see cref="TransferClassifier.TryFindArrayField"/>), which its <c>Marshal_</c> copies.
    /// </summary>
    public bool HasArrayFields { get; init; }

    /// <summary>
    /// <c>AotContexts.HoldsArray_{key}</c> of a user struct with <see cref="HasArrayFields"/>, or of the element of an
    /// array-like parameter whose element the CPU launcher converts and has array fields, otherwise
    /// <see langword="null"/>: the CPU method that tells whether the argument (or any of its elements) holds a given
    /// array in a field.
    /// </summary>
    public string? CpuHoldsArrayMethodName { get; init; }

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

    /// <summary>
    /// The element count, 0 for a <c>null</c> array and for a default <c>ImmutableArray</c>, whose <c>Length</c>
    /// throws.
    /// </summary>
    public string LengthExpression => Container switch
    {
        ArrayContainerKind.Array => $"({Identifier}?.Length ?? 0)",
        ArrayContainerKind.ImmutableArray => $"{ValueExpression}.AsSpan().Length",
        _ => $"{ValueExpression}.{LengthMember}"
    };

    /// <summary>
    /// Whether the argument holds no array: a null array, or a span-like value over none (<c>default</c>, and a
    /// <see langword="null"/> nullable value). An empty array, or a span-like value over one, holds an array.
    /// </summary>
    public string NullTestExpression => $"{ArrayPinningType}.IsNull({SpanExpression})";

    /// <summary>
    /// The reference that a <c>fixed</c> statement pins as <c>&amp;&lt;reference&gt;</c>: null when the argument holds
    /// no array (<see cref="NullTestExpression"/>), the address of the array's data otherwise, also for an empty array.
    /// </summary>
    public string PinnableReferenceExpression => $"{ArrayPinningType}.GetPinnableReference({SpanExpression})";

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
        var kernelClass = parameter.ContainingSymbol?.ContainingType;
        var marshaledType = parameter.Type.UnwrapNullableArrayLike() ?? parameter.Type;
        var (copyIn, copyOut) = ArrayCopyDirection.Get(parameter);
        var shape = new KernelParameterShape
        {
            Ordinal = parameter.Ordinal,
            Identifier = parameter.Name.ToCSharpIdentifier(),
            Name = parameter.Name,
            RefKind = parameter.RefKind,
            Kind = parameter.RefKind is RefKind.Ref or RefKind.Out
                ? KernelParameterKind.RefScalar
                : KernelParameterKind.Scalar,
            Container = ArrayContainerKind.None,
            TypeName = parameter.Type.GetFullyQualifiedCsharpName(),
            IsNullableReference =
                parameter.Type.IsReferenceType && parameter.NullableAnnotation == NullableAnnotation.Annotated,
            IsNullableValue = parameter.Type.IsNullableArrayLike(),
            IsRefLike = parameter.Type.IsRefLikeType,
            IsConstexpr = parameter.IsConstexprParameter(),
            HasExplicitDefault = parameter.HasExplicitDefaultValue,
            DefaultValueLiteral = parameter.HasExplicitDefaultValue
                ? FormatDefaultValue(parameter.Type, parameter.ExplicitDefaultValue)
                : null,
            IsCopiedIn = copyIn,
            IsCopiedOut = copyOut
        };

        // A user struct wins over by-reference: ref MyStruct goes through the native shadow struct as well
        if (TypeLayoutCalculator.IsUserStruct(marshaledType))
        {
            var hasArrayFields = marshaledType is INamedTypeSymbol named &&
                TransferClassifier.TryFindArrayField(named, out _);
            return shape with
            {
                Kind = KernelParameterKind.CustomStruct,
                AotStructPath = AotHostNames.GetAotStructPath(marshaledType, kernelClass),
                MarshalMethodName = AotHostNames.GetQualifiedMarshalMethodName(marshaledType, kernelClass),
                UnmarshalMethodName = AotHostNames.GetQualifiedUnmarshalMethodName(marshaledType, kernelClass),
                BufferElementTypeName = shape.IsByRef ? GetBufferElementTypeName(parameter.Type, kernelClass) : null,
                HasArrayFields = hasArrayFields,
                NonNullableArrayFields = parameter.RefKind == RefKind.None
                    ? GetNonNullableArrayFieldNames(marshaledType)
                    : default,
                CpuHoldsArrayMethodName = hasArrayFields
                    ? AotHostNames.GetQualifiedHoldsArrayMethodName(marshaledType, kernelClass)
                    : null,
                Cuda = ParameterTransfer.Create(marshaledType, HostDialects.Cuda, kernelClass),
                Cpu = ParameterTransfer.Create(marshaledType, HostDialects.Cpu, kernelClass)
            };
        }

        if (marshaledType.TryGetArrayLikeElementType(out var elementType))
        {
            var cpu = ParameterTransfer.Create(marshaledType, HostDialects.Cpu, kernelClass);
            return shape with
            {
                Kind = KernelParameterKind.Array,
                Container = GetContainer(marshaledType),
                ElementTypeName = elementType.GetFullyQualifiedCsharpName(),
                BufferElementTypeName = GetBufferElementTypeName(elementType, kernelClass),
                CpuHoldsArrayMethodName = cpu.ConvertedElementHasArrayFields
                    ? AotHostNames.GetQualifiedHoldsArrayMethodName(elementType, kernelClass)
                    : null,
                Cuda = ParameterTransfer.Create(marshaledType, HostDialects.Cuda, kernelClass),
                Cpu = cpu
            };
        }

        return shape.IsByRef
            ? shape with { BufferElementTypeName = GetBufferElementTypeName(parameter.Type, kernelClass) }
            : shape;
    }

    // Operands.ResultBuffer, ... (NonNullProof.GetNonNullableArrayFields as C# field names)
    private static EquatableArray<string> GetNonNullableArrayFieldNames(ITypeSymbol type)
    {
        var paths = NonNullProof.GetNonNullableArrayFields(type);
        if (paths.IsEmpty)
        {
            return default;
        }

        var names = ImmutableArray.CreateBuilder<string>(paths.Length);
        foreach (var path in paths)
        {
            var name = "";
            foreach (var field in path)
            {
                name = name.Length == 0 ? field.Name.ToCSharpIdentifier() : name + "." + field.Name.ToCSharpIdentifier();
            }

            names.Add(name);
        }

        return names.MoveToImmutable().AsEquatableArray();
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

    /// <summary>
    /// Whether the auto result array of a kernel returning <paramref name="returnType"/> is read back element by element
    /// under <paramref name="dialect"/> (#704): an unmanaged user struct that <see cref="IsConverted"/>. A managed return
    /// type is GSH0004 and gets no conversion.
    /// </summary>
    public static bool IsConvertedReturn(ITypeSymbol returnType, ITranspilerDialect dialect) =>
        returnType.IsUnmanagedType && IsConverted(returnType, dialect);

    // A user struct lives on the device in its native shadow layout, which the wrapper class declares as AotContexts.*
    private static string? GetBufferElementTypeName(ITypeSymbol element, INamedTypeSymbol? kernelClass) =>
        !element.IsUnmanagedType ? null
        : TypeLayoutCalculator.IsUserStruct(element) ? AotHostNames.GetAotStructPath(element, kernelClass)
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
