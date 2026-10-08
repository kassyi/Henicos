using Kassyi.GSharp.Compiler.Dialects;
using Kassyi.GSharp.Compiler.Lowering;
using Kassyi.GSharp.Compiler.Transpiler.Helpers;

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Provides high-level compilation services to transpile C# method and GPU kernel declarations into CUDA C++ source definitions or forward declarations.
/// </summary>
/// <remarks>
/// <para><b>Performance:</b> Utilizes pooled syntax walkers and string builders to achieve near zero-allocation transpilation.</para>
/// <para><b>Thread Safety:</b> All static methods in this class are thread-safe and can be invoked concurrently across multiple compilation workers.</para>
/// <para><b>Execution Target:</b> Host-only (Roslyn compiler-time execution). Cannot be invoked within GPU kernels.</para>
/// </remarks>
public static class MethodTranspiler
{
    /// <summary>
    /// Transpiles a C# method declaration into a complete target dialect (CUDA C++ / host C++) implementation string,
    /// including dependent struct definitions, parameter qualifiers, and the method body.
    /// </summary>
    /// <param name="methodDecl">The Roslyn method declaration syntax node to transpile.</param>
    /// <param name="semanticModel">The semantic model bound to the compilation containing the method.</param>
    /// <param name="reportDiagnostic">An optional callback delegate to receive emitted compiler diagnostics and warnings.</param>
    /// <param name="isDevice">
    /// <see langword="true"/> to emit a device-side function (<c>__device__</c>); 
    /// <see langword="false"/> to emit a kernel entry point (<c>extern "C" __global__</c>).
    /// </param>
    /// <param name="dialect">
    /// The target language dialect to use for code generation. If <see langword="null"/>, defaults to <see cref="GSharpCudaDialect"/>.
    /// </param>
    /// <returns>
    /// A string containing the complete emitted source code, including topologically sorted struct dependencies and the function definition.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="methodDecl"/> or <paramref name="semanticModel"/> is <see langword="null"/>.
    /// </exception>
    public static string GenerateCudaSource(MethodDeclarationSyntax methodDecl, SemanticModel semanticModel,
        Action<Diagnostic>? reportDiagnostic = null, bool isDevice = true,
        ITranspilerDialect? dialect = null)
    {
        using var session = TranspilationSession.Begin(methodDecl, semanticModel, reportDiagnostic, dialect);
        if (session.HasError)
        {
            return session.ErrorMessage!;
        }

        ref var context = ref session.Context;

        IReadOnlyList<VariableDeclaratorSyntax>? contextFields = null;
        BlockSyntax? loweredBody = null;

        if (!isDevice && !context.Emitter.Dialect.IsCuda && context.Emitter.IsGpuKernelEntry && methodDecl.Body != null)
        {
            var tuple = AstLoweringPipeline.Lower(methodDecl.Body!);
            loweredBody = tuple.LoweredBody;
            contextFields = tuple.ContextFields;
        }

        using var mainWriter = new SourceWriter();
        using var bodyWriter = new SourceWriter();

        context.Emitter.Writer = bodyWriter;

        if (contextFields != null)
        {
            var methodName = methodDecl.Identifier.Text;
            var methodSymbol = context.Query.SemanticModel.GetDeclaredSymbol<IMethodSymbol>(methodDecl);
            if (methodSymbol != null)
            {
                methodName = $"{methodSymbol.ContainingType.Name}_{methodName}";
            }

            WriteContextStruct(methodName, contextFields, ref context);
        }

        WriteMethodSignature(methodDecl, isDevice, contextFields != null, ref context);
        WriteMethodBody(methodDecl, loweredBody, ref context);

        if (!isDevice && !context.Emitter.Dialect.IsCuda)
        {
            WriteCpuLauncher(methodDecl, contextFields, ref context);
        }

        context.Emitter.Writer = mainWriter;
        var sortedStructs = StructTranspiler.GetTopologicallySortedStructs(ref context);
        foreach (var structSym in sortedStructs)
        {
            StructTranspiler.WriteStructDefinition(structSym, ref context);
        }

        return mainWriter + bodyWriter.ToString();
    }

