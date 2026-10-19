
namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>Transpiles C# control-flow statements (if, for, while, do-while, switch, return) to CUDA C++.</summary>
internal static class StatementTranspilers
{
    /// <summary>
    /// Transpiles a C# local function into a C++ capture-by-reference auto lambda expression.
    /// </summary>
    public static void TranspileLocalFunctionStatement(LocalFunctionStatementSyntax node, ref TranspileContext context)
    {
        context.Emitter.Writer.Append($"auto {node.Identifier.Text} = [&]");
        context.Visitor.Visit(node.ParameterList);
        context.Emitter.Writer.AppendSpace();

        LambdaTranspilerHelper.TranspileLambdaBody(ref context, node.Body, node.ExpressionBody?.Expression);

        context.Emitter.Writer.AppendSemiColonLine();
    }

    /// <summary>
    /// Transpiles an if-else control-flow statement, hoisting inline declarations from the condition.
    /// </summary>
    public static void TranspileIfStatement(IfStatementSyntax node, ref TranspileContext context)
    {
        DeclarationHoistingHelper.HoistDeclarationExpressions(node.Condition, ref context);
        context.Emitter.Writer.AppendKeyword("if");
        using (context.Emitter.Writer.Parentheses())
        {
            context.Visitor.Visit(node.Condition);
        }

        context.Emitter.Writer.AppendLine();
        context.Visitor.VisitStatementOrBlock(node.Statement);

        if (node.Else == null)
        {
            return;
        }

        context.Emitter.Writer.AppendLine("else");
        context.Visitor.VisitStatementOrBlock(node.Else.Statement);
    }

    /// <summary>
    /// Transpiles a standard three-part for loop statement.
    /// </summary>
    public static void TranspileForStatement(ForStatementSyntax node, ref TranspileContext context)
    {
        context.Emitter.Writer.AppendKeyword("for");
        using (context.Emitter.Writer.Parentheses())
        {
            TranspileForInitializers(node, ref context);
            context.Emitter.Writer.AppendSemiColon();

            TranspileForCondition(node, ref context);
            context.Emitter.Writer.AppendSemiColon();

            TranspileForIncrementors(node, ref context);
        }

        context.Emitter.Writer.AppendLine();
        context.Visitor.VisitStatementOrBlock(node.Statement);
    }

