namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>Transpiles the loop statements (for, foreach, while, do-while) to CUDA C++.</summary>
internal static class LoopStatementTranspilers
{
    /// <summary>
    /// Transpiles a standard three-part for loop statement.
    /// </summary>
    public static void TranspileForStatement(ForStatementSyntax node, ref TranspileContext context)
    {
        // The declaration, the initializers, the condition and the incrementors (not the body, which has its own)
        if (node.Declaration != null)
        {
            StatementPrelude.Emit(node.Declaration, ref context);
        }

        foreach (var initializer in node.Initializers)
        {
            StatementPrelude.Emit(initializer, ref context);
        }

        if (node.Condition != null)
        {
            StatementPrelude.Emit(node.Condition, ref context);
        }

        foreach (var incrementor in node.Incrementors)
        {
            StatementPrelude.Emit(incrementor, ref context);
        }

        context.Emitter.Writer.Append("for");
        using (context.Emitter.Writer.Parentheses())
        {
            TranspileForInitializers(node, ref context);
            context.Emitter.Writer.Append(';');

            TranspileForCondition(node, ref context);
            context.Emitter.Writer.Append(';');

            TranspileForIncrementors(node, ref context);
        }

        context.Emitter.Writer.AppendLine();
        context.Visitor.VisitStatementOrBlock(node.Statement);
    }