    private static void WriteContextStruct(string methodName, IReadOnlyList<VariableDeclaratorSyntax> fields,
        ref TranspileContext context)
    {
        context.Emitter.Writer.AppendLine($"struct __gsharp_context_{methodName} {{");
        context.Emitter.Writer.AppendLine("int _state = 0;");
        context.Emitter.Writer.AppendLine($"__gsharp_context_{methodName}* _all_contexts = nullptr;");
        context.Emitter.Writer.AppendLine("uint64_t _warpExchange = 0;");
        context.Emitter.Writer.AppendLine("bool _yieldedForGridSync = false;");
        foreach (var field in fields)
        {
            var typeSyntax = (field.Parent as VariableDeclarationSyntax)?.Type;
            if (typeSyntax == null)
            {
                continue;
            }

            var typeSymbol = context.Query.SemanticModel.GetTypeSymbol(typeSyntax);
            switch (typeSymbol)
            {
                case null when typeSyntax.IsVar:
                    // Fallback for dynamically generated AST nodes (e.g., _warTemp_X)
                    // Since they are not in the semantic model, default to 'int' for C++ (which allows implicit cast).
                    context.Emitter.Writer.Append("int");
                    context.Emitter.Writer.Append($" {field.Identifier.ValueText};");
                    context.Emitter.Writer.AppendLine();
                    continue;
                case null:
                    continue;
            }

            StructTranspiler.RegisterUsedStructIfApplicable(typeSymbol, ref context);
            context.Emitter.Dialect.AppendType(ref context, typeSymbol);
            context.Emitter.Writer.Append($" {field.Identifier.ValueText};");
            context.Emitter.Writer.AppendLine();
        }

        context.Emitter.Writer.AppendLine("};");
        context.Emitter.Writer.AppendLine();
    }

