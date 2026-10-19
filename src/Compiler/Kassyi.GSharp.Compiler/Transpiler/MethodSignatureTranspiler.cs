
namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Provides code emission services for method signatures, parameter qualifiers, and target-specific function prefixes.
/// </summary>
internal static class MethodSignatureTranspiler
{
    public static string GetKernelMethodName(MethodDeclarationSyntax method, IMethodSymbol? methodSymbol) =>
        methodSymbol != null ? $"{methodSymbol.ContainingType.Name}_{method.Identifier.Text}" : method.Identifier.Text;

    public static void WriteMethodSignature(MethodDeclarationSyntax method, bool isDevice, bool hasContext,
        ref TranspileContext context)
    {
        var methodSymbol = context.Query.SemanticModel.GetDeclaredSymbol<IMethodSymbol>(method);
        ReportUnsupportedParameterTypes(methodSymbol, ref context);

        if (isDevice)
        {
            WriteDeviceMethodPrefix(methodSymbol, ref context);
        }
        else
        {
            WriteKernelMethodPrefix(ref context);
        }

        var methodName = isDevice ? method.Identifier.Text : GetKernelMethodName(method, methodSymbol);

        context.Emitter.Writer.Append($" {methodName}");
        using (context.Emitter.Writer.Parentheses())
        {
            WriteParameters(method.ParameterList.Parameters, isDevice, ref context);

            if (hasContext)
            {
                if (method.ParameterList.Parameters.Count > 0)
                {
                    context.Emitter.Writer.Append(", ");
                }

                context.Emitter.Writer.Append($"__gsharp_context_{methodName}* {GSharpInternalNames.Context}");
            }

            if (isDevice || methodSymbol is null || !methodSymbol.HasAttribute(KnownAttributes.GpuKernel) ||
                methodSymbol.ReturnType.SpecialType == SpecialType.System_Void)
            {
                return;
            }

            if (method.ParameterList.Parameters.Count > 0 || hasContext)
            {
                context.Emitter.Writer.Append(", ");
            }

            context.Emitter.Dialect.AppendType(ref context, methodSymbol.ReturnType);
            context.Emitter.Writer.Append("* __restrict__ __gsharp_auto_result");
        }
    }

    // [WHY] Reported here rather than in WriteParameters, which also runs for the CPU host launcher
    // and would duplicate the diagnostic for the same parameter.
    private static void ReportUnsupportedParameterTypes(IMethodSymbol? methodSymbol, ref TranspileContext context)
    {
        if (methodSymbol is null)
        {
            return;
        }

        foreach (var parameter in methodSymbol.Parameters)
        {
            if (parameter.Type is not IFunctionPointerTypeSymbol ||
                !parameter.TryGetDeclaringSyntax<ParameterSyntax>(out var paramSyntax) ||
                paramSyntax.Type is not { } typeSyntax)
            {
                continue;
            }

            context.Reporter.Report(GSharpDiagnostics.UnsupportedSyntax, typeSyntax, typeSyntax.Kind().ToString());
        }
    }

    internal static (bool IsRefPointer, bool IsPointerType, bool HasRestrict) WriteParameterDeclaration(
        ParameterSyntax p, ITypeSymbol? typeSymbol, IParameterSymbol? parameterSymbol, ref TranspileContext context)
    {
        typeSymbol = typeSymbol.UnwrapNullableArrayLike();
        var isReadOnly = parameterSymbol != null && parameterSymbol.HasAttribute(KnownAttributes.GpuReadOnly);
        var isArray = typeSymbol.IsArrayLikeType();

        if (isReadOnly && !isArray)
        {
            context.Emitter.Writer.Append("const ");
        }

        return context.Emitter.Dialect.AppendParameter(ref context, p, typeSymbol, parameterSymbol);
    }

