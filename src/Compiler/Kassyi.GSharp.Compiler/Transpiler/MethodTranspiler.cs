namespace Kassyi.GSharp.Compiler.Transpiler;

public static class MethodTranspiler
{
    public static string GenerateCudaSource(MethodDeclarationSyntax method, SemanticModel semanticModel,
        Action<Diagnostic>? reportDiagnostic = null, bool isDevice = false)
    {
        using var mainWriter = new SourceWriter();
        using var bodyWriter = new SourceWriter();

        var bclIntrinsicMap = BclIntrinsicMapBuilder.GetOrBuildMap(semanticModel.Compilation);
        using var visitor = RoslynToCudaVisitor.Rent(semanticModel, reportDiagnostic, bclIntrinsicMap);
        ref var context = ref visitor._context;

        context.Writer = bodyWriter;
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
        ref var context = ref visitor._context;

        context.Writer = sigWriter;
        WriteMethodSignature(method, isDevice, ref context);
        context.Writer.Append(";");
        context.Writer.AppendLine();

        context.Writer = mainWriter;
        foreach (var structSym in StructTranspiler.GetTopologicallySortedStructs(ref context))
        {
            var structName = CudaTypeMapper.GetFullyQualifiedCppName(structSym);
            context.Writer.AppendLine($"struct {structName};");
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
            WriteParameters(method.ParameterList.Parameters, ref context);
        }
    }

    private static void WriteDeviceMethodPrefix(IMethodSymbol? methodSymbol, ref TranspileContext context)
    {
        var hasAggressiveInlining = false;
        var hasNoInlining = false;

        if (methodSymbol != null)
        {
            foreach (var attr in methodSymbol.GetAttributes())
            {
                switch (attr.AttributeClass?.Name)
                {
                    case KnownAttributes.GpuInlineAttribute:
                        hasAggressiveInlining = true;
                        break;
                    case KnownAttributes.GpuNoInlineAttribute:
                        hasNoInlining = true;
                        break;
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
        if (returnType != null && returnType.SpecialType != SpecialType.System_Void)
        {
            context.Writer.Append(CudaTypeMapper.MapType(returnType));
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
        if (returnType != null && returnType.SpecialType != SpecialType.System_Void)
        {
            context.ReportDiagnostic?.Invoke(Diagnostic.Create(GSharpDiagnostics.KernelReturnMustBeVoid,
                method.ReturnType.GetLocation(), method.Identifier.Text, returnType.ToDisplayString()));
        }

        context.Writer.AppendKeyword("extern \"C\" __global__ void");
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
                    context.Writer.AppendKeyword("return");
                    context.Writer.AppendSpace();
                }

                context.Visitor.Visit(method.ExpressionBody.Expression);
                context.Writer.AppendSemiColon();
                context.Writer.AppendLine();
            }
        }
    }

    private static void WriteParameters(SeparatedSyntaxList<ParameterSyntax> parameters, ref TranspileContext context)
    {
        var validParameters = new List<ParameterSyntax>(parameters.Count);
        foreach (var p in parameters)
        {
            if (!IsJitConstant(p, ref context))
            {
                validParameters.Add(p);
            }
        }

        for (var i = 0; i < validParameters.Count; i++)
        {
            var p = validParameters[i];
            var typeSymbol = context.SemanticModel.GetTypeInfo(p.Type!).Type;

            StructTranspiler.RegisterUsedStructIfApplicable(typeSymbol, ref context);
            var (cppType, isPointer) = CudaTypeMapper.GetParameterCppType(p, typeSymbol);
            StructTranspiler.CheckLargeStructByValue(p, typeSymbol, isPointer, ref context);

            var paramSymbol = context.SemanticModel.GetDeclaredSymbol(p);
            if (paramSymbol != null &&
                paramSymbol.GetAttributes().Any(a => KnownAttributes.IsGpuConst(a.AttributeClass?.Name)))
            {
                context.Writer.AppendKeyword("const");
                context.Writer.AppendSpace();
            }

            context.Writer.Append(cppType);
            context.Writer.AppendSpace();
            context.Writer.Append(p.Identifier.Text);
            if (i < validParameters.Count - 1)
            {
                context.Writer.AppendComma();
            }
        }
    }

    private static bool IsJitConstant(ParameterSyntax p, ref TranspileContext context)
    {
        var paramSymbol = context.SemanticModel.GetDeclaredSymbol(p);
        if (paramSymbol == null)
        {
            return false;
        }

        foreach (var attr in paramSymbol.GetAttributes())
        {
            if (KnownAttributes.IsJitConstant(attr.AttributeClass?.Name))
            {
                return true;
            }
        }

        return false;
    }
}
