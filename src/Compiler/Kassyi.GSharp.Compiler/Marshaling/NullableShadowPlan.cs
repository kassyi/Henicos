namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>How the value of a <see cref="NullableShadowPlan"/> is held in the shadow struct.</summary>
internal enum NullableValueForm
{
    /// <summary>
    /// The value's own shadow struct (a user struct), converted by its <c>Marshal_</c> / <c>Unmarshal_</c>.
    /// </summary>
    Shadow,

    /// <summary>A field of the value's type (a primitive or an enum), assigned as it is.</summary>
    Named,

    /// <summary>
    /// A <c>fixed byte</c> buffer of the value's size (a vector type of the dialect), copied through a pointer.
    /// </summary>
    Bytes
}

/// <summary>
/// Everything that the shadow struct of a <c>Nullable&lt;T&gt;</c> and its marshaling methods are written from, for one
/// dialect (<see cref="NullableShadowEmitter"/>).
/// </summary>
/// <remarks>
/// The shadow has the device layout of <c>System_Nullable&lt;T&gt;</c>: <c>HasValue</c> in its first byte and the value
/// at <see cref="ValueOffset"/>. The marshaling reads and writes the host value through <c>HasValue</c> and
/// <c>GetValueOrDefault()</c>, whatever the CLR layout of <c>T</c> (#961).
/// </remarks>
/// <param name="NullableTypeName">The <c>global::</c>-qualified C# type of the <c>Nullable&lt;T&gt;</c>.</param>
/// <param name="ValueTypeName">The <c>global::</c>-qualified C# type of <c>T</c>.</param>
/// <param name="ShadowName">The name of the shadow struct in <c>AotContexts</c>.</param>
/// <param name="ShadowPath">The <c>AotContexts</c> path of the shadow struct.</param>
/// <param name="MarshalMethodName">The <c>Marshal_</c> method of the shadow.</param>
/// <param name="UnmarshalMethodName">The <c>Unmarshal_</c> method of the shadow.</param>
/// <param name="DeviceSize">The device size of the <c>Nullable&lt;T&gt;</c>.</param>
/// <param name="ValueOffset">The device offset of the value.</param>
/// <param name="ValueSize">The device size of the value.</param>
/// <param name="ValueForm">How the shadow holds the value.</param>
/// <param name="ValueShadowPath">
/// The <c>AotContexts</c> path of the value's shadow for <see cref="NullableValueForm.Shadow"/>.
/// </param>
/// <param name="ValueMarshalMethodName">The value's <c>Marshal_</c> for <see cref="NullableValueForm.Shadow"/>.</param>
/// <param name="ValueUnmarshalMethodName">
/// The value's <c>Unmarshal_</c> for <see cref="NullableValueForm.Shadow"/>.
/// </param>
/// <param name="ArraysParameter">The parameter of <c>Marshal_</c> that records the arrays of a launch.</param>
/// <param name="ArraysParameterType">The type of <paramref name="ArraysParameter"/>.</param>
/// <param name="ArrayHelpers">
/// The CPU methods that copy and find the arrays the value holds, or <see langword="null"/> when it holds none or on
/// CUDA.
/// </param>
internal sealed record NullableShadowPlan(
    string NullableTypeName,
    string ValueTypeName,
    string ShadowName,
    string ShadowPath,
    string MarshalMethodName,
    string UnmarshalMethodName,
    int DeviceSize,
    int ValueOffset,
    int ValueSize,
    NullableValueForm ValueForm,
    string? ValueShadowPath,
    string? ValueMarshalMethodName,
    string? ValueUnmarshalMethodName,
    string ArraysParameter,
    string ArraysParameterType,
    NullableArrayHelpers? ArrayHelpers)
{
    /// <summary>Plans the shadow of <paramref name="nullableType"/> under <paramref name="dialect"/>.</summary>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="nullableType"/> has no shadow (<see cref="HostShadowStructs.TryGetNullableValue"/>).
    /// </exception>
    public static NullableShadowPlan Create(INamedTypeSymbol nullableType, ITranspilerDialect dialect)
    {
        if (!HostShadowStructs.TryGetNullableValue(nullableType, out var value))
        {
            throw new InvalidOperationException(
                $"'{TypeLayoutCalculator.FormatTypeName(nullableType)}' has no Nullable shadow struct.");
        }

        var device = TypeLayoutCalculator.GetDeviceLayout(nullableType, dialect);
        var valueLayout = TypeLayoutCalculator.GetDeviceLayout(value, dialect);
        var form = TypeLayoutCalculator.IsUserStruct(value) ? NullableValueForm.Shadow
            : value.SpecialType != SpecialType.None || value.TypeKind == TypeKind.Enum ? NullableValueForm.Named
            : NullableValueForm.Bytes;
        var isShadow = form == NullableValueForm.Shadow;
        var helpers = !dialect.IsCuda && HostShadowStructs.HoldsArrayFields(value)
            ? new NullableArrayHelpers(
                GetName(AotHostNames.AppendCopyArraysMethodName, nullableType),
                GetName(AotHostNames.AppendHoldsArrayMethodName, nullableType),
                GetName(AotHostNames.AppendCopyArraysMethodName, value),
                GetName(AotHostNames.AppendHoldsArrayMethodName, value))
            : null;
        return new NullableShadowPlan(
            nullableType.GetFullyQualifiedCsharpName(),
            value.GetFullyQualifiedCsharpName(),
            AotHostNames.GetNativeStructName(nullableType),
            AotHostNames.GetAotStructPath(nullableType),
            GetName(AotHostNames.AppendMarshalMethodName, nullableType),
            GetName(AotHostNames.AppendUnmarshalMethodName, nullableType),
            device.Size,
            TypeLayoutCalculator.AlignUp(1, valueLayout.Alignment),
            valueLayout.Size,
            form,
            isShadow ? AotHostNames.GetAotStructPath(value) : null,
            isShadow ? GetName(AotHostNames.AppendMarshalMethodName, value) : null,
            isShadow ? GetName(AotHostNames.AppendUnmarshalMethodName, value) : null,
            StructArrayHelperEmitter.ArraysParameter(dialect),
            dialect.IsCuda ? $"{StructMarshalingEmitter.DeviceArrayRegionsType}?" : $"{HostTypeNames.PointerList}?",
            helpers);
    }

    private delegate ref readonly SourceWriter NameWriter(in SourceWriter writer, ISymbol symbol);

    private static string GetName(NameWriter append, ISymbol symbol)
    {
        using var writer = new SourceWriter();
        append(writer, symbol);
        return writer.ToString();
    }
}

/// <summary>
/// The CPU array helpers of a <c>Nullable&lt;T&gt;</c> whose value holds arrays (<see cref="NullableShadowPlan"/>).
/// </summary>
/// <param name="CopyArraysMethodName">The <c>CopyArrays_</c> method of the <c>Nullable&lt;T&gt;</c>.</param>
/// <param name="HoldsArrayMethodName">The <c>HoldsArray_</c> methods of the <c>Nullable&lt;T&gt;</c>.</param>
/// <param name="ValueCopyArraysMethodName">The value's <c>CopyArrays_</c> method.</param>
/// <param name="ValueHoldsArrayMethodName">The value's <c>HoldsArray_</c> method.</param>
internal sealed record NullableArrayHelpers(
    string CopyArraysMethodName,
    string HoldsArrayMethodName,
    string ValueCopyArraysMethodName,
    string ValueHoldsArrayMethodName);
