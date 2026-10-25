namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>
/// Validates Tensor Core warp matrix (<c>Wmma</c>) fragment types, marker types, and operations (GSH0076).
/// </summary>
internal sealed class WmmaRule :
    ISyntaxValidationRule<MethodDeclarationSyntax>,
    ISyntaxValidationRule<LocalFunctionStatementSyntax>,
    ISyntaxValidationRule<ParameterSyntax>,
    ISyntaxValidationRule<VariableDeclarationSyntax>,
    ISyntaxValidationRule<BaseObjectCreationExpressionSyntax>,
    ISyntaxValidationRule<InvocationExpressionSyntax>
{
    public static readonly WmmaRule Instance = new();

    public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(GSharpDiagnostics.UnsupportedWarpMatrixOperation);

    private WmmaRule()
    {
    }

    public void Analyze(MethodDeclarationSyntax node, ref ValidationContext context)
    {
        var returnType = context.SemanticModel.GetTypeSymbol(node.ReturnType);
        if (!CheckTypeUsage(returnType, allowDirectFragment: false, isKernelParameter: false, out var targetName, out var reason))
        {
            context.ReportDiagnostic(GSharpDiagnostics.UnsupportedWarpMatrixOperation, node.ReturnType, targetName, reason);
        }
    }

    public void Analyze(LocalFunctionStatementSyntax node, ref ValidationContext context)
    {
        var returnType = context.SemanticModel.GetTypeSymbol(node.ReturnType);
        if (!CheckTypeUsage(returnType, allowDirectFragment: false, isKernelParameter: false, out var targetName, out var reason))
        {
            context.ReportDiagnostic(GSharpDiagnostics.UnsupportedWarpMatrixOperation, node.ReturnType, targetName, reason);
        }
    }

    public void Analyze(ParameterSyntax node, ref ValidationContext context)
    {
        if (node.Type is null)
        {
            return;
        }

        var paramType = context.SemanticModel.GetTypeSymbol(node.Type);
        var isKernelParam = node.Parent?.Parent is MethodDeclarationSyntax &&
                            context.MethodSymbol.HasAttribute(KnownAttributes.GpuKernel);
        if (!CheckTypeUsage(
                paramType,
                allowDirectFragment: !isKernelParam,
                isKernelParameter: isKernelParam,
                out var targetName,
                out var reason))
        {
            context.ReportDiagnostic(GSharpDiagnostics.UnsupportedWarpMatrixOperation, node.Type, targetName, reason);
        }
    }

    public void Analyze(VariableDeclarationSyntax node, ref ValidationContext context)
    {
        var type = context.SemanticModel.GetTypeSymbol(node.Type);
        if (CheckTypeUsage(type, allowDirectFragment: true, isKernelParameter: false, out var targetName, out var reason))
        {
            return;
        }

        var reportNode = node.Type.IsVar &&
                         node.Variables is [{ Initializer.Value: { } initVal }] &&
                         TryGetInitializerTypeSyntax(initVal) is { } initTypeSyntax
            ? initTypeSyntax
            : node.Type;

        context.ReportDiagnostic(GSharpDiagnostics.UnsupportedWarpMatrixOperation, reportNode, targetName, reason);
    }

    public void Analyze(BaseObjectCreationExpressionSyntax node, ref ValidationContext context)
    {
        if (node.Parent is EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax } })
        {
            return;
        }

        var type = context.SemanticModel.GetTypeSymbol(node);
        if (!CheckTypeUsage(type, allowDirectFragment: true, isKernelParameter: false, out var targetName, out var reason))
        {
            context.ReportDiagnostic(GSharpDiagnostics.UnsupportedWarpMatrixOperation, node, targetName, reason);
        }
    }

    public void Analyze(InvocationExpressionSyntax node, ref ValidationContext context)
    {
        if (context.SemanticModel.GetSymbol(node) is not IMethodSymbol method)
        {
            return;
        }

        if (method.ContainingType is { Name: TypeConstants.WmmaType } &&
            method.ContainingNamespace.IsCoreNamespace())
        {
            AnalyzeWmmaCall(node, method, ref context);
            return;
        }

        if (!method.IsGenericMethod)
        {
            return;
        }

        foreach (var typeArg in method.TypeArguments)
        {
            if (!CheckTypeUsage(typeArg, allowDirectFragment: false, isKernelParameter: false, out var targetName, out var reason))
            {
                context.ReportDiagnostic(GSharpDiagnostics.UnsupportedWarpMatrixOperation, node, targetName, reason);
                break;
            }
        }
    }

    private static void AnalyzeWmmaCall(
        InvocationExpressionSyntax node,
        IMethodSymbol method,
        ref ValidationContext context)
    {
        var arguments = node.ArgumentList.Arguments;

        switch (method.Name)
        {
            case TypeConstants.WmmaTypes.MultiplyAccumulate:
            {
                if (method.TypeArguments.Length >= 5 &&
                    !IsSupportedMultiplyAccumulate(
                        method.TypeArguments[0],
                        method.TypeArguments[1],
                        method.TypeArguments[4],
                        out var mmaReason))
                {
                    context.ReportDiagnostic(
                        GSharpDiagnostics.UnsupportedWarpMatrixOperation,
                        node,
                        "Wmma.MultiplyAccumulate",
                        mmaReason);
                }

                foreach (var arg in arguments)
                {
                    if (!RefSyntaxHelper.IsCppLValue(arg.Expression, context.SemanticModel))
                    {
                        context.ReportDiagnostic(
                            GSharpDiagnostics.UnsupportedWarpMatrixOperation,
                            arg.Expression,
                            "Wmma.MultiplyAccumulate",
                            "fragment arguments must be variables, not temporary values");
                    }
                }

                break;
            }

            case TypeConstants.WmmaTypes.Load when arguments.Count >= 2:
            {
                var fragArg = FindArgument(arguments, "fragment", 0);
                var srcArg = FindArgument(arguments, "source", 1);

                if (fragArg is not null && !RefSyntaxHelper.IsCppLValue(fragArg.Expression, context.SemanticModel))
                {
                    context.ReportDiagnostic(
                        GSharpDiagnostics.UnsupportedWarpMatrixOperation,
                        fragArg.Expression,
                        "Wmma.Load",
                        "the destination fragment must be a variable, not a temporary value");
                }

                if (srcArg is not null && !IsGlobalOrSharedMemoryElement(srcArg.Expression, context.SemanticModel))
                {
                    context.ReportDiagnostic(
                        GSharpDiagnostics.UnsupportedWarpMatrixOperation,
                        srcArg.Expression,
                        "Wmma.Load",
                        "the source must refer to global or shared memory (an array, Span, or SharedMemory<T> element, or a ref/in parameter)");
                }

                break;
            }

            case TypeConstants.WmmaTypes.Store when arguments.Count >= 2:
            {
                var dstArg = FindArgument(arguments, "destination", 0);
                var fragArg = FindArgument(arguments, "fragment", 1);

                if (dstArg is not null && !IsGlobalOrSharedMemoryElement(dstArg.Expression, context.SemanticModel))
                {
                    context.ReportDiagnostic(
                        GSharpDiagnostics.UnsupportedWarpMatrixOperation,
                        dstArg.Expression,
                        "Wmma.Store",
                        "the destination must refer to global or shared memory (an array, Span, or SharedMemory<T> element, or a ref parameter)");
                }

                if (fragArg is not null && !RefSyntaxHelper.IsCppLValue(fragArg.Expression, context.SemanticModel))
                {
                    context.ReportDiagnostic(
                        GSharpDiagnostics.UnsupportedWarpMatrixOperation,
                        fragArg.Expression,
                        "Wmma.Store",
                        "the source fragment must be a variable, not a temporary value");
                }

                break;
            }

            case TypeConstants.WmmaTypes.Fill when arguments.Count >= 1:
            {
                var fragArg = FindArgument(arguments, "fragment", 0);
                if (fragArg is not null && !RefSyntaxHelper.IsCppLValue(fragArg.Expression, context.SemanticModel))
                {
                    context.ReportDiagnostic(
                        GSharpDiagnostics.UnsupportedWarpMatrixOperation,
                        fragArg.Expression,
                        "Wmma.Fill",
                        "the fragment must be a variable, not a temporary value");
                }

                break;
            }
        }
    }

    private static bool IsGlobalOrSharedMemoryElement(ExpressionSyntax expression, SemanticModel semanticModel) =>
        RefSyntaxHelper.IsCppLValue(expression, semanticModel) &&
        (CopyAsyncRule.IsGlobalMemorySourceLocation(expression, semanticModel) ||
         CopyAsyncRule.IsSharedMemoryLocation(expression, semanticModel));

    private static ArgumentSyntax? FindArgument(
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        string parameterName,
        int positionalIndex)
    {
        foreach (var arg in arguments)
        {
            if (arg.NameColon is { Name.Identifier.ValueText: var name } && name == parameterName)
            {
                return arg;
            }
        }

        return positionalIndex < arguments.Count && arguments[positionalIndex].NameColon is null
            ? arguments[positionalIndex]
            : null;
    }

    private static TypeSyntax? TryGetInitializerTypeSyntax(ExpressionSyntax initializer) =>
        initializer switch
        {
            ObjectCreationExpressionSyntax objectCreation => objectCreation.Type,
            DefaultExpressionSyntax defaultExpr => defaultExpr.Type,
            _ => null
        };

    private static bool CheckTypeUsage(
        ITypeSymbol? type,
        bool allowDirectFragment,
        bool isKernelParameter,
        [NotNullWhen(false)] out string? targetName,
        [NotNullWhen(false)] out string? reason,
        int depth = 0)
    {
        if (type is null || depth > 8)
        {
            targetName = null;
            reason = null;
            return true;
        }

        if (type.IsWmmaFragmentType() && type is INamedTypeSymbol fragmentType)
        {
            if (!allowDirectFragment)
            {
                targetName = fragmentType.Name;
                reason = isKernelParameter
                    ? "WMMA fragments cannot be passed as kernel parameters"
                    : "WMMA fragments can only be declared as local variables or device function parameters";
                return false;
            }

            if (!IsSupportedFragmentType(fragmentType, out reason))
            {
                targetName = fragmentType.Name;
                return false;
            }

            targetName = null;
            reason = null;
            return true;
        }

        if (type.IsWmmaMarkerType())
        {
            targetName = type.Name;
            reason = "WMMA shape and layout markers can only be used as type arguments of WMMA fragments";
            return false;
        }

        if (type is IPointerTypeSymbol pointerType &&
            ContainsWmmaFragmentOrMarker(pointerType.PointedAtType, out var ptrWmmaName))
        {
            targetName = ptrWmmaName;
            reason = "pointers to WMMA fragments or markers are not supported";
            return false;
        }

        if (type.TryGetArrayLikeElementType(out var elemType) &&
            ContainsWmmaFragmentOrMarker(elemType, out var elemWmmaName))
        {
            targetName = elemWmmaName;
            reason = "arrays or spans of WMMA fragments or markers are not supported";
            return false;
        }

        if (type is INamedTypeSymbol namedType)
        {
            foreach (var typeArg in namedType.TypeArguments)
            {
                if (ContainsWmmaFragmentOrMarker(typeArg, out var argWmmaName))
                {
                    targetName = argWmmaName;
                    reason = $"WMMA fragments and markers cannot be used as type arguments of '{namedType.Name}'";
                    return false;
                }
            }

            if (namedType.TypeKind == TypeKind.Struct &&
                !namedType.ContainingNamespace.IsSystemOrNumerics() &&
                !namedType.ContainingNamespace.IsCoreNamespace())
            {
                foreach (var member in namedType.GetMembers())
                {
                    switch (member)
                    {
                        case IFieldSymbol { IsStatic: false } field
                            when ContainsWmmaFragmentOrMarker(field.Type, out var fieldWmmaName, depth + 1):
                            targetName = namedType.Name;
                            reason = $"struct '{namedType.Name}' cannot contain WMMA fragment or marker field '{field.Name}' of type '{fieldWmmaName}'";
                            return false;
                        case IPropertySymbol { IsStatic: false } prop
                            when ContainsWmmaFragmentOrMarker(prop.Type, out var propWmmaName, depth + 1):
                            targetName = namedType.Name;
                            reason = $"struct '{namedType.Name}' cannot contain WMMA fragment or marker property '{prop.Name}' of type '{propWmmaName}'";
                            return false;
                    }
                }
            }
        }

        targetName = null;
        reason = null;
        return true;
    }

    private static bool ContainsWmmaFragmentOrMarker(
        ITypeSymbol? type,
        [NotNullWhen(true)] out string? wmmaTypeName,
        int depth = 0)
    {
        if (type is null || depth > 8)
        {
            wmmaTypeName = null;
            return false;
        }

        if (type.IsWmmaFragmentType() || type.IsWmmaMarkerType())
        {
            wmmaTypeName = type.Name;
            return true;
        }

        if (type is IPointerTypeSymbol pointerType)
        {
            return ContainsWmmaFragmentOrMarker(pointerType.PointedAtType, out wmmaTypeName, depth + 1);
        }

        if (type.TryGetArrayLikeElementType(out var elemType))
        {
            return ContainsWmmaFragmentOrMarker(elemType, out wmmaTypeName, depth + 1);
        }

        if (type is INamedTypeSymbol namedType)
        {
            foreach (var typeArg in namedType.TypeArguments)
            {
                if (ContainsWmmaFragmentOrMarker(typeArg, out wmmaTypeName, depth + 1))
                {
                    return true;
                }
            }

            if (namedType.TypeKind == TypeKind.Struct &&
                !namedType.ContainingNamespace.IsSystemOrNumerics() &&
                !namedType.ContainingNamespace.IsCoreNamespace())
            {
                foreach (var member in namedType.GetMembers())
                {
                    switch (member)
                    {
                        case IFieldSymbol { IsStatic: false } field
                            when ContainsWmmaFragmentOrMarker(field.Type, out wmmaTypeName, depth + 1):
                            return true;
                        case IPropertySymbol { IsStatic: false } prop
                            when ContainsWmmaFragmentOrMarker(prop.Type, out wmmaTypeName, depth + 1):
                            return true;
                    }
                }
            }
        }

        wmmaTypeName = null;
        return false;
    }

    private static bool IsSupportedFragmentType(INamedTypeSymbol fragmentType, [NotNullWhen(false)] out string? reason)
    {
        if (fragmentType.TypeArguments.Length < 2)
        {
            reason = "fragment is missing required type arguments";
            return false;
        }

        var shape = fragmentType.TypeArguments[0];
        var elem = fragmentType.TypeArguments[1];

        if (fragmentType.Name is TypeConstants.WmmaTypes.MatrixA or TypeConstants.WmmaTypes.MatrixB)
        {
            if (fragmentType.TypeArguments.Length < 3 || !WmmaTypeSyntaxHelper.TryGetLayoutCppName(fragmentType.TypeArguments[2], out _))
            {
                reason = "matrix layout must be WmmaRowMajor or WmmaColumnMajor";
                return false;
            }

            if (IsInt8Shape(shape))
            {
                if (elem.SpecialType is SpecialType.System_SByte or SpecialType.System_Byte)
                {
                    reason = null;
                    return true;
                }

                reason = $"shape '{shape.Name}' requires element type 'sbyte' or 'byte' for {fragmentType.Name}, not '{elem.Name}'";
                return false;
            }

            if (IsDoubleShape(shape))
            {
                if (elem.SpecialType == SpecialType.System_Double)
                {
                    reason = null;
                    return true;
                }

                reason = $"shape '{shape.Name}' requires element type 'double' for {fragmentType.Name}, not '{elem.Name}'";
                return false;
            }

            reason = $"shape '{shape.Name}' is not a supported WMMA shape marker";
            return false;
        }

        if (IsInt8Shape(shape))
        {
            if (elem.SpecialType == SpecialType.System_Int32)
            {
                reason = null;
                return true;
            }

            reason = $"shape '{shape.Name}' requires accumulator element type 'int', not '{elem.Name}'";
            return false;
        }

        if (IsDoubleShape(shape))
        {
            if (elem.SpecialType == SpecialType.System_Double)
            {
                reason = null;
                return true;
            }

            reason = $"shape '{shape.Name}' requires accumulator element type 'double', not '{elem.Name}'";
            return false;
        }

        reason = $"shape '{shape.Name}' is not a supported WMMA shape marker";
        return false;
    }

    private static bool IsSupportedMultiplyAccumulate(
        ITypeSymbol shape,
        ITypeSymbol inputElement,
        ITypeSymbol accumulatorElement,
        [NotNullWhen(false)] out string? reason)
    {
        if (IsInt8Shape(shape) &&
            inputElement.SpecialType is SpecialType.System_SByte or SpecialType.System_Byte &&
            accumulatorElement.SpecialType == SpecialType.System_Int32)
        {
            reason = null;
            return true;
        }

        if (IsDoubleShape(shape) &&
            inputElement.SpecialType == SpecialType.System_Double &&
            accumulatorElement.SpecialType == SpecialType.System_Double)
        {
            reason = null;
            return true;
        }

        reason = $"shape '{shape.Name}' with input element '{inputElement.Name}' and accumulator element '{accumulatorElement.Name}' is not supported";
        return false;
    }

    private static bool IsInt8Shape(ITypeSymbol shape) =>
        shape is INamedTypeSymbol
        {
            Name: TypeConstants.WmmaTypes.M16N16K16
                or TypeConstants.WmmaTypes.M32N8K16
                or TypeConstants.WmmaTypes.M8N32K16
        } && shape.ContainingNamespace.IsCoreNamespace();

    private static bool IsDoubleShape(ITypeSymbol shape) =>
        shape is INamedTypeSymbol { Name: TypeConstants.WmmaTypes.M8N8K4 } &&
        shape.ContainingNamespace.IsCoreNamespace();
}
