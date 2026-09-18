namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>Transpiles C# control-flow statements (if, for, while, do-while, switch, return) to CUDA C++.</summary>
/// <remarks><para>Implements: <b>SYN-01, SYN-70, SYN-71, SYN-72, SYN-84, SYN-91</b></para></remarks>
internal static class StatementTranspilers
{
    /// <summary>
    /// Transpiles a C# local function into a C++ capture-by-reference auto lambda expression.
    /// </summary>
    public static void TranspileLocalFunctionStatement(LocalFunctionStatementSyntax node, ref TranspileContext context)
    {
        context.Writer.Append($"auto {node.Identifier.Text} = [&]");
        context.Visitor.Visit(node.ParameterList);
        context.Writer.AppendSpace();

        if (node.Body != null)
        {
            using (context.Writer.Scope())
            {
                foreach (var stmt in node.Body.Statements)
                {
                    context.Visitor.Visit(stmt);
                    context.Writer.AppendLine();
                }
            }
        }
        else if (node.ExpressionBody != null)
        {
            using (context.Writer.Scope())
            {
                context.Writer.Append("return ");
                context.Visitor.Visit(node.ExpressionBody.Expression);
                context.Writer.AppendSemiColonLine();
            }
        }

        context.Writer.AppendSemiColonLine();
    }

    /// <summary>
    /// Transpiles an if-else control-flow statement, hoisting inline declarations from the condition.
    /// </summary>
    public static void TranspileIfStatement(IfStatementSyntax node, ref TranspileContext context)
    {
        DeclarationHoistingHelper.HoistDeclarationExpressions(node.Condition, ref context);
        context.Writer.AppendKeyword("if");
        using (context.Writer.Parentheses())
        {
            context.Visitor.Visit(node.Condition);
        }

        context.Writer.AppendLine();

        context.Visitor.VisitStatementOrBlock(node.Statement);

        if (node.Else != null)
        {
            context.Writer.AppendLine("else");
            context.Visitor.VisitStatementOrBlock(node.Else.Statement);
        }
    }

    /// <summary>
    /// Transpiles a standard three-part for loop statement.
    /// </summary>
    public static void TranspileForStatement(ForStatementSyntax node, ref TranspileContext context)
    {
        context.Writer.AppendKeyword("for");
        using (context.Writer.Parentheses())
        {
            TranspileForInitializers(node, ref context);
            context.Writer.AppendSemiColon();

            TranspileForCondition(node, ref context);
            context.Writer.AppendSemiColon();

            TranspileForIncrementors(node, ref context);
        }

        context.Writer.AppendLine();
        context.Visitor.VisitStatementOrBlock(node.Statement);
    }

    /// <summary>
    /// Transpiles a foreach statement into a C++ range-based for loop.
    /// </summary>
    public static void TranspileForEachStatement(ForEachStatementSyntax node, ref TranspileContext context)
    {
        DeclarationHoistingHelper.HoistDeclarationExpressions(node.Expression, ref context);
        context.Writer.AppendKeyword("for");
        using (context.Writer.Parentheses())
        {
            if (node.Type.IsVar)
            {
                context.Writer.Append("auto ");
            }
            else
            {
                var typeSymbol = context.SemanticModel.GetTypeInfo(node.Type).Type;
                context.AppendType(typeSymbol);
                context.Writer.AppendSpace();
            }

            context.Writer.Append($"{node.Identifier.Text} : ");
            context.Visitor.Visit(node.Expression);
        }

        context.Writer.AppendLine();
        context.Visitor.VisitStatementOrBlock(node.Statement);
    }

    private static void TranspileForInitializers(ForStatementSyntax node, ref TranspileContext context)
    {
        if (node.Declaration != null)
        {
            var typeSymbol = context.SemanticModel.GetTypeInfo(node.Declaration.Type).Type;
            if (node.Declaration.Type.IsVar)
            {
                context.Writer.Append("auto ");
            }
            else
            {
                context.AppendType(typeSymbol);
                context.Writer.AppendSpace();
            }

            for (var i = 0; i < node.Declaration.Variables.Count; i++)
            {
                var v = node.Declaration.Variables[i];
                if (v.Initializer != null)
                {
                    context.Writer.Append($"{v.Identifier.Text} = ");
                    context.Visitor.Visit(v.Initializer.Value);
                }
                else
                {
                    context.Writer.Append(v.Identifier.Text);
                }

                if (i < node.Declaration.Variables.Count - 1)
                {
                    context.Writer.AppendComma();
                }
            }
        }
        else
        {
            for (var i = 0; i < node.Initializers.Count; i++)
            {
                context.Visitor.Visit(node.Initializers[i]);
                if (i < node.Initializers.Count - 1)
                {
                    context.Writer.AppendComma();
                }
            }
        }
    }