    /// <summary>
    /// Transpiles a foreach statement into a C++ range-based for loop.
    /// </summary>
    public static void TranspileForEachStatement(ForEachStatementSyntax node, ref TranspileContext context)
    {
        DeclarationHoistingHelper.HoistDeclarationExpressions(node.Expression, ref context);
        context.Emitter.Writer.AppendKeyword("for");
        using (context.Emitter.Writer.Parentheses())
        {
            if (node.Type.IsVar)
            {
                context.Emitter.Writer.Append("auto ");
            }
            else
            {
                var typeSymbol = context.Query.SemanticModel.GetTypeSymbol(node.Type);
                context.Emitter.Dialect.AppendType(ref context, typeSymbol);
                context.Emitter.Writer.AppendSpace();
            }

            context.Emitter.Writer.Append($"{node.Identifier.Text} : ");
            context.Visitor.Visit(node.Expression);
        }

        context.Emitter.Writer.AppendLine();
        context.Visitor.VisitStatementOrBlock(node.Statement);
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
                context.Emitter.Writer.AppendComma();
            }
        }
    }

    /// <summary>
    /// Transpiles a while loop statement, hoisting inline declarations from the condition expression.
    /// </summary>
    public static void TranspileWhileStatement(WhileStatementSyntax node, ref TranspileContext context)
    {
        DeclarationHoistingHelper.HoistDeclarationExpressions(node.Condition, ref context);
        context.Emitter.Writer.AppendKeyword("while");
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
        context.Emitter.Writer
            .AppendKeyword("do")
            .AppendLine();
        context.Visitor.VisitStatementOrBlock(node.Statement);
        context.Emitter.Writer.AppendKeyword("while");
        using (context.Emitter.Writer.Parentheses())
        {
            context.Visitor.Visit(node.Condition);
        }

        context.Emitter.Writer.AppendSemiColonLine();
    }

    /// <summary>
    /// Transpiles a switch statement and its case sections into C++ switch-case syntax.
    /// </summary>
    public static void TranspileSwitchStatement(SwitchStatementSyntax node, ref TranspileContext context)
    {
        var switchId = ++context.Emitter.SwitchIdCounter;
        context.Emitter.ActiveSwitchIds.Push(switchId);

        context.Emitter.Writer.AppendKeyword("switch");
        using (context.Emitter.Writer.Parentheses())
        {
            context.Visitor.Visit(node.Expression);
        }

        context.Emitter.Writer.AppendLine();
        using (context.Emitter.Writer.Scope())
        {
            foreach (var section in node.Sections)
            {
                context.Visitor.Visit(section);
            }
        }

        context.Emitter.ActiveSwitchIds.Pop();
    }

    /// <summary>
    /// Transpiles the labels and statements within a switch case section.
    /// </summary>
    public static void TranspileSwitchSection(SwitchSectionSyntax node, ref TranspileContext context)
    {
        foreach (var label in node.Labels)
        {
            context.Visitor.Visit(label);
            context.Emitter.Writer.AppendLine();
        }

        using (context.Emitter.Writer.Scope())
        {
            foreach (var stmt in node.Statements)
            {
                context.Visitor.Visit(stmt);
                context.Emitter.Writer.AppendLine();
            }
        }
    }

    /// <summary>
    /// Transpiles a break statement.
    /// </summary>
    public static void TranspileBreakStatement(BreakStatementSyntax node, ref TranspileContext context) =>
        context.Emitter.Writer.Append("break;");

    /// <summary>
    /// Transpiles a continue statement.
    /// </summary>
    public static void TranspileContinueStatement(ContinueStatementSyntax node, ref TranspileContext context) =>
        context.Emitter.Writer.Append("continue;");

    public static void TranspileGotoStatement(GotoStatementSyntax node, ref TranspileContext context)
    {
        if (node.IsKind(SyntaxKind.GotoCaseStatement))
        {
            context.Emitter.Writer.Append($"goto __gsharp_switch_{context.Emitter.CurrentSwitchId}_state_");
            context.Visitor.Visit(node.Expression);
            context.Emitter.Writer.AppendSemiColonLine();
            return;
        }
        else if (node.IsKind(SyntaxKind.GotoDefaultStatement))
        {
            context.Emitter.Writer.AppendLine($"goto __gsharp_switch_{context.Emitter.CurrentSwitchId}_state_default;");
            return;
        }

        context.Emitter.Writer.Append("goto ");
        if (node.Expression is IdentifierNameSyntax identifier && context.Emitter.LabelSuffix != null)
        {
            context.Emitter.Writer.Append(identifier.Identifier.ValueText);
            context.Emitter.Writer.Append(context.Emitter.LabelSuffix);
        }
        else
        {
            context.Visitor.Visit(node.Expression);
        }

        context.Emitter.Writer.AppendSemiColonLine();
    }

    public static void TranspileLabeledStatement(LabeledStatementSyntax node, ref TranspileContext context)
    {
        context.Emitter.Writer.Append(node.Identifier.ValueText);
        if (context.Emitter.LabelSuffix != null)
        {
            context.Emitter.Writer.Append(context.Emitter.LabelSuffix);
        }

        context.Emitter.Writer.AppendLine(":");
        context.Visitor.Visit(node.Statement);
    }

    /// <summary>
    /// Transpiles a return statement, hoisting any inline declarations from the returned expression.
    /// </summary>
    public static void TranspileReturnStatement(ReturnStatementSyntax node, ref TranspileContext context)
    {
        var isStateMachineReturn = node.HasAnnotations("StateMachineReturn");
        var emitStateTermination = !isStateMachineReturn && !context.Emitter.Dialect.IsCuda
                                   && context.Emitter is { IsGpuKernelEntry: true, CallableDepth: 0 };

        if (node.Expression != null)
        {
            DeclarationHoistingHelper.HoistDeclarationExpressions(node.Expression, ref context);

            if (context.Emitter is { IsGpuKernelEntry: true, IsVoidReturn: false, CallableDepth: 0 })
            {
                context.Emitter.Writer.Append("__gsharp_auto_result[__gsharp_global_tid] = ");
                context.Visitor.Visit(node.Expression);
                context.Emitter.Writer.AppendSemiColonLine();

                if (emitStateTermination)
                {
                    context.Emitter.Writer.Append(GSharpInternalNames.StateTermination);
                }

                context.Emitter.Writer.AppendKeyword("return;");
                return;
            }

            if (emitStateTermination)
            {
                context.Emitter.Writer.Append(GSharpInternalNames.StateTermination);
            }

            context.Emitter.Writer.Append("return ");
            context.Visitor.Visit(node.Expression);
            context.Emitter.Writer.AppendSemiColon();
            return;
        }

        if (emitStateTermination)
        {
            context.Emitter.Writer.Append(GSharpInternalNames.StateTermination);
        }

        context.Emitter.Writer.AppendKeyword("return;");
    }
}