    public static void WriteParameters(SeparatedSyntaxList<ParameterSyntax> parameters, bool isDevice,
        ref TranspileContext context)
    {
        if (parameters.Count == 0)
        {
            return;
        }

        var paramSemanticModel = context.Query.SemanticModel.Compilation.GetSemanticModel(parameters[0].SyntaxTree);

        for (var i = 0; i < parameters.Count; i++)
        {
            var p = parameters[i];
            if (p.Type == null)
            {
                continue;
            }

            var typeSymbol = paramSemanticModel.GetTypeSymbol(p.Type).UnwrapNullableArrayLike();

            StructTranspiler.RegisterUsedStructIfApplicable(typeSymbol, ref context);

            var paramSymbol = paramSemanticModel.GetDeclaredSymbol<IParameterSymbol>(p);

            var (isRefPointer, isPointerType, hasRestrict) =
                WriteParameterDeclaration(p, typeSymbol, paramSymbol, ref context);

            StructTranspiler.CheckLargeStructByValue(p, typeSymbol, isRefPointer, ref context);

            if (!isDevice && isPointerType && !hasRestrict)
            {
                context.Emitter.Writer.Append(" __restrict__");
            }

            context.Emitter.Writer.Append($" {p.Identifier.Text}");
            if (i < parameters.Count - 1)
            {
                context.Emitter.Writer.Append(", ");
            }
        }
    }

    private static void WriteDeviceMethodPrefix(IMethodSymbol? methodSymbol, ref TranspileContext context)
    {
        var inlineKeyword = GetInliningKeyword(methodSymbol);

        if (context.Emitter.Dialect.IsCuda)
        {
            context.Emitter.Writer.Append($"{inlineKeyword} __device__ ");
        }
        else
        {
            context.Emitter.Writer.Append($"{inlineKeyword} ");
        }

        var returnType = methodSymbol?.ReturnType;
        if (returnType is { SpecialType: not SpecialType.System_Void })
        {
            // C# ref returns map onto C++ lvalue references; 'ref readonly' adds const.
            if (methodSymbol is { RefKind: RefKind.RefReadOnly })
            {
                context.Emitter.Writer.Append("const ");
            }

            context.Emitter.Dialect.AppendType(ref context, returnType);

            if (methodSymbol is { RefKind: RefKind.Ref or RefKind.RefReadOnly })
            {
                context.Emitter.Writer.Append("&");
            }
        }
        else
        {
            context.Emitter.Writer.AppendKeyword("void");
        }
    }

    private static string GetInliningKeyword(IMethodSymbol? methodSymbol)
    {
        if (methodSymbol == null)
        {
            return "inline";
        }

        foreach (var attr in methodSymbol.GetAttributes())
        {
            if (attr.IsAttribute(KnownAttributes.GpuInline))
            {
                return "__forceinline__";
            }

            if (attr.IsAttribute(KnownAttributes.GpuNoInline))
            {
                return "__noinline__";
            }

            if (attr.IsAttribute(KnownAttributes.MethodImpl))
            {
                var opt = ExtractMethodImplOptions(attr);
                if ((opt & MethodImplOptions.AggressiveInlining) != 0)
                {
                    return "__forceinline__";
                }

                if ((opt & MethodImplOptions.NoInlining) != 0)
                {
                    return "__noinline__";
                }
            }
        }

        return "inline";
    }

    private static MethodImplOptions ExtractMethodImplOptions(AttributeData attr)
    {
        if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is int or short)
        {
            return (MethodImplOptions)Convert.ToInt32(attr.ConstructorArguments[0].Value);
        }

        foreach (var namedArg in attr.NamedArguments)
        {
            if (namedArg is { Key: "Value", Value.Value: int or short })
            {
                return (MethodImplOptions)Convert.ToInt32(namedArg.Value.Value);
            }
        }

        return default;
    }

    private static void WriteKernelMethodPrefix(ref TranspileContext context)
    {
        // Auto-allocate feature: GpuKernel methods are now allowed to return values.
        context.Emitter.Writer.AppendKeyword(context.Emitter.Dialect.IsCuda
            ? "extern \"C\" __global__ void"
            : "extern \"C\" __declspec(dllexport) void");
    }
}