    private static void TranspileForCondition(ForStatementSyntax node, ref TranspileContext context)
    {
        if (node.Condition != null)
        {
            context.Visitor.Visit(node.Condition);
        }
    }

    private static void TranspileForIncrementors(ForStatementSyntax node, ref TranspileContext context)
    {
        for (var i = 0; i < node.Incrementors.Count; i++)
        {
            context.Visitor.Visit(node.Incrementors[i]);
            if (i < node.Incrementors.Count - 1)
            {
                context.Writer.AppendComma();
            }
        }
    }

    /// <summary>
    /// Transpiles a while loop statement, hoisting inline declarations from the condition expression.
    /// </summary>
    public static void TranspileWhileStatement(WhileStatementSyntax node, ref TranspileContext context)
    {
        DeclarationHoistingHelper.HoistDeclarationExpressions(node.Condition, ref context);
        context.Writer.AppendKeyword("while");
        using (context.Writer.Parentheses())
        {
            context.Visitor.Visit(node.Condition);
        }

        context.Writer.AppendLine();
        context.Visitor.VisitStatementOrBlock(node.Statement);
    }

    /// <summary>
    /// Transpiles a do-while loop statement.
    /// </summary>
    public static void TranspileDoStatement(DoStatementSyntax node, ref TranspileContext context)
    {
        context.Writer
            .AppendKeyword("do")
            .AppendLine();
        context.Visitor.VisitStatementOrBlock(node.Statement);
        context.Writer.AppendKeyword("while");
        using (context.Writer.Parentheses())
        {
            context.Visitor.Visit(node.Condition);
        }

        context.Writer.AppendSemiColonLine();
    }

    /// <summary>
    /// Transpiles a switch statement and its case sections into C++ switch-case syntax.
    /// </summary>
    public static void TranspileSwitchStatement(SwitchStatementSyntax node, ref TranspileContext context)
    {
        context.Writer.AppendKeyword("switch");
        using (context.Writer.Parentheses())
        {
            context.Visitor.Visit(node.Expression);
        }

        context.Writer.AppendLine();
        using (context.Writer.Scope())
        {
            foreach (var section in node.Sections)
            {
                context.Visitor.Visit(section);
            }
        }
    }

    /// <summary>
    /// Transpiles the labels and statements within a switch case section.
    /// </summary>
    public static void TranspileSwitchSection(SwitchSectionSyntax node, ref TranspileContext context)
    {
        foreach (var label in node.Labels)
        {
            context.Visitor.Visit(label);
            context.Writer.AppendLine();
        }

        foreach (var stmt in node.Statements)
        {
            context.Visitor.Visit(stmt);
            context.Writer.AppendLine();
        }
    }

    /// <summary>
    /// Transpiles a break statement.
    /// </summary>
    public static void TranspileBreakStatement(BreakStatementSyntax node, ref TranspileContext context) =>
        context.Writer.Append("break;");

    /// <summary>
    /// Transpiles a continue statement.
    /// </summary>
    public static void TranspileContinueStatement(ContinueStatementSyntax node, ref TranspileContext context) =>
        context.Writer.Append("continue;");

    /// <summary>
    /// Transpiles a return statement, hoisting any inline declarations from the returned expression.
    /// </summary>
    public static void TranspileReturnStatement(ReturnStatementSyntax node, ref TranspileContext context)
    {
        var enclosingCallable = node.Ancestors().FirstOrDefault(n =>
            n is MethodDeclarationSyntax or LocalFunctionStatementSyntax or ParenthesizedLambdaExpressionSyntax
                or SimpleLambdaExpressionSyntax or AnonymousMethodExpressionSyntax);

        if (enclosingCallable is MethodDeclarationSyntax methodNode)
        {
            var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodNode);
            if (methodSymbol != null && methodSymbol.HasAttribute(KnownAttributes.GpuKernel))
            {
                if (methodSymbol.ReturnType.SpecialType != SpecialType.System_Void && node.Expression != null)
                {
                    DeclarationHoistingHelper.HoistDeclarationExpressions(node.Expression, ref context);
                    context.Writer.Append("__gsharp_auto_result[__gsharp_global_tid] = ");
                    context.Visitor.Visit(node.Expression);
                    context.Writer.AppendSemiColonLine();
                    context.Writer.AppendKeyword("return").AppendSemiColon();
                    return;
                }
            }
        }

        if (node.Expression != null)
        {
            DeclarationHoistingHelper.HoistDeclarationExpressions(node.Expression, ref context);
            context.Writer.Append("return ");
            context.Visitor.Visit(node.Expression);
        }
        else
        {
            context.Writer.AppendKeyword("return");
        }

        context.Writer.AppendSemiColon();
    }
}


