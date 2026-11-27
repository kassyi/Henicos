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
    Bytes,

    /// <summary>
    /// A <c>GSharpArrayProxy</c> of an array-like value (<c>Memory&lt;T&gt;?</c>), marshaled as an array-like field is
    /// (<see cref="NullableShadowPlan.ArrayValue"/>, #1115).
    /// </summary>
    ArrayView
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
/// <param name="ArrayValue">
/// The view of an array-like value for <see cref="NullableValueForm.ArrayView"/>, otherwise <see langword="null"/>.
/// </param>
/// <param name="HeldArrays">
/// The methods that collect the held arrays and views, or <see langword="null"/> when the value holds none.
/// </param>
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
    ArrayFieldView? ArrayValue,
    NullableArrayHelpers? ArrayHelpers,
    NullableHeldArrays? HeldArrays)
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
        var isArray = value.IsArrayLikeType();
        var form = NativeStructEmitter.ClassifyType(value, dialect) switch
        {
            NativeFieldKind.ArrayProxy => NullableValueForm.ArrayView,
            NativeFieldKind.UserStruct => NullableValueForm.Shadow,
            NativeFieldKind.DirectValue => NullableValueForm.Named,
            _ => NullableValueForm.Bytes,
        };
        var isShadow = form == NullableValueForm.Shadow;
        return new NullableShadowPlan(
            nullableType.GetFullyQualifiedCsharpName(),
            value.GetFullyQualifiedCsharpName(),
            AotHostNames.GetNativeStructName(nullableType),
            AotHostNames.GetAotStructPath(nullableType),
            AotHostNames.GetMarshalMethodName(nullableType),
            AotHostNames.GetUnmarshalMethodName(nullableType),
            device.Size,
            TypeLayoutCalculator.AlignUp(1, valueLayout.Alignment),
            valueLayout.Size,
            form,
            isShadow ? AotHostNames.GetAotStructPath(value) : null,
            isShadow ? AotHostNames.GetMarshalMethodName(value) : null,
            isShadow ? AotHostNames.GetUnmarshalMethodName(value) : null,
            StructArrayHelperEmitter.ArraysParameter(dialect),
            dialect.IsCuda ? $"{StructMarshalingEmitter.DeviceArrayRegionsType}?" : $"{HostTypeNames.PointerList}?",
            isArray ? ArrayFieldView.Create(value, dialect) : null,
            PlanArrayHelpers(nullableType, value, isArray, dialect),
            PlanHeldArrays(nullableType, value, dialect));
    }

    private static NullableHeldArrays? PlanHeldArrays(INamedTypeSymbol nullableType, ITypeSymbol value,
        ITranspilerDialect dialect) =>
        PlanHeldValue(value, dialect) is { } heldValue
            ? new NullableHeldArrays(AotHostNames.GetAliasHeldArraysMethodName(nullableType), heldValue)
            : null;

    private static NullableHeldValue? PlanHeldValue(ITypeSymbol type, ITranspilerDialect dialect)
    {
        if (type is IArrayTypeSymbol arrayType)
        {
            var element = arrayType.ElementType;
            var alignment = element.IsReferenceType
                ? 0
                : TypeLayoutCalculator.GetDeviceLayout(element, dialect).Alignment;
            var converted = KernelParameterShape.IsConverted(element, dialect);
            var cuda = StructArrayHelperEmitter.HeldCudaAlignment(element, dialect);
            var child = StructArrayHelperEmitter.HoldsArrays(element) ? PlanHeldValue(element, dialect) : null;
            return new NullableHeldArray(alignment, cuda, converted, child);
        }

        if (type.IsArrayLikeType())
        {
            if (!type.TryGetArrayLikeElementType(out var element))
            {
                return null;
            }

            var view = ArrayFieldView.Create(type, dialect);
            var alignment = element.IsReferenceType
                ? 0
                : TypeLayoutCalculator.GetDeviceLayout(element, dialect).Alignment;
            var converted = KernelParameterShape.IsConverted(element, dialect);
            var cuda = StructArrayHelperEmitter.HeldCudaAlignment(element, dialect);
            var child = StructArrayHelperEmitter.HoldsArrays(element) ? PlanHeldValue(element, dialect) : null;
            return new NullableHeldView(view, alignment, cuda, converted, child);
        }

        if (HostShadowStructs.HoldsArrayFields(type) || type.IsNullableArrayLike())
        {
            return new NullableHeldMethod(AotHostNames.GetAliasHeldArraysMethodName(type));
        }

        return null;
    }

    // CopyArrays_ for a value that holds arrays or is one (the [In] copy of the view), HoldsArray_ for a value whose
    // arrays are compared by identity (a struct with array fields); the CPU only
    private static NullableArrayHelpers? PlanArrayHelpers(INamedTypeSymbol nullableType, ITypeSymbol value,
        bool isArray, ITranspilerDialect dialect)
    {
        if (dialect.IsCuda || !(isArray || HostShadowStructs.HoldsArrayFields(value)))
        {
            return null;
        }

        return new NullableArrayHelpers(
            AotHostNames.GetCopyArraysMethodName(nullableType),
            isArray ? null : AotHostNames.GetHoldsArrayMethodName(nullableType),
            isArray ? null : AotHostNames.GetCopyArraysMethodName(value),
            isArray ? null : AotHostNames.GetHoldsArrayMethodName(value));
    }
}