    /// <summary>
    /// Transpiles a C# method declaration into a pure C# managed state machine and wrapper for host-side debugging.
    /// </summary>
#pragma warning disable RS0030
    public static string GenerateManagedCSharpSource(MethodDeclarationSyntax methodDecl, SemanticModel semanticModel)
    {
        if (methodDecl.Body == null)
        {
            return string.Empty;
        }

        var tuple = AstLoweringPipeline.Lower(methodDecl.Body);
        var loweredBody = tuple.LoweredBody;
        var contextFields = tuple.ContextFields;

        var methodName = methodDecl.Identifier.Text;
        var methodSymbol = semanticModel.GetDeclaredSymbol<IMethodSymbol>(methodDecl);
        if (methodSymbol != null)
        {
            methodName = $"{methodSymbol.ContainingType.Name}_{methodName}";
        }

        using var writer = new SourceWriter();

        // Replace "_ctx->" with "_ctx." for C# managed execution
        var rewriter = new ManagedExecutionRewriter();
        loweredBody = (BlockSyntax)rewriter.Visit(loweredBody);

        var bodySource = loweredBody.NormalizeWhitespace().ToFullString().Replace("_ctx->", "_ctx.");

        var localInitBuilder = new StringBuilder();

        // Output context struct
        writer.AppendLine($"public class __gsharp_context_{methodName} {{");
        writer.AppendLine("public int _state;");
        writer.AppendLine("public uint _warpExchange;");
        writer.AppendLine("public bool _yieldedForGridSync;");
        if (contextFields != null)
        {
            foreach (var field in contextFields)
            {
                var typeSyntax = (field.Parent as VariableDeclarationSyntax)?.Type;
                if (typeSyntax == null)
                {
                    continue;
                }

                var typeString = typeSyntax.ToFullString().Trim();
                var initString = field.Initializer?.Value.ToFullString() ?? "";

                if (typeSyntax.IsVar || typeString == "var")
                {
                    try
                    {
                        var typeInfo = semanticModel.GetTypeInfo(typeSyntax);
                        if (typeInfo.Type != null && typeInfo.Type.TypeKind != TypeKind.Error)
                        {
                            typeString = typeInfo.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        }
                        else
                        {
                            // Fallback: If we couldn't resolve the type but the initializer looks like a ref struct
                            if (initString.Contains("SharedMemory") || initString.Contains("Span"))
                            {
                                typeString = "var";
                            }
                            else
                            {
                                typeString = "dynamic";
                            }
                        }
                    }
                    catch (ArgumentException)
                    {
                        // Syntax node is not within syntax tree
                        if (initString.Contains("SharedMemory") || initString.Contains("Span"))
                        {
                            typeString = "var";
                        }
                        else
                        {
                            typeString = "dynamic";
                        }
                    }
                }

                if (typeString.Contains("SharedMemory") || typeString.Contains("Span<") ||
                    initString.Contains("SharedMemory") || initString.Contains("Span"))
                {
                    // Un-hoist ref structs
                    bodySource = bodySource.Replace($"_ctx.{field.Identifier.ValueText}", field.Identifier.ValueText);

                    var init = field.Initializer != null ? field.Initializer.ToFullString() : "";
                    localInitBuilder.AppendLine(
                        $"        {(typeString == "int" ? "var" : typeString)} {field.Identifier.ValueText} {init};");

                    // Remove the assignment from the body to avoid double allocation (which consumes an extra Allocation Index)
                    if (!string.IsNullOrWhiteSpace(init))
                    {
                        bodySource = bodySource.Replace($"{field.Identifier.ValueText} {init};", "");
                        bodySource = bodySource.Replace($"{field.Identifier.ValueText}{init};", ""); // Without space
                    }
                }
                else
                {
                    writer.AppendLine($"public {typeString} {field.Identifier.ValueText};");
                }
            }
        }

        writer.AppendLine("}");
        writer.AppendLine();

        // Output the state machine function
        writer.Append($"public static unsafe void __gsharp_statemachine_{methodName}(");
        if (methodSymbol != null)
        {
            bool isFirst = true;
            foreach (var p in methodSymbol.Parameters)
            {
                if (!isFirst)
                {
                    writer.Append(", ");
                }

                isFirst = false;
                var refKind = p.RefKind switch
                {
                    RefKind.Ref => "ref ",
                    RefKind.Out => "out ",
                    RefKind.In => "in ",
                    _ => ""
                };
                writer.Append($"{refKind}{p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} {p.Name}");
            }

            if (!isFirst)
            {
                writer.Append(", ");
            }

            writer.Append($"__gsharp_context_{methodName} _ctx");

            if (methodSymbol.ReturnType.SpecialType != SpecialType.System_Void)
            {
                writer.Append(
                    $", ref {methodSymbol.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} __gsharp_auto_result");
            }
        }

        writer.AppendLine(") {");
        if (methodSymbol != null)
        {
            foreach (var p in methodSymbol.Parameters)
            {
                if (p.RefKind == RefKind.Out)
                {
                    writer.AppendLine($"        {p.Name} = default;");
                }
            }
        }

        writer.Append(localInitBuilder.ToString());
        writer.AppendLine(bodySource);
        writer.AppendLine("}");

        return writer.ToString();
    }

    private class ManagedExecutionRewriter : CSharpSyntaxRewriter
    {
        private int _inLambdaOrLocalFuncCount;

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            if (node.Identifier.Text == "_state" || node.Identifier.Text == "_warpExchange" ||
                node.Identifier.Text == "_yieldedForGridSync")
            {
                if (node.Parent is MemberAccessExpressionSyntax parent && parent.Name == node)
                {
                    return base.VisitIdentifierName(node);
                }

                return SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName("_ctx"),
                    node).WithTriviaFrom(node);
            }

