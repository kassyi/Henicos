
namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Provides code emission services for method signatures, parameter qualifiers, and target-specific function prefixes.
/// </summary>
internal static class MethodSignatureTranspiler
{
    /// <summary>The exported entry point of a kernel (see <see cref="GlobalCppNames.GetKernelName"/>).</summary>
    public static string GetKernelMethodName(MethodDeclarationSyntax method, IMethodSymbol? methodSymbol) =>
        methodSymbol != null ? GlobalCppNames.GetKernelName(methodSymbol) : method.Identifier.ValueText;

    /// <summary>
    /// A name that belongs to one kernel: <paramref name="prefix"/> followed by the body of its entry point name.
    /// </summary>
    public static string GetKernelDerivedName(string prefix, MethodDeclarationSyntax method, IMethodSymbol? methodSymbol) =>
        methodSymbol != null
            ? GlobalCppNames.GetKernelDerivedName(prefix, methodSymbol)
            : prefix + method.Identifier.ValueText;

    public static void WriteMethodSignature(MethodDeclarationSyntax method, bool isDevice, bool hasContext,
        ref TranspileContext context)
    {
        var methodSymbol = context.Query.SemanticModel.GetDeclaredSymbol<IMethodSymbol>(method);
        ReportUnsupportedParameterTypes(methodSymbol, ref context);
        ReportKernelParameterSpace(methodSymbol, method, isDevice, ref context);

        if (isDevice)
        {
            WriteDeviceMethodPrefix(methodSymbol, ref context);
        }
        else
        {
            WriteKernelMethodPrefix(ref context);
        }

        var methodName = !isDevice ? GetKernelMethodName(method, methodSymbol)
            : methodSymbol != null && CppNameResolver.IsEmittedDeviceFunction(methodSymbol, context.Emitter.Dialect)
                ? GlobalCppNames.GetFunctionName(methodSymbol)
                : method.Identifier.Text;

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

                context.Emitter.Writer
                    .Append(GetKernelDerivedName(GSharpInternalNames.KernelContextPrefix, method, methodSymbol))
                    .Append("* ").Append(GSharpInternalNames.Context);
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

    private const int KernelParameterSpaceLimitBytes = 4096;

    private const int DevicePointerBytes = 8;

    // [WHY] CUDA limits total parameter space for kernel arguments to 4 KB (4096 bytes).
    private static void ReportKernelParameterSpace(IMethodSymbol? methodSymbol, MethodDeclarationSyntax method, bool isDevice, ref TranspileContext context)
    {
        if (isDevice || methodSymbol is null || !methodSymbol.HasAttribute(KnownAttributes.GpuKernel))
        {
            return;
        }

        var offset = 0;
        foreach (var parameter in methodSymbol.Parameters)
        {
            var layout = parameter.RefKind != RefKind.None
                ? new CudaLayoutRules.CudaLayoutInfo(DevicePointerBytes, DevicePointerBytes)
                : CudaLayoutRules.GetCudaLayout(parameter.Type.UnwrapNullableArrayLike() ?? parameter.Type);

            offset = CudaLayoutRules.AlignUp(offset, layout.Alignment) + layout.Size;
        }

        if (methodSymbol.ReturnType.SpecialType != SpecialType.System_Void)
        {
            offset = CudaLayoutRules.AlignUp(offset, DevicePointerBytes) + DevicePointerBytes;
        }

        if (offset > KernelParameterSpaceLimitBytes)
        {
            // The message names the kernel as the user wrote it, not by its entry point.
            var kernelName = $"{methodSymbol.ContainingType.Name}.{methodSymbol.Name}";
            context.Reporter.Report(GSharpDiagnostics.KernelParameterSpaceExceeded, method.Identifier.GetLocation(), kernelName, offset, KernelParameterSpaceLimitBytes);
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

            if (!isDevice && isPointerType && !hasRestrict)
            {
                context.Emitter.Writer.Append(" __restrict__");
            }

            context.Emitter.Writer.AppendSpace().AppendCppName(
                CppNameResolver.Resolve(paramSymbol, p.Identifier.ValueText, context.Emitter.Dialect));
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
