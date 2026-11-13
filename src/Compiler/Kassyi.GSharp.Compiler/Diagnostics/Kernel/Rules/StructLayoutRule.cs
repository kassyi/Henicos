namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>
/// Checks the user structs of kernel parameters, kernel return types and device locals against the layout model of the
/// project's dialect: a struct the device cannot lay out (GSH0078) or a member type the device has no type for (GSH0079);
/// an array or a returned struct whose elements the launcher converts one by one (GSH0080), and on CUDA an array whose
/// elements each allocate their array fields (GSH0081; the CPU launcher pins them instead); a field, at any depth,
/// whose type the generated launcher cannot name (GSH0108). Each is reported at the type of the parameter, return
/// value or local.
/// </summary>
/// <remarks>
/// The layouts are those of <see cref="TypeLayoutCalculator"/>, which the generated device definition, shadow struct
/// and launcher are built from, so the diagnostics describe what the generated code does.
/// </remarks>
internal sealed class StructLayoutRule :
    ISyntaxValidationRule<MethodDeclarationSyntax>,
    ISyntaxValidationRule<LocalDeclarationStatementSyntax>
{
    public static readonly StructLayoutRule Instance = new();

    public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
        GSharpDiagnostics.UnsupportedSyntax,
        GSharpDiagnostics.StructLayoutNotRepresentable,
        GSharpDiagnostics.FieldTypeHasNoDeviceRepresentation,
        GSharpDiagnostics.StructArrayConvertedPerElement,
        GSharpDiagnostics.StructArrayFieldAllocatedPerElement,
        GSharpDiagnostics.StructFieldTypeInaccessibleToLauncher);

    private StructLayoutRule()
    {
    }

    public void Analyze(MethodDeclarationSyntax node, ref ValidationContext context)
    {
        var method = context.MethodSymbol;
        if (!method.HasAttribute(KnownAttributes.GpuKernel))
        {
            return;
        }

        var parameters = node.ParameterList.Parameters;
        for (var i = 0; i < parameters.Count && i < method.Parameters.Length; i++)
        {
            if (parameters[i].Type is not { } typeSyntax)
            {
                continue;
            }

            var marshaledType = GetMarshaledType(method.Parameters[i].Type, out var isArray);
            var structType = AsUserStruct(marshaledType);
            if (structType is not null &&
                (ReportIfUnsupported(structType, typeSyntax, ref context) ||
                 ReportInaccessibleFieldType(structType, typeSyntax, ref context)))
            {
                continue;
            }

            if (isArray)
            {
                if (HostShadowStructs.IsConverted(marshaledType, context.Dialect))
                {
                    ReportConverted(marshaledType, typeSyntax, ref context);
                }

                // [WHY] The CUDA launcher allocates and copies the array fields of each element; the CPU one pins them
                if (structType is not null && context.Dialect.IsCuda &&
                    TransferClassifier.TryFindArrayField(structType, out var arrayField))
                {
                    context.ReportDiagnostic(
                        GSharpDiagnostics.StructArrayFieldAllocatedPerElement,
                        typeSyntax,
                        TypeLayoutCalculator.FormatTypeName(structType),
                        arrayField);
                }
            }
            else if (structType is not null &&
                     TryFindConvertedInlineArrayElement(structType, context.Dialect, depth: 0, out var element))
            {
                ReportConverted(element, typeSyntax, ref context);
            }
        }

        // The returned values come back as an array with one element per thread, converted like an array parameter's
        // (#704, #961). A managed return type is GSH0004 (KernelSignatureRule).
        if (!method.ReturnType.IsUnmanagedType || node.ReturnType is not { } returnSyntax)
        {
            return;
        }

        var returnStruct = AsUserStruct(GetMarshaledType(method.ReturnType, out _));
        if ((returnStruct is null ||
             (!ReportIfUnsupported(returnStruct, returnSyntax, ref context) &&
              !ReportInaccessibleFieldType(returnStruct, returnSyntax, ref context))) &&
            HostShadowStructs.IsConverted(method.ReturnType, context.Dialect))
        {
            ReportConverted(method.ReturnType, returnSyntax, ref context);
        }
    }

    public void Analyze(LocalDeclarationStatementSyntax node, ref ValidationContext context)
    {
        if (context.SemanticModel.GetTypeSymbol(node.Declaration.Type) is { } type &&
            TryGetUserStruct(type, out var structType, out _))
        {
            ReportIfUnsupported(structType, node.Declaration.Type, ref context);
        }
    }

    // The user struct of a parameter or local: the type itself, or the element of an array-like type, or the value of
    // a Nullable of either (#972)
    private static bool TryGetUserStruct(ITypeSymbol type, [NotNullWhen(true)] out INamedTypeSymbol? structType,
        out bool isArray)
    {
        structType = AsUserStruct(GetMarshaledType(type, out isArray));
        return structType is not null;
    }

    // The type that crosses the boundary one value at a time: the element of an array-like type (a Memory<T>?
    // included), otherwise the type itself
    private static ITypeSymbol GetMarshaledType(ITypeSymbol type, out bool isArray)
    {
        var marshaledType = type.UnwrapNullableArrayLike() ?? type;
        isArray = marshaledType.TryGetArrayLikeElementType(out var element);
        return isArray ? element : marshaledType;
    }

    // The user struct of a marshaled type or of its Nullable. A StackLinq enumerable is a StackLinq.cuh template on the
    // device, not a struct laid out from its C# fields (#1086)
    private static INamedTypeSymbol? AsUserStruct(ITypeSymbol marshaledType) =>
        marshaledType.UnwrapNullable() is INamedTypeSymbol structType &&
        TypeLayoutCalculator.IsUserStruct(structType) &&
        !StackLinqHelper.IsStackLinqType(structType)
            ? structType
            : null;

    // GSH0079 when a member type has no device representation, GSH0078 for any other reason. Returns whether it
    // reported.
    private static bool ReportIfUnsupported(INamedTypeSymbol structType, SyntaxNode typeSyntax,
        ref ValidationContext context)
    {
        if (GpuCodeConstraints.ContainsMultidimensionalArray(structType))
        {
            context.ReportDiagnostic(
                GSharpDiagnostics.UnsupportedSyntax,
                typeSyntax,
                GpuCodeConstraints.MultidimensionalArray);
            return true;
        }

        if (TypeLayoutCalculator.GetStructLayout(structType, context.Dialect).Unsupported is not { } unsupported)
        {
            return false;
        }

        if (unsupported is { IsMissingDeviceType: true, FieldType: { } fieldType })
        {
            context.ReportDiagnostic(
                GSharpDiagnostics.FieldTypeHasNoDeviceRepresentation,
                typeSyntax,
                TypeLayoutCalculator.FormatTypeName(fieldType),
                unsupported.FieldPath);
        }
        else
        {
            context.ReportDiagnostic(
                GSharpDiagnostics.StructLayoutNotRepresentable,
                typeSyntax,
                TypeLayoutCalculator.FormatTypeName(structType),
                unsupported.Reason);
        }

        return true;
    }

    // GSH0108: a field whose type the kernel's class, where the launcher is generated, cannot name (LauncherTypeAccess)
    private static bool ReportInaccessibleFieldType(INamedTypeSymbol structType, SyntaxNode typeSyntax,
        ref ValidationContext context)
    {
        var launcherClass = context.MethodSymbol.ContainingType;
        if (LauncherTypeAccess.Find(structType, launcherClass, context.SemanticModel.Compilation) is not { } found)
        {
            return false;
        }

        context.ReportDiagnostic(
            GSharpDiagnostics.StructFieldTypeInaccessibleToLauncher,
            typeSyntax,
            TypeLayoutCalculator.FormatTypeName(found.FieldType),
            found.FieldPath,
            TypeLayoutCalculator.FormatTypeName(launcherClass));
        return true;
    }

    private static void ReportConverted(ITypeSymbol type, SyntaxNode typeSyntax, ref ValidationContext context) =>
        context.ReportDiagnostic(
            GSharpDiagnostics.StructArrayConvertedPerElement,
            typeSyntax,
            TypeLayoutCalculator.FormatTypeName(type),
            TransferClassifier.DescribeLayoutDifference(type, context.Dialect));

    // The element of the first [InlineArray] in the struct, its nested structs included, that is converted element by
    // element
    private static bool TryFindConvertedInlineArrayElement(INamedTypeSymbol structType, ITranspilerDialect dialect,
        int depth, [NotNullWhen(true)] out INamedTypeSymbol? element)
    {
        const int MaxDepth = 16;
        element = null;
        if (depth > MaxDepth)
        {
            return false;
        }

        foreach (var member in StructAbiMembers.Get(structType))
        {
            if (member.Type is not INamedTypeSymbol nested || !TypeLayoutCalculator.IsUserStruct(nested))
            {
                continue;
            }

            if (member.Kind == StructAbiMemberKind.InlineArrayElement &&
                TypeLayoutCalculator.GetStructLayout(nested, dialect).Transfer == TransferClass.Convert)
            {
                element = nested;
                return true;
            }

            if (TryFindConvertedInlineArrayElement(nested, dialect, depth + 1, out element))
            {
                return true;
            }
        }

        return false;
    }
}