            if (node.Identifier.Text == "BitOperations")
            {
                if (node.Parent is MemberAccessExpressionSyntax parent && parent.Name == node)
                {
                    // It's the right side of a dot (e.g. System.Numerics.BitOperations), do not replace to avoid InvalidCastException
                    return base.VisitIdentifierName(node);
                }

                return SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName("System.Numerics"),
                    SyntaxFactory.IdentifierName("BitOperations")).WithTriviaFrom(node);
            }

            return base.VisitIdentifierName(node);
        }

        public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        {
            _inLambdaOrLocalFuncCount++;
            var result = base.VisitLocalFunctionStatement(node);
            _inLambdaOrLocalFuncCount--;
            return result;
        }

        public override SyntaxNode? VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
        {
            _inLambdaOrLocalFuncCount++;
            var result = base.VisitSimpleLambdaExpression(node);
            _inLambdaOrLocalFuncCount--;
            return result;
        }

        public override SyntaxNode? VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
        {
            _inLambdaOrLocalFuncCount++;
            var result = base.VisitParenthesizedLambdaExpression(node);
            _inLambdaOrLocalFuncCount--;
            return result;
        }

        public override SyntaxNode? VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node)
        {
            _inLambdaOrLocalFuncCount++;
            var result = base.VisitAnonymousMethodExpression(node);
            _inLambdaOrLocalFuncCount--;
            return result;
        }

        public override SyntaxNode? VisitExpressionStatement(ExpressionStatementSyntax node)
        {
            if (node.Expression is not InvocationExpressionSyntax invocation ||
                invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
                memberAccess.Name.Identifier.Text != "GridStrideLoop" ||
                invocation.ArgumentList.Arguments.Count != 2)
            {
                return base.VisitExpressionStatement(node);
            }

            var countArg = invocation.ArgumentList.Arguments[0].Expression;
            if (invocation.ArgumentList.Arguments[1].Expression is not SimpleLambdaExpressionSyntax lambda)
            {
                return base.VisitExpressionStatement(node);
            }

            _inLambdaOrLocalFuncCount++;
            var loopVar = lambda.Parameter.Identifier.Text;

            var startExpr = SyntaxFactory.ParseExpression(
                "global::Kassyi.GSharp.Core.ManagedGpuContext.BlockDimX * global::Kassyi.GSharp.Core.ManagedGpuContext.BlockIdxX +" +
                " global::Kassyi.GSharp.Core.ManagedGpuContext.ThreadIdxX");
            var stepExpr = SyntaxFactory.ParseExpression(
                "global::Kassyi.GSharp.Core.ManagedGpuContext.BlockDimX * global::Kassyi.GSharp.Core.ManagedGpuContext.GridDimX");
            var condition = SyntaxFactory.ParseExpression($"{loopVar} < ({countArg})");

            var declaration = SyntaxFactory.VariableDeclaration(
                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.IntKeyword)),
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.VariableDeclarator(lambda.Parameter.Identifier)
                        .WithInitializer(SyntaxFactory.EqualsValueClause(startExpr))
                )
            );

            var incrementors = SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(
                SyntaxFactory.AssignmentExpression(
                    SyntaxKind.AddAssignmentExpression,
                    SyntaxFactory.IdentifierName(loopVar),
                    stepExpr
                )
            );

            var body = (BlockSyntax)base.Visit(lambda.Block!);
            _inLambdaOrLocalFuncCount--;

            return SyntaxFactory.ForStatement(
                declaration,
                SyntaxFactory.SeparatedList<ExpressionSyntax>(),
                condition,
                incrementors,
                body
            ).WithTriviaFrom(node);
        }

        public override SyntaxNode? VisitReturnStatement(ReturnStatementSyntax node)
        {
            if (_inLambdaOrLocalFuncCount == 0 && !node.HasAnnotations("StateMachineReturn"))
            {
                var statements = new List<StatementSyntax>();

                if (node.Expression != null)
                {
                    statements.Add(SyntaxFactory.ExpressionStatement(
                        SyntaxFactory.AssignmentExpression(
                            SyntaxKind.SimpleAssignmentExpression,
                            SyntaxFactory.IdentifierName("__gsharp_auto_result"),
                            (ExpressionSyntax)Visit(node.Expression)!
                        )
                    ));
                }

                statements.Add(SyntaxFactory.ExpressionStatement(
                    SyntaxFactory.AssignmentExpression(
                        SyntaxKind.SimpleAssignmentExpression,
                        SyntaxFactory.ParseExpression("_ctx._state"),
                        SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(-1))
                    )
                ));

                statements.Add(SyntaxFactory.ReturnStatement());

                return SyntaxFactory.Block(statements).WithTriviaFrom(node);
            }

            return base.VisitReturnStatement(node);
        }
    }
