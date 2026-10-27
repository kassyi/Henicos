namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>
/// Checks the user structs of kernel parameters and device locals against the layout model of the project's dialect:
/// a struct the device cannot lay out (GSH0078) or a member type the device has no type for (GSH0079); on CUDA, an
/// array whose elements the launcher converts one by one (GSH0080) and an array whose elements each allocate their
/// array fields (GSH0081); on the CPU dialect, an array of a struct whose layouts differ, which the CPU launcher cannot
/// convert yet (GSH0082). Each is reported at the type of the parameter or local.
/// </summary>
/// <remarks>
/// The layouts are those of <see cref="TypeLayoutCalculator"/>, which the generated device definition, shadow struct
/// and launcher are built from, so the diagnostics describe what the generated code does. The CPU launcher passes a
/// struct by value or by reference through its shadow struct (#511); GSH0082 goes away when it converts struct arrays
/// as well.
/// </remarks>
internal sealed class StructLayoutRule :
    ISyntaxValidationRule<MethodDeclarationSyntax>,
    ISyntaxValidationRule<LocalDeclarationStatementSyntax>
{
    public static readonly StructLayoutRule Instance = new();

    public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
        GSharpDiagnostics.StructLayoutNotRepresentable,
        GSharpDiagnostics.FieldTypeHasNoDeviceRepresentation,
        GSharpDiagnostics.StructArrayConvertedPerElement,
        GSharpDiagnostics.StructArrayFieldAllocatedPerElement,
        GSharpDiagnostics.StructTransferNotSupportedOnCpu);

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
            if (parameters[i].Type is not { } typeSyntax ||
                !TryGetUserStruct(method.Parameters[i].Type, out var structType, out var isArray))
            {
                continue;
            }

            if (ReportUnsupported(structType, typeSyntax, ref context))
            {
                continue;
            }

            // [WHY] The CPU launcher converts a struct by value or by ref through its shadow struct, but still passes an
            // array of structs in the host layout
            var transfer = TypeLayoutCalculator.GetStructLayout(structType, context.Dialect).Transfer;
            if (!context.Dialect.IsCuda)
            {
                if (isArray && transfer != TransferClass.Identical)
                {
                    context.ReportDiagnostic(
                        GSharpDiagnostics.StructTransferNotSupportedOnCpu,
                        typeSyntax,
                        TypeLayoutCalculator.FormatTypeName(structType),
                        TypeLayoutCalculator.DescribeLayoutDifference(structType, context.Dialect));
                }

                continue;
            }

            if (isArray)
            {
                if (transfer == TransferClass.Convert)
                {
                    ReportConverted(structType, typeSyntax, ref context);
                }

                if (TypeLayoutCalculator.TryFindArrayField(structType, out var arrayField))
                {
                    context.ReportDiagnostic(
                        GSharpDiagnostics.StructArrayFieldAllocatedPerElement,
                        typeSyntax,
                        TypeLayoutCalculator.FormatTypeName(structType),
                        arrayField);
                }
            }
            else if (TryFindConvertedInlineArrayElement(structType, context.Dialect, depth: 0, out var element))
            {
                ReportConverted(element, typeSyntax, ref context);
            }
        }
    }

    public void Analyze(LocalDeclarationStatementSyntax node, ref ValidationContext context)
    {
        if (context.SemanticModel.GetTypeSymbol(node.Declaration.Type) is { } type &&
            TryGetUserStruct(type, out var structType, out _))
        {
            ReportUnsupported(structType, node.Declaration.Type, ref context);
        }
    }

    // The user struct of a parameter or local: the type itself, or the element of an array-like type
    private static bool TryGetUserStruct(ITypeSymbol type, [NotNullWhen(true)] out INamedTypeSymbol? structType,
        out bool isArray)
    {
        var marshaledType = type.UnwrapNullableArrayLike() ?? type;
        isArray = marshaledType.TryGetArrayLikeElementType(out var element);
        structType = (isArray ? element : marshaledType) as INamedTypeSymbol;
        return structType is not null && TypeLayoutCalculator.IsUserStruct(structType);
    }

    // GSH0079 when a member type has no device representation, GSH0078 for any other reason
    private static bool ReportUnsupported(INamedTypeSymbol structType, SyntaxNode typeSyntax,
        ref ValidationContext context)
    {
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

    private static void ReportConverted(INamedTypeSymbol structType, SyntaxNode typeSyntax,
        ref ValidationContext context) =>
        context.ReportDiagnostic(
            GSharpDiagnostics.StructArrayConvertedPerElement,
            typeSyntax,
            TypeLayoutCalculator.FormatTypeName(structType),
            TypeLayoutCalculator.DescribeLayoutDifference(structType, context.Dialect));

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
