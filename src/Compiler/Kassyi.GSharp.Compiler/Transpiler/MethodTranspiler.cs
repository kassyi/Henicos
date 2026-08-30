using System.Runtime.CompilerServices;

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>Transpiles C# methods and GPU kernels into CUDA C++ function definitions or forward signatures.</summary>
/// <remarks><para>Implements: <b>GEN-20</b></para></remarks>
public static class MethodTranspiler
{
    public static string GenerateCudaSource(MethodDeclarationSyntax method, SemanticModel semanticModel,
        Action<Diagnostic>? reportDiagnostic = null, bool isDevice = false)
    {
        using var mainWriter = new SourceWriter();
        using var bodyWriter = new SourceWriter();

        var bclIntrinsicMap = BclIntrinsicMapBuilder.GetOrBuildMap(semanticModel.Compilation);
        using var visitor = RoslynToCudaVisitor.Rent(semanticModel, reportDiagnostic, bclIntrinsicMap);
        ref var context = ref visitor.Context;

        context.Writer = bodyWriter;
        var methodSymbol = semanticModel.GetDeclaredSymbol(method);
        if (methodSymbol != null)
        {
            if (methodSymbol.GetAttributes().Any(a => a.AttributeClass?.Name == "SkipLocalsInitAttribute") ||
                methodSymbol.ContainingType?.GetAttributes()
                    .Any(a => a.AttributeClass?.Name == "SkipLocalsInitAttribute") == true ||
                methodSymbol.ContainingModule?.GetAttributes()
                    .Any(a => a.AttributeClass?.Name == "SkipLocalsInitAttribute") == true)
            {
                context.SkipLocalsInit = true;
            }
        }

        WriteMethodSignature(method, isDevice, ref context);
        WriteMethodBody(method, ref context);

        context.Writer = mainWriter;
        var sortedStructs = StructTranspiler.GetTopologicallySortedStructs(ref context);
        foreach (var structSym in sortedStructs)
        {
            StructTranspiler.WriteStructDefinition(structSym, ref context);
        }

        return mainWriter + bodyWriter.ToString();
    }

    public static string GenerateCudaSignature(MethodDeclarationSyntax method, SemanticModel semanticModel,
        Action<Diagnostic>? reportDiagnostic = null, bool isDevice = false)
    {
        using var mainWriter = new SourceWriter();
        using var sigWriter = new SourceWriter();

        var bclIntrinsicMap = BclIntrinsicMapBuilder.GetOrBuildMap(semanticModel.Compilation);
        using var visitor = RoslynToCudaVisitor.Rent(semanticModel, reportDiagnostic, bclIntrinsicMap);
        ref var context = ref visitor.Context;

        context.Writer = sigWriter;
        WriteMethodSignature(method, isDevice, ref context);
        context.Writer.Append(";");
        context.Writer.AppendLine();

        context.Writer = mainWriter;
        foreach (var structSym in StructTranspiler.GetTopologicallySortedStructs(ref context))
        {
            context.Writer.Append("struct ");
            context.Writer.AppendFullyQualifiedCppName(structSym);
            context.Writer.AppendLine(";");
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

        context.Writer.AppendSpace();
        context.Writer.Append(method.Identifier.Text);
        using (context.Writer.Parentheses())
        {
            WriteParameters(method.ParameterList.Parameters, isDevice, ref context);
        }
    }

    internal static void WriteDeviceMethodPrefix(IMethodSymbol? methodSymbol, ref TranspileContext context)
    {
        var hasAggressiveInlining = false;
        var hasNoInlining = false;

        if (methodSymbol != null)
        {
            foreach (var attr in methodSymbol.GetAttributes())
            {
                if (attr.IsAttribute(KnownAttributes.GpuInline))
                {
                    hasAggressiveInlining = true;
                }
                else if (attr.IsAttribute(KnownAttributes.GpuNoInline))
                {
                    hasNoInlining = true;
                }
                else if (attr.IsAttribute(KnownAttributes.MethodImpl) &&
                         attr.AttributeClass?.ContainingNamespace is
                         {
                             Name: "CompilerServices",
                             ContainingNamespace:
                             {
                                 Name: "Runtime",
                                 ContainingNamespace:
                                 {
                                     Name: "System", ContainingNamespace.IsGlobalNamespace: true
                                 }
                             }
                         })
                {
                    if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is int or short)
                    {
                        var options = (MethodImplOptions)Convert.ToInt32(attr.ConstructorArguments[0].Value);
                        if ((options & MethodImplOptions.AggressiveInlining) != 0)
                        {
                            hasAggressiveInlining = true;
                        }

                        if ((options & MethodImplOptions.NoInlining) != 0)
                        {
                            hasNoInlining = true;
                        }
                    }
                    else if (attr.NamedArguments.Length > 0)
                    {
                        foreach (var namedArg in attr.NamedArguments)
                        {
                            if (namedArg is not { Key: "Value", Value.Value: int or short })
                            {
                                continue;
                            }

                            var options = (MethodImplOptions)Convert.ToInt32(namedArg.Value.Value);
                            if ((options & MethodImplOptions.AggressiveInlining) != 0)
                            {
                                hasAggressiveInlining = true;
                            }

                            if ((options & MethodImplOptions.NoInlining) != 0)
                            {
                                hasNoInlining = true;
                            }
                        }
                    }
                }
            }
        }

        if (hasAggressiveInlining)
        {
            context.Writer.AppendKeyword("__forceinline__");
        }
        else if (hasNoInlining)
        {
            context.Writer.AppendKeyword("__noinline__");
        }
        else
        {
            context.Writer.AppendKeyword("inline");
        }

        context.Writer.AppendSpace();
        context.Writer.AppendKeyword("__device__");
        context.Writer.AppendSpace();

        var returnType = methodSymbol?.ReturnType;
        if (returnType is { SpecialType: not SpecialType.System_Void })
        {
            context.Writer.AppendType(returnType);
        }
        else
        {
            context.Writer.AppendKeyword("void");
        }
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

        context.Writer.AppendKeyword("extern \"C\" __global__ void");
    }

    internal static void WriteMethodBody(MethodDeclarationSyntax method, ref TranspileContext context)
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
                    context.Writer.AppendKeyword("return");
                    context.Writer.AppendSpace();
                }

                context.Visitor.Visit(method.ExpressionBody.Expression);
                context.Writer.AppendSemiColon();
                context.Writer.AppendLine();
            }
        }
    }

    internal static void WriteParameters(SeparatedSyntaxList<ParameterSyntax> parameters, bool isDevice,
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
                context.Writer.AppendKeyword("const");
                context.Writer.AppendSpace();
            }

            var (isRefPointer, isPointerType, hasRestrict) = context.Writer.AppendParameterCppType(p, typeSymbol);
            StructTranspiler.CheckLargeStructByValue(p, typeSymbol, isRefPointer, ref context);

            if (!isDevice && isPointerType && !hasRestrict)
            {
                context.Writer.AppendSpace();
                context.Writer.AppendKeyword("__restrict__");
            }

            context.Writer.AppendSpace();
            context.Writer.Append(p.Identifier.Text);
            if (i < validParameters.Count - 1)
            {
                context.Writer.AppendComma();
            }
        }
    }
}
