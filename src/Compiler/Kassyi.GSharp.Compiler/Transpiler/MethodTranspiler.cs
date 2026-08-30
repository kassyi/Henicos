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
        WriteMethodBody(method, ref context, mainWriter);

        context.Writer = mainWriter;
        var sortedStructs = StructTranspiler.GetTopologicallySortedStructs(ref context);
        foreach (var structSym in sortedStructs)
        {
            StructTranspiler.WriteStructDefinition(structSym, ref context);
        }

        return mainWriter.ToString() + bodyWriter.ToString();
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

        var methodSymbol = semanticModel.GetDeclaredSymbol(method);
        if (methodSymbol != null)
        {
            var returnType = methodSymbol.ReturnType;
            if (returnType.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>")
            {
                var stateMachineName = $"{method.Identifier.Text}_StateMachine";
                var fwd = $"struct {stateMachineName};\n";
                return mainWriter.ToString() + fwd + sigWriter.ToString();
            }
        }
        return mainWriter.ToString() + sigWriter.ToString();
    }

    private static void WriteMethodSignature(MethodDeclarationSyntax method, bool isDevice,
        ref TranspileContext context)
    {
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(method);
        
        var returnType = methodSymbol?.ReturnType;
        bool isIterator = returnType != null && returnType.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>";

        if (isIterator)
        {
            if (isDevice)
            {
                WriteDeviceMethodPrefix(methodSymbol, ref context, $"{method.Identifier.Text}_StateMachine");
            }
            else
            {
                context.ReportDiagnostic?.Invoke(Diagnostic.Create(GSharpDiagnostics.KernelReturnMustBeVoid,
                    method.ReturnType.GetLocation(), method.Identifier.Text, returnType.ToDisplayString()));
            }

            context.Writer.AppendSpace();
            context.Writer.Append(method.Identifier.Text);
            using (context.Writer.Parentheses())
            {
                WriteParameters(method.ParameterList.Parameters, isDevice, ref context);
            }
            return;
        }

        if (isDevice)
        {
            WriteDeviceMethodPrefix(methodSymbol, ref context, null);
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

    private static void WriteDeviceMethodPrefix(IMethodSymbol? methodSymbol, ref TranspileContext context, string? overrideReturnType = null)
    {
        AnalyzeInliningAttributes(methodSymbol, out var hasAggressiveInlining, out var hasNoInlining);

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

        if (overrideReturnType != null)
        {
            context.Writer.Append(overrideReturnType);
        }
        else
        {
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
    }

    private static void AnalyzeInliningAttributes(IMethodSymbol? methodSymbol, out bool hasAggressiveInlining, out bool hasNoInlining)
    {
        hasAggressiveInlining = false;
        hasNoInlining = false;

        if (methodSymbol == null) return;

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
            else if (IsMethodImplAttribute(attr))
            {
                AnalyzeMethodImplAttribute(attr, ref hasAggressiveInlining, ref hasNoInlining);
            }
        }
    }

    private static bool IsMethodImplAttribute(AttributeData attr)
    {
        return attr.IsAttribute(KnownAttributes.MethodImpl) &&
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
               };
    }

    private static void AnalyzeMethodImplAttribute(AttributeData attr, ref bool hasAggressiveInlining, ref bool hasNoInlining)
    {
        if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is int or short)
        {
            var options = (MethodImplOptions)Convert.ToInt32(attr.ConstructorArguments[0].Value);
            hasAggressiveInlining |= (options & MethodImplOptions.AggressiveInlining) != 0;
            hasNoInlining |= (options & MethodImplOptions.NoInlining) != 0;
        }
        else if (attr.NamedArguments.Length > 0)
        {
            foreach (var namedArg in attr.NamedArguments)
            {
                if (namedArg.Key == "Value" && namedArg.Value.Value is int or short)
                {
                    var options = (MethodImplOptions)Convert.ToInt32(namedArg.Value.Value);
                    hasAggressiveInlining |= (options & MethodImplOptions.AggressiveInlining) != 0;
                    hasNoInlining |= (options & MethodImplOptions.NoInlining) != 0;
                }
            }
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

    internal static void WriteMethodBody(MethodDeclarationSyntax method, ref TranspileContext context, SourceWriter? mainWriter = null)
    {
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(method);
        var returnType = methodSymbol?.ReturnType;
        bool isIterator = returnType != null && returnType.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>";

        if (isIterator && mainWriter != null)
        {
            bool isDevice = methodSymbol.HasAttribute(KnownAttributes.GpuDeviceFunction);
            WriteIteratorStateMachine(method, methodSymbol!, isDevice, ref context, mainWriter.Value);
            return;
        }

        using (context.Writer.Scope())
        {
            if (method.Body != null)
            {
                context.Visitor.Visit(method.Body);
            }
            else if (method.ExpressionBody != null)
            {
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

    private static void WriteIteratorStateMachine(MethodDeclarationSyntax method, IMethodSymbol methodSymbol, bool isDevice, ref TranspileContext context, SourceWriter mainWriter)
    {
        var stateMachineName = $"{method.Identifier.Text}_StateMachine";
        var elementType = ((INamedTypeSymbol)methodSymbol.ReturnType).TypeArguments[0];

        var originalWriter = context.Writer;
        var structWriter = mainWriter;
        
        WriteStateMachineStructDefinition(structWriter, method, methodSymbol, stateMachineName, elementType, ref context);
        
        context.Writer = originalWriter;

        structWriter.AppendSemiColon();
        structWriter.AppendLine();

        context.Writer.AppendLine();
        using (context.Writer.Scope())
        {
            context.Writer.Append($"return {stateMachineName}(");
            for (int i = 0; i < method.ParameterList.Parameters.Count; i++)
            {
                context.Writer.Append(method.ParameterList.Parameters[i].Identifier.Text);
                if (i < method.ParameterList.Parameters.Count - 1) context.Writer.Append(", ");
            }
            context.Writer.AppendLine(");");
        }
    }

    private static void WriteStateMachineStructDefinition(SourceWriter structWriter, MethodDeclarationSyntax method, IMethodSymbol methodSymbol, string stateMachineName, ITypeSymbol elementType, ref TranspileContext context)
    {
        structWriter.Append($"struct {stateMachineName}");
        structWriter.AppendLine();
        using (structWriter.Scope())
        {
            structWriter.AppendLine("int _state;");
            structWriter.AppendType(elementType);
            structWriter.AppendLine(" _current;");
            
            var parameters = method.ParameterList.Parameters;
            foreach (var p in parameters)
            {
                var pSymbol = context.SemanticModel.GetDeclaredSymbol(p);
                if (pSymbol != null)
                {
                    structWriter.AppendType(pSymbol.Type);
                    structWriter.AppendSpace();
                    structWriter.Append(p.Identifier.Text);
                    structWriter.AppendLine(";");
                }
            }

            var hoistVisitor = new VariableHoistingVisitor(context.SemanticModel);
            if (method.Body != null)
            {
                hoistVisitor.Visit(method.Body);
            }
            else if (method.ExpressionBody != null)
            {
                hoistVisitor.Visit(method.ExpressionBody);
            }

            foreach (var v in hoistVisitor.Variables)
            {
                structWriter.AppendType(v.Type);
                structWriter.AppendSpace();
                structWriter.Append(v.Name);
                structWriter.AppendLine(";");
            }

            WriteStateMachineConstructor(structWriter, stateMachineName, parameters, ref context);
            WriteStateMachineMoveNext(structWriter, method, hoistVisitor, ref context);
            WriteStateMachineDispose(structWriter, ref context);

            structWriter.AppendLine();
            structWriter.AppendType(elementType);
            structWriter.AppendLine(" __device__ __forceinline__ get_Current() const { return _current; }");
            structWriter.AppendLine($"__device__ __forceinline__ {stateMachineName} GetEnumerator() {{ return *this; }}");
        }
    }

    private static void WriteStateMachineConstructor(SourceWriter structWriter, string stateMachineName, SeparatedSyntaxList<ParameterSyntax> parameters, ref TranspileContext context)
    {
        structWriter.AppendLine();
        structWriter.Append($"__device__ __forceinline__ {stateMachineName}(");
        for (int i = 0; i < parameters.Count; i++)
        {
            var p = parameters[i];
            var pSymbol = context.SemanticModel.GetDeclaredSymbol(p);
            structWriter.AppendType(pSymbol!.Type);
            structWriter.Append($" {p.Identifier.Text}");
            if (i < parameters.Count - 1) structWriter.Append(", ");
        }
        structWriter.Append(") : _state(0)");
        foreach (var p in parameters)
        {
            structWriter.Append($", {p.Identifier.Text}({p.Identifier.Text})");
        }
        structWriter.AppendLine(" {}");
        structWriter.AppendLine();
    }

    private static void WriteStateMachineMoveNext(SourceWriter structWriter, MethodDeclarationSyntax method, VariableHoistingVisitor hoistVisitor, ref TranspileContext context)
    {
        structWriter.AppendLine("__device__ __forceinline__ bool MoveNext()");
        using (structWriter.Scope())
        {
            structWriter.AppendLine("switch (_state) {");
            structWriter.AppendLine("    case 0: goto STATE_0;");
            for (int i = 1; i <= hoistVisitor.YieldCount; i++)
            {
                structWriter.AppendLine($"    case {i}: goto STATE_{i};");
            }
            structWriter.AppendLine("}");
            structWriter.AppendLine("STATE_0:;");

            context.Writer = structWriter;
            context.IsStateMachineContext = true;
            context.StateMachineStateCount = 1;
            context.ForEachCounter = 0;
            context.StateMachineDisposeRoutes = new();

            if (method.Body != null)
            {
                foreach (var stmt in method.Body.Statements)
                {
                    context.Visitor.Visit(stmt);
                }
            }

            context.IsStateMachineContext = false;
            
            structWriter.AppendLine("_state = -1;");
            structWriter.AppendLine("return false;");
        }
    }

    private static void WriteStateMachineDispose(SourceWriter structWriter, ref TranspileContext context)
    {
        structWriter.AppendLine("__device__ __forceinline__ void Dispose()");
        using (structWriter.Scope())
        {
            if (context.StateMachineDisposeRoutes == null || context.StateMachineDisposeRoutes.Count == 0)
            {
                return;
            }

            structWriter.AppendLine("switch (_state) {");
            for (int i = 1; i < context.StateMachineStateCount; i++)
            {
                var activeEnums = context.StateMachineDisposeRoutes
                    .Where(r => r.StartState <= i && i <= r.EndState)
                    .OrderByDescending(r => r.StartState)
                    .ToList();

                if (activeEnums.Count > 0)
                {
                    structWriter.AppendLine($"    case {i}:");
                    foreach (var route in activeEnums)
                    {
                        structWriter.AppendLine($"        __enum_{route.ForeachId}.Dispose();");
                    }
                    structWriter.AppendLine("        break;");
                }
            }
            structWriter.AppendLine("}");
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