/// <summary>
/// The <c>AliasHeldArrays_</c> plan of a <c>Nullable&lt;T&gt;</c> whose value is an array-like view or a struct that
/// holds arrays or views, for the comparison of held arrays and views with the slices of a launch.
/// </summary>
/// <param name="MethodName">The method of the <c>Nullable&lt;T&gt;</c>.</param>
/// <param name="Value">The planned walk for the unwrapped value.</param>
internal sealed record NullableHeldArrays(string MethodName, NullableHeldValue Value);

/// <summary>A planned node in an <c>AliasHeldArrays_</c> walk of a <c>Nullable&lt;T&gt;</c> value.</summary>
internal abstract record NullableHeldValue;

/// <summary>An array (<c>T[]</c>) element inside a <c>Nullable&lt;T&gt;</c> view.</summary>
/// <param name="ElementAlignment">The device alignment of the element.</param>
/// <param name="ElementCudaAlignment">
/// The CUDA alignment of the element on a CPU launcher (<see cref="StructArrayHelperEmitter.HeldCudaAlignment"/>).
/// </param>
/// <param name="ElementIsConverted">Whether the element is converted on the dialect's device.</param>
/// <param name="Element">The walk for each element when it holds arrays or views, otherwise <see langword="null"/>.</param>
internal sealed record NullableHeldArray(
    int ElementAlignment,
    int ElementCudaAlignment,
    bool ElementIsConverted,
    NullableHeldValue? Element) : NullableHeldValue;

/// <summary>A non-array view (<c>Memory&lt;T&gt;</c>, etc.) inside a <c>Nullable&lt;T&gt;</c>.</summary>
/// <param name="View">The view access plan.</param>
/// <param name="ElementAlignment">The device alignment of the element.</param>
/// <param name="ElementCudaAlignment">
/// The CUDA alignment of the element on a CPU launcher (<see cref="StructArrayHelperEmitter.HeldCudaAlignment"/>).
/// </param>
/// <param name="ElementIsConverted">Whether the element is converted on the dialect's device.</param>
/// <param name="Element">The walk for each element when it holds arrays or views, otherwise <see langword="null"/>.</param>
internal sealed record NullableHeldView(
    ArrayFieldView View,
    int ElementAlignment,
    int ElementCudaAlignment,
    bool ElementIsConverted,
    NullableHeldValue? Element) : NullableHeldValue;

/// <summary>A struct or <c>Nullable&lt;T&gt;</c> whose own <c>AliasHeldArrays_</c> method collects its arrays/views.</summary>
/// <param name="MethodName">The <c>AliasHeldArrays_</c> method to call.</param>
internal sealed record NullableHeldMethod(string MethodName) : NullableHeldValue;

/// <summary>
/// The CPU array helpers of a <c>Nullable&lt;T&gt;</c> whose value holds arrays (<see cref="NullableShadowPlan"/>).
/// </summary>
/// <param name="CopyArraysMethodName">The <c>CopyArrays_</c> method of the <c>Nullable&lt;T&gt;</c>.</param>
/// <param name="HoldsArrayMethodName">
/// The <c>HoldsArray_</c> methods of the <c>Nullable&lt;T&gt;</c>, or <see langword="null"/> for an array-like value,
/// whose arrays are not compared by identity.
/// </param>
/// <param name="ValueCopyArraysMethodName">
/// The value's <c>CopyArrays_</c> method, or <see langword="null"/> for an array-like value, which the view emitter
/// copies itself.
/// </param>
/// <param name="ValueHoldsArrayMethodName">
/// The value's <c>HoldsArray_</c> method, with <paramref name="HoldsArrayMethodName"/>.
/// </param>
internal sealed record NullableArrayHelpers(
    string CopyArraysMethodName,
    string? HoldsArrayMethodName,
    string? ValueCopyArraysMethodName,
    string? ValueHoldsArrayMethodName);