    /// <summary>
    /// Transpiles a foreach statement: an iterator source is expanded inline,
    /// anything else becomes a C++ range-based for loop.
    /// </summary>
    public static void TranspileForEachStatement(ForEachStatementSyntax node, ref TranspileContext context)
    {
        if (IteratorTypeResolver.TryResolveExternalIteratorSource(
                node.Expression,
                context.Query.SemanticModel,
                out _))
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, node, IteratorInliner.IteratorWithoutBody);
            return;
        }

        if (IteratorTypeResolver.TryResolveIteratorSource(
                node.Expression,
                context.Query.SemanticModel,
                out var sourceInvocation,
                out var iteratorMethod))
        {
            TranspileIteratorForEach(node, sourceInvocation, iteratorMethod, ref context);
            return;
        }

        TranspileRangeForEach(node, ref context);
    }

    // Inlines the body of the iterator method the loop reads from, in place of the loop.
    private static void TranspileIteratorForEach(
        ForEachStatementSyntax node,
        InvocationExpressionSyntax sourceInvocation,
        IMethodSymbol iteratorMethod,
        ref TranspileContext context
    )
    {
        if (IteratorTypeResolver.IsRecursiveIterator(iteratorMethod, context.Query.SemanticModel.Compilation))
        {
            var location = iteratorMethod.TryGetDeclaringSyntax<MethodDeclarationSyntax>(out var decl)
                ? decl.Identifier.GetLocation()
                : node.GetLocation();
            context.Reporter.Fail(
                GSharpDiagnostics.IteratorShapeNotSupported,
                location,
                iteratorMethod.Name,
                "recursive iterators are not supported");
            return;
        }

        RuntimeHelpers.EnsureSufficientExecutionStack();

        if (IteratorTypeResolver.IsGenericIEnumerable(iteratorMethod.ReturnType, out var elementType))
        {
            StructTranspiler.RegisterUsedStructIfApplicable(elementType, ref context);
        }

        var expandedBlock = IteratorInliner.Expand(
            node,
            iteratorMethod,
            sourceInvocation,
            context.Query.SemanticModel,
            ref context.Emitter.IteratorExpansionCounter,
            ref context.Emitter.ArgumentTempCounter,
            context.Emitter.Dialect,
            context.Reporter);

        context.Visitor.VisitBlock(expandedBlock);
    }

    // Writes a C++ range-based for loop over an array-like source.
    private static void TranspileRangeForEach(ForEachStatementSyntax node, ref TranspileContext context)
    {
        StatementPrelude.Emit(node.Expression, ref context);

        // A loop variable that converts each element with a .NET conversion helper (#995) takes the raw element first:
        // for (auto __gsharp_foreach_e_<n> : <source>) { int x = __gsharp_to_i32(__gsharp_foreach_e_<n>); <body> }
        var model = context.Query.SemanticModel;
        var sourceElementType = model.GetTypeSymbol(node.Expression).TryGetArrayLikeElementType(out var element)
            ? element
            : null;
        var targetType = ForEachElementConversion.GetTarget(node, sourceElementType, model);
        var conversionHelper = IntegerSemantics.GetConversionHelper(sourceElementType, targetType, out var needsCast);
        var rawElement = conversionHelper != null ? context.Emitter.ForEachElementCounter++ : -1;

        context.Emitter.Writer.Append("for");
        using (context.Emitter.Writer.Parentheses())
        {
            if (conversionHelper != null)
            {
                context.Emitter.Writer.Append($"auto {GSharpInternalNames.Lowering.ForEachElementPrefix}{rawElement}");
            }
            else
            {
                if (node.Type.IsVar)
                {
                    context.Emitter.Writer.Append("auto ");
                }
                else
                {
                    context.Emitter.Dialect.AppendType(ref context, model.GetTypeSymbol(node.Type));
                    context.Emitter.Writer.Append(' ');
                }

                CppNameResolver.AppendDeclaredName(node, node.Identifier, ref context);
            }

            context.Emitter.Writer.Append(" : ");
            // foreach over a null array throws NullReferenceException at the loop entry in C# (#706)
            ArrayNullCheck.AppendReceiver(node.Expression, ref context);
        }

        context.Emitter.Writer.AppendLine();
        if (conversionHelper == null)
        {
            context.Visitor.VisitStatementOrBlock(node.Statement);
            return;
        }

        TranspileConvertedElementBody(node, targetType, conversionHelper, needsCast, rawElement, ref context);
    }

    // { <type> x = <helper>(__gsharp_foreach_e_<n>); <body> }
    private static void TranspileConvertedElementBody(ForEachStatementSyntax node, ITypeSymbol? targetType,
        string conversionHelper, bool needsCast, int rawElement, ref TranspileContext context)
    {
        using (context.Emitter.Writer.Scope())
        {
            context.Emitter.Dialect.AppendType(ref context, targetType);
            context.Emitter.Writer.Append(' ');
            CppNameResolver.AppendDeclaredName(node, node.Identifier, ref context);
            context.Emitter.Writer.Append(" = ");
            if (needsCast)
            {
                using (context.Emitter.Writer.Parentheses())
                {
                    context.Emitter.Dialect.AppendType(ref context, targetType);
                }
            }

            context.Emitter.Writer.Append(conversionHelper);
            using (context.Emitter.Writer.Parentheses())
            {
                context.Emitter.Writer.Append($"{GSharpInternalNames.Lowering.ForEachElementPrefix}{rawElement}");
            }

            context.Emitter.Writer.AppendLine(";");
            if (node.Statement is BlockSyntax block)
            {
                context.Visitor.VisitBlock(block);
            }
            else
            {
                context.Visitor.Visit(node.Statement);
                context.Emitter.Writer.AppendLine();
            }
        }
    }

    private static void TranspileForInitializers(ForStatementSyntax node, ref TranspileContext context)
    {
        if (node.Declaration != null)
        {
            var typeSymbol = context.Query.SemanticModel.GetTypeSymbol(node.Declaration.Type);
            var isPointer = typeSymbol?.TypeKind == TypeKind.Pointer;
            DeclarationTranspilerHelper.TranspileCombinedDeclarations(
                node.Declaration.Variables, typeSymbol, node.Declaration.Type.IsVar, isPointer, ref context);
        }
        else
        {
            TranspileCommaSeparatedExpressions(node.Initializers, ref context);
        }
    }

    private static void TranspileForCondition(ForStatementSyntax node, ref TranspileContext context)
    {
        if (node.Condition != null)
        {
            context.Visitor.Visit(node.Condition);
        }
    }

    private static void TranspileForIncrementors(ForStatementSyntax node, ref TranspileContext context) =>
        TranspileCommaSeparatedExpressions(node.Incrementors, ref context);

    private static void TranspileCommaSeparatedExpressions(SeparatedSyntaxList<ExpressionSyntax> expressions,
        ref TranspileContext context)
    {
        for (var i = 0; i < expressions.Count; i++)
        {
            context.Visitor.Visit(expressions[i]);
            if (i < expressions.Count - 1)
            {
                context.Emitter.Writer.Append(',');
            }
        }
    }

    /// <summary>
    /// Transpiles a while loop statement, hoisting inline declarations from the condition expression.
    /// </summary>
    public static void TranspileWhileStatement(WhileStatementSyntax node, ref TranspileContext context)
    {
        StatementPrelude.Emit(node.Condition, ref context);
        context.Emitter.Writer.Append("while");
        using (context.Emitter.Writer.Parentheses())
        {
            context.Visitor.Visit(node.Condition);
        }

        context.Emitter.Writer.AppendLine();
        context.Visitor.VisitStatementOrBlock(node.Statement);
    }

    /// <summary>
    /// Transpiles a do-while loop statement.
    /// </summary>
    public static void TranspileDoStatement(DoStatementSyntax node, ref TranspileContext context)
    {
        StatementPrelude.Emit(node.Condition, ref context);
        context.Emitter.Writer
            .Append("do")
            .AppendLine();
        context.Visitor.VisitStatementOrBlock(node.Statement);
        context.Emitter.Writer.Append("while");
        using (context.Emitter.Writer.Parentheses())
        {
            context.Visitor.Visit(node.Condition);
        }

        context.Emitter.Writer.AppendLine(";");
    }
}
