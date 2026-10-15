
namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Provides code emission services for method signatures, parameter qualifiers, and target-specific function prefixes.
/// </summary>
internal static class MethodSignatureTranspiler
{
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

        var methodName = method.Identifier.Text;
        if (!isDevice && methodSymbol != null)
        {
            methodName = $"{methodSymbol.ContainingType.Name}_{methodName}";
        }

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

                context.Emitter.Writer.Append($"__gsharp_context_{methodName}* _ctx");
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
                parameter.DeclaringSyntaxReferences.IsEmpty ||
                parameter.DeclaringSyntaxReferences[0].GetSyntax() is not ParameterSyntax { Type: { } typeSyntax })
            {
                continue;
            }

            context.Reporter.Report(GSharpDiagnostics.UnsupportedSyntax, typeSyntax, typeSyntax.Kind().ToString());
        }
    }

    public static void WriteParameters(SeparatedSyntaxList<ParameterSyntax> parameters, bool isDevice,
        ref TranspileContext context)
    {
        for (var i = 0; i < parameters.Count; i++)
        {
            var p = parameters[i];
            if (p.Type == null)
            {
                continue;
            }

            var paramSemanticModel = context.Query.SemanticModel.Compilation.GetSemanticModel(p.SyntaxTree);
            var typeSymbol = paramSemanticModel.GetTypeInfo(p.Type!).Type;

            StructTranspiler.RegisterUsedStructIfApplicable(typeSymbol, ref context);

            var paramSymbol = paramSemanticModel.GetDeclaredSymbol(p) as IParameterSymbol;
            var isReadOnly = paramSymbol != null && paramSymbol.HasAttribute(KnownAttributes.GpuReadOnly);
            var isArray = typeSymbol != null && KernelLaunchEmitter.IsArrayLikeType(typeSymbol, out _);

            if (isReadOnly && !isArray)
            {
                context.Emitter.Writer.Append("const ");
            }

            var (isRefPointer, isPointerType, hasRestrict) =
                context.Emitter.Dialect.AppendParameter(ref context, p, typeSymbol);
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
            var keyword = attr.AttributeClass?.Name switch
            {
                KnownAttributes.GpuInline or KnownAttributes.GpuInline + "Attribute" => "__forceinline__",
                KnownAttributes.GpuNoInline or KnownAttributes.GpuNoInline + "Attribute" => "__noinline__",
                KnownAttributes.MethodImpl or KnownAttributes.MethodImpl + "Attribute" => ExtractMethodImplOptions(attr)
                    switch
                    {
                        var opt when (opt & MethodImplOptions.AggressiveInlining) != 0 => "__forceinline__",
                        var opt when (opt & MethodImplOptions.NoInlining) != 0 => "__noinline__",
                        _ => null
                    },
                _ => null
            };

            if (keyword != null)
            {
                return keyword;
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