#pragma warning restore RS0030

    /// <summary>
    /// Transpiles a C# method declaration into a target dialect forward declaration signature,
    /// along with forward declarations of referenced struct types.
    /// </summary>
    /// <param name="methodDecl">The Roslyn method declaration syntax node to transpile.</param>
    /// <param name="semanticModel">The semantic model bound to the compilation containing the method.</param>
    /// <param name="reportDiagnostic">An optional callback delegate to receive emitted compiler diagnostics and warnings.</param>
    /// <param name="isDevice">
    /// <see langword="true"/> to emit a device-side function forward signature; 
    /// <see langword="false"/> to emit a kernel entry point signature.
    /// </param>
    /// <param name="dialect">
    /// The target language dialect to use for code generation. If <see langword="null"/>, defaults to <see cref="GSharpCudaDialect"/>.
    /// </param>
    /// <returns>
    /// A string containing the forward declaration statements suitable for header inclusion.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="methodDecl"/> or <paramref name="semanticModel"/> is <see langword="null"/>.
    /// </exception>
    public static string GenerateCudaSignature(MethodDeclarationSyntax methodDecl, SemanticModel semanticModel,
        Action<Diagnostic>? reportDiagnostic = null, bool isDevice = true,
        ITranspilerDialect? dialect = null)
    {
        using var session = TranspilationSession.Begin(methodDecl, semanticModel, reportDiagnostic, dialect);
        if (session.HasError)
        {
            return session.ErrorMessage!;
        }

        ref var context = ref session.Context;

        IReadOnlyList<VariableDeclaratorSyntax>? contextFields = null;
        if (!isDevice && !context.Emitter.Dialect.IsCuda && context.Emitter.IsGpuKernelEntry && methodDecl.Body != null)
        {
            var tuple = AstLoweringPipeline.Lower(methodDecl.Body!);
            contextFields = tuple.ContextFields;
        }

        using var mainWriter = new SourceWriter();
        using var sigWriter = new SourceWriter();

        context.Emitter.Writer = sigWriter;

        if (contextFields != null)
        {
            var methodName = methodDecl.Identifier.Text;
            var methodSymbol = context.Query.SemanticModel.GetDeclaredSymbol<IMethodSymbol>(methodDecl);
            if (methodSymbol != null)
            {
                methodName = $"{methodSymbol.ContainingType.Name}_{methodName}";
            }

            WriteContextStruct(methodName, contextFields, ref context);
        }

        WriteMethodSignature(methodDecl, isDevice, contextFields != null, ref context);
        context.Emitter.Writer.AppendSemiColonLine();

        context.Emitter.Writer = mainWriter;
        foreach (var structSym in StructTranspiler.GetTopologicallySortedStructs(ref context))
        {
            StructTranspiler.WriteTemplatePrefix(ref context, structSym);
            context.Emitter.Writer.Append("struct ").AppendFullyQualifiedCppName(structSym).AppendLine(";");
        }

        context.Emitter.Writer.Append(sigWriter.ToString());
        return mainWriter.ToString();
    }


    private static void WriteMethodSignature(MethodDeclarationSyntax method, bool isDevice, bool hasContext,
        ref TranspileContext context)
    {
        var methodSymbol = context.Query.SemanticModel.GetDeclaredSymbol<IMethodSymbol>(method);

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
            context.Emitter.Dialect.AppendType(ref context, returnType);
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

    private static void WriteMethodBody(MethodDeclarationSyntax method, BlockSyntax? loweredBody,
        ref TranspileContext context)
    {
        using (context.Emitter.Writer.Scope())
        {
            var methodSymbol = context.Query.SemanticModel.GetDeclaredSymbol<IMethodSymbol>(method);
            var isKernelWithReturn = methodSymbol != null && methodSymbol.HasAttribute(KnownAttributes.GpuKernel) &&
                                     methodSymbol.ReturnType.SpecialType != SpecialType.System_Void;

            if (isKernelWithReturn)
            {
                context.Emitter.Writer.AppendLine(
                    "unsigned int __gsharp_global_tid = (blockIdx.z * gridDim.y * gridDim.x + blockIdx.y * gridDim.x + blockIdx.x)" +
                    " * (blockDim.x * blockDim.y * blockDim.z) +" +
                    " threadIdx.z * blockDim.y * blockDim.x + threadIdx.y * blockDim.x + threadIdx.x;");
            }

            if (loweredBody != null)
            {
                context.Emitter.Writer.AppendLine("#define _state _ctx->_state");
                context.Emitter.Writer.AppendLine("#define _yieldedForGridSync _ctx->_yieldedForGridSync");
            }

            if (loweredBody != null)
            {
                context.Visitor.Visit(loweredBody);
            }
            else if (method.Body != null)
            {
                context.Visitor.Visit(method.Body);
            }
            else if (method.ExpressionBody != null)
            {
                if (methodSymbol is { ReturnsVoid: false })
                {
                    context.Emitter.Writer.Append(isKernelWithReturn
                        ? "__gsharp_auto_result[__gsharp_global_tid] = "
                        : "return ");
                }

                context.Visitor.Visit(method.ExpressionBody.Expression);
                context.Emitter.Writer.AppendSemiColonLine();

                if (isKernelWithReturn)
                {
                    context.Emitter.Writer.AppendKeyword("return").AppendSemiColonLine();
                }
            }

            if (loweredBody != null)
            {
                context.Emitter.Writer.AppendLine("#undef _state");
                context.Emitter.Writer.AppendLine("#undef _yieldedForGridSync");
            }
        }
    }

    private static void WriteParameters(SeparatedSyntaxList<ParameterSyntax> parameters, bool isDevice,
        ref TranspileContext context)
    {
        for (var i = 0; i < parameters.Count; i++)
        {
            var p = parameters[i];
            if (p.Type == null)
            {
                continue;
            }

            var typeSymbol = context.Query.SemanticModel.GetTypeSymbol(p.Type);

            StructTranspiler.RegisterUsedStructIfApplicable(typeSymbol, ref context);

            var paramSymbol = context.Query.SemanticModel.GetDeclaredSymbol<IParameterSymbol>(p);
            if (paramSymbol != null && paramSymbol.HasAttribute(KnownAttributes.GpuReadOnly))
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

    private static void WriteCpuLauncher(MethodDeclarationSyntax method,
        IReadOnlyList<VariableDeclaratorSyntax>? contextFields, ref TranspileContext context)
    {
        var hasContext = contextFields != null;
        var methodSymbol = context.Query.SemanticModel.GetDeclaredSymbol<IMethodSymbol>(method);
        if (methodSymbol == null)
        {
            return;
        }

        context.Emitter.Writer.AppendLine();
        context.Emitter.Writer.AppendKeyword("extern \"C\" __declspec(dllexport) void");
        var methodName = $"{methodSymbol.ContainingType.Name}_{method.Identifier.Text}";
        context.Emitter.Writer.Append($" __gsharp_launch_{methodName}(");

        // Launch dimensions
        context.Emitter.Writer.Append("uint32_t gridDimX, uint32_t gridDimY, uint32_t gridDimZ, ");
        context.Emitter.Writer.Append("uint32_t blockDimX, uint32_t blockDimY, uint32_t blockDimZ");

        if (method.ParameterList.Parameters.Count > 0)
        {
            context.Emitter.Writer.Append(", ");
        }

        WriteParameters(method.ParameterList.Parameters, false, ref context);

        var isKernelWithReturn = methodSymbol.HasAttribute(KnownAttributes.GpuKernel) &&
                                 methodSymbol.ReturnType.SpecialType != SpecialType.System_Void;
        if (isKernelWithReturn)
        {
            context.Emitter.Writer.Append(", ");
            context.Emitter.Dialect.AppendType(ref context, methodSymbol.ReturnType);
            context.Emitter.Writer.Append("* __restrict__ __gsharp_auto_result");
        }

        context.Emitter.Writer.AppendLine(")");

        using (context.Emitter.Writer.Scope())
        {
            context.Emitter.Writer.AppendLine(
                "int32_t _gsharp_total_blocks = (int32_t)(gridDimX * gridDimY * gridDimZ);");
            context.Emitter.Writer.AppendLine("#pragma omp parallel for schedule(dynamic)");
            context.Emitter.Writer.AppendLine("for (int32_t _b_idx = 0; _b_idx < _gsharp_total_blocks; _b_idx++)");
            using (context.Emitter.Writer.Scope())
            {
                context.Emitter.Writer.AppendLine("int32_t bx = _b_idx % gridDimX;");
                context.Emitter.Writer.AppendLine("int32_t by = (_b_idx / gridDimX) % gridDimY;");
                context.Emitter.Writer.AppendLine("int32_t bz = _b_idx / (gridDimX * gridDimY);");
                context.Emitter.Writer.AppendLine("__gsharp_reset_smem();");
                if (hasContext)
                {
                    context.Emitter.Writer.AppendLine(
                        $"std::vector<__gsharp_context_{methodName}> _gsharp_contexts(blockDimX * blockDimY * blockDimZ);");

                    if (contextFields != null)
                    {
                        WriteSharedMemoryEmulationBuffers(contextFields, ref context);
                    }

                    context.Emitter.Writer.AppendLine("for (auto& ctx : _gsharp_contexts) {");
                    context.Emitter.Writer.AppendLine("ctx._state = 0;");
                    context.Emitter.Writer.AppendLine("ctx._all_contexts = _gsharp_contexts.data();");

                    if (contextFields != null)
                    {
                        WriteSharedMemoryEmulationAssignments(contextFields, ref context);
                    }

                    context.Emitter.Writer.AppendLine("}");
                    context.Emitter.Writer.AppendLine("std::atomic<bool> anyActive{true};");
                    context.Emitter.Writer.AppendLine("while (anyActive)");
                    using (context.Emitter.Writer.Scope())
                    {
                        context.Emitter.Writer.AppendLine("anyActive = false;");
                        WriteBlockLoop(method, methodSymbol, methodName, hasContext, ref context);
                    }
                }
                else
                {
                    WriteBlockLoop(method, methodSymbol, methodName, hasContext, ref context);
                }
            }
        }
    }

    private static void WriteSharedMemoryEmulationBuffers(IReadOnlyList<VariableDeclaratorSyntax> contextFields,
        ref TranspileContext context)
    {
        foreach (var field in contextFields)
        {
            var typeSyntax = (field.Parent as VariableDeclarationSyntax)?.Type;
            if (typeSyntax is not GenericNameSyntax genericName || genericName.Identifier.ValueText != "SharedMemory")
            {
                continue;
            }

            var typeSymbolField = context.Query.SemanticModel.GetTypeSymbol(typeSyntax) as INamedTypeSymbol;
            var typeArg = typeSymbolField?.TypeArguments[0];

            var initializer = field.Initializer?.Value;
            var sizeExpression =
                initializer != null &&
                InitializationSyntaxExtractor.TryGetObjectCreationFirstArgument(initializer, out var sizeArg)
                    ? sizeArg
                    : null;

            if (typeArg == null || sizeExpression == null)
            {
                continue;
            }

            context.Emitter.Writer.Append("std::vector<");
            context.Emitter.Dialect.AppendType(ref context, typeArg);
            context.Emitter.Writer.Append($"> _smem_buffer_{field.Identifier.ValueText}(");
            context.Visitor.Visit(sizeExpression);
            context.Emitter.Writer.AppendLine(");");
        }
    }

    private static void WriteSharedMemoryEmulationAssignments(IReadOnlyList<VariableDeclaratorSyntax> contextFields,
        ref TranspileContext context)
    {
        foreach (var field in contextFields)
        {
            var typeSyntax = (field.Parent as VariableDeclarationSyntax)?.Type;
            if (typeSyntax is GenericNameSyntax { Identifier.ValueText: "SharedMemory" })
            {
                context.Emitter.Writer.AppendLine(
                    $"ctx.{field.Identifier.ValueText}.data = _smem_buffer_{field.Identifier.ValueText}.data();");
            }
        }
    }

    private static void WriteBlockLoop(MethodDeclarationSyntax method, IMethodSymbol methodSymbol, string methodName,
        bool hasContext, ref TranspileContext context)
    {
        context.Emitter.Writer.AppendLine(
            "int32_t _gsharp_total_threads = (int32_t)(blockDimX * blockDimY * blockDimZ);");
        context.Emitter.Writer.AppendLine("#pragma omp parallel for schedule(static)");
        context.Emitter.Writer.AppendLine("for (int32_t _t_idx = 0; _t_idx < _gsharp_total_threads; _t_idx++)");
        using (context.Emitter.Writer.Scope())
        {
            context.Emitter.Writer.AppendLine("int32_t tx = _t_idx % blockDimX;");
            context.Emitter.Writer.AppendLine("int32_t ty = (_t_idx / blockDimX) % blockDimY;");
            context.Emitter.Writer.AppendLine("int32_t tz = _t_idx / (blockDimX * blockDimY);");
            if (hasContext)
            {
                context.Emitter.Writer.AppendLine(
                    "uint32_t _ctxtid = tz * blockDimY * blockDimX + ty * blockDimX + tx;");
                context.Emitter.Writer.AppendLine("if (_gsharp_contexts[_ctxtid]._state != -1)");
            }

            using (hasContext ? context.Emitter.Writer.Scope() : default)
            {
                if (hasContext)
                {
                    context.Emitter.Writer.AppendLine("anyActive = true;");
                }

                context.Emitter.Writer.AppendLine(
                    "__gsharp_set_thread_idx(tx, ty, tz, bx, by, bz, blockDimX, blockDimY, blockDimZ, gridDimX, gridDimY, gridDimZ);");
                context.Emitter.Writer.Append(methodName);
                using (context.Emitter.Writer.Parentheses())
                {
                    var isFirst = true;

                    foreach (var parameter in method.ParameterList.Parameters)
                    {
                        if (!isFirst)
                        {
                            context.Emitter.Writer.AppendComma().AppendSpace();
                        }

                        isFirst = false;
                        context.Emitter.Writer.Append(parameter.Identifier.Text);
                    }

                    if (hasContext)
                    {
                        if (!isFirst)
                        {
                            context.Emitter.Writer.AppendComma().AppendSpace();
                        }

                        isFirst = false;
                        context.Emitter.Writer.Append("&_gsharp_contexts[_ctxtid]");
                    }

                    var isKernelWithReturn = methodSymbol.HasAttribute(KnownAttributes.GpuKernel) &&
                                             methodSymbol.ReturnType.SpecialType != SpecialType.System_Void;

                    if (isKernelWithReturn)
                    {
                        if (!isFirst)
                        {
                            context.Emitter.Writer.AppendComma().AppendSpace();
                        }

                        context.Emitter.Writer.Append("__gsharp_auto_result");
                    }
                }

                context.Emitter.Writer.AppendSemiColonLine();
            }
        }
    }
}
