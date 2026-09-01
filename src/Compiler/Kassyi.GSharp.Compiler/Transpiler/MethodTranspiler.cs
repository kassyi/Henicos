
using Kassyi.GSharp.Compiler.Dialects;

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>Transpiles C# methods and GPU kernels into CUDA C++ function definitions or forward signatures.</summary>
/// <remarks><para>Implements: <b>GEN-20</b></para></remarks>
public static class MethodTranspiler
{
    public static string GenerateCudaSource(MethodDeclarationSyntax methodDecl, SemanticModel semanticModel,
        Action<Diagnostic>? reportDiagnostic = null, bool isDevice = true,
        ITranspilerDialect? dialect = null)
    {
        using var mainWriter = new SourceWriter();
        using var bodyWriter = new SourceWriter();

        var bclIntrinsicMap = BclIntrinsicMapBuilder.GetOrBuildMap(semanticModel.Compilation);
        dialect ??= new GSharpCudaDialect();
        using var visitor = RoslynToCudaVisitor.Rent(dialect, semanticModel, reportDiagnostic, bclIntrinsicMap);
        ref var context = ref visitor.Context;

        context.Writer = bodyWriter;
        var methodSymbol = semanticModel.GetDeclaredSymbol(methodDecl);
        if (methodSymbol != null)
        {
            if (methodSymbol.HasAttribute(KnownAttributes.SkipLocalsInit) ||
                methodSymbol.ContainingType?.HasAttribute(KnownAttributes.SkipLocalsInit) == true ||
                methodSymbol.ContainingModule?.HasAttribute(KnownAttributes.SkipLocalsInit) == true)
            {
                context.SkipLocalsInit = true;
            }
        }

        WriteMethodSignature(methodDecl, isDevice, ref context);
        WriteMethodBody(methodDecl, ref context);

        context.Writer = mainWriter;
        var sortedStructs = StructTranspiler.GetTopologicallySortedStructs(ref context);
        foreach (var structSym in sortedStructs)
        {
            StructTranspiler.WriteStructDefinition(structSym, ref context);
        }

        return mainWriter + bodyWriter.ToString();
    }

    public static string GenerateCudaSignature(MethodDeclarationSyntax methodDecl, SemanticModel semanticModel,
        Action<Diagnostic>? reportDiagnostic = null, bool isDevice = true,
        ITranspilerDialect? dialect = null)
    {
        using var mainWriter = new SourceWriter();
        using var sigWriter = new SourceWriter();

        var bclIntrinsicMap = BclIntrinsicMapBuilder.GetOrBuildMap(semanticModel.Compilation);
        dialect ??= new GSharpCudaDialect();
        using var visitor = RoslynToCudaVisitor.Rent(dialect, semanticModel, reportDiagnostic, bclIntrinsicMap);
        ref var context = ref visitor.Context;

        context.Writer = sigWriter;
        WriteMethodSignature(methodDecl, isDevice, ref context);
        context.Writer.AppendSemiColonLine();

        context.Writer = mainWriter;
        foreach (var structSym in StructTranspiler.GetTopologicallySortedStructs(ref context))
        {
            context.Writer.Append("struct ").AppendFullyQualifiedCppName(structSym).AppendLine(";");
        }

        context.Writer.Append(sigWriter.ToString());
        return mainWriter.ToString();
    }

    private static void WriteMethodSignature(MethodDeclarationSyntax method, bool isDevice,
        ref TranspileContext context)
    {
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(method);

        if (isDevice)
        {
            WriteDeviceMethodPrefix(methodSymbol, ref context);
        }
        else
        {
            WriteKernelMethodPrefix(method, methodSymbol, ref context);
        }

        context.Writer.Append($" {method.Identifier.Text}");
        using (context.Writer.Parentheses())
        {
            WriteParameters(method.ParameterList.Parameters, isDevice, ref context);
        }
    }

    private static void WriteDeviceMethodPrefix(IMethodSymbol? methodSymbol, ref TranspileContext context)
    {
        var inlineKeyword = GetInliningKeyword(methodSymbol);
        
        if (context.Dialect.IsCuda)
        {
            context.Writer.Append($"{inlineKeyword} __device__ ");
        }
        else
        {
            context.Writer.Append($"{inlineKeyword} ");
        }

        var returnType = methodSymbol?.ReturnType;
        if (returnType is { SpecialType: not SpecialType.System_Void })
        {
            context.AppendType(returnType);
        }
        else
        {
            context.Writer.AppendKeyword("void");
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
                var options = ExtractMethodImplOptions(attr);
                if ((options & MethodImplOptions.AggressiveInlining) != 0)
                {
                    return "__forceinline__";
                }

                if ((options & MethodImplOptions.NoInlining) != 0)
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

    private static void WriteKernelMethodPrefix(MethodDeclarationSyntax method, IMethodSymbol? methodSymbol,
        ref TranspileContext context)
    {
        var returnType = methodSymbol?.ReturnType;
        if (returnType is { SpecialType: not SpecialType.System_Void })
        {
            context.ReportDiagnostic?.Invoke(Diagnostic.Create(GSharpDiagnostics.KernelReturnMustBeVoid,
                method.ReturnType.GetLocation(), method.Identifier.Text, returnType.ToDisplayString()));
        }

        if (context.Dialect.IsCuda)
        {
            context.Writer.AppendKeyword("extern \"C\" __global__ void");
        }
        else
        {
            context.Writer.AppendKeyword("extern \"C\" __declspec(dllexport) void");
        }
    }

    private static void WriteMethodBody(MethodDeclarationSyntax method, ref TranspileContext context)
    {
        using (context.Writer.Scope())
        {
            if (method.Body != null)
            {
                context.Visitor.Visit(method.Body);
            }
            else if (method.ExpressionBody != null)
            {
                var methodSymbol = context.SemanticModel.GetDeclaredSymbol(method);
                if (methodSymbol is { ReturnsVoid: false })
                {
                    context.Writer.Append("return ");
                }

                context.Visitor.Visit(method.ExpressionBody.Expression);
                context.Writer.AppendSemiColonLine();
            }
        }
    }

    private static void WriteParameters(SeparatedSyntaxList<ParameterSyntax> parameters, bool isDevice,
        ref TranspileContext context)
    {
        var validParameters = new List<ParameterSyntax>(parameters.Count);
        foreach (var p in parameters)
        {
            var paramSymbol = context.SemanticModel.GetDeclaredSymbol(p)?.HasAttribute(KnownAttributes.JitConstant) ==
                              true;
            if (!paramSymbol)
            {
                validParameters.Add(p);
            }
        }

        for (var i = 0; i < validParameters.Count; i++)
        {
            var p = validParameters[i];
            var typeSymbol = context.SemanticModel.GetTypeInfo(p.Type!).Type;

            StructTranspiler.RegisterUsedStructIfApplicable(typeSymbol, ref context);

            var paramSymbol = context.SemanticModel.GetDeclaredSymbol(p);
            if (paramSymbol != null && paramSymbol.HasAttribute(KnownAttributes.GpuConst))
            {
                context.Writer.Append("const ");
            }

            var (isRefPointer, isPointerType, hasRestrict) = context.AppendParameterCppType(p, typeSymbol);
            StructTranspiler.CheckLargeStructByValue(p, typeSymbol, isRefPointer, ref context);

            if (!isDevice && isPointerType && !hasRestrict)
            {
                context.Writer.Append(" __restrict__");
            }

            context.Writer.Append($" {p.Identifier.Text}");
            if (i < validParameters.Count - 1)
            {
                context.Writer.AppendComma();
            }
        }
    }
}
