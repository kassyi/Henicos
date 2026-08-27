namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>Transpiles C# control-flow statements (if, for, while, do-while, switch, return) to CUDA C++.</summary>
internal static class StatementTranspilers
{
    public static void TranspileIfStatement(IfStatementSyntax node, ref TranspileContext context)
    {
        context.Writer.AppendKeyword("if");
        using (context.Writer.Parentheses())
        {
            context.Visitor.Visit(node.Condition);
        }

        context.Writer.AppendLine();

        context.Visitor.VisitStatementOrBlock(node.Statement);

        if (node.Else != null)
        {
            context.Writer.AppendKeyword("else");
            context.Writer.AppendLine();
            context.Visitor.VisitStatementOrBlock(node.Else.Statement);
        }
    }

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

    private static void TranspileForInitializers(ForStatementSyntax node, ref TranspileContext context)
    {
        if (node.Declaration != null)
        {
            var typeSymbol = context.SemanticModel.GetTypeInfo(node.Declaration.Type).Type;
            var type = CudaTypeMapper.MapType(typeSymbol);
            context.Writer.Append(type);
            context.Writer.AppendSpace();
            for (var i = 0; i < node.Declaration.Variables.Count; i++)
            {
                var v = node.Declaration.Variables[i];
                context.Writer.Append(v.Identifier.Text);
                if (v.Initializer != null)
                {
                    context.Writer.AppendOperator("=");
                    context.Visitor.Visit(v.Initializer.Value);
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

    public static void TranspileWhileStatement(WhileStatementSyntax node, ref TranspileContext context)
    {
        context.Writer.AppendKeyword("while");
        using (context.Writer.Parentheses())
        {
            context.Visitor.Visit(node.Condition);
        }

        context.Writer.AppendLine();
        context.Visitor.VisitStatementOrBlock(node.Statement);
    }

    public static void TranspileDoStatement(DoStatementSyntax node, ref TranspileContext context)
    {
        context.Writer.AppendKeyword("do");
        context.Writer.AppendLine();
        context.Visitor.VisitStatementOrBlock(node.Statement);
        context.Writer.AppendKeyword("while");
        using (context.Writer.Parentheses())
        {
            context.Visitor.Visit(node.Condition);
        }

        context.Writer.Append(";");
        context.Writer.AppendLine();
    }

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

    public static void TranspileBreakStatement(BreakStatementSyntax node, ref TranspileContext context)
    {
        context.Writer.AppendKeyword("break");
        context.Writer.AppendSemiColon();
    }

    public static void TranspileContinueStatement(ContinueStatementSyntax node, ref TranspileContext context)
    {
        context.Writer.AppendKeyword("continue");
        context.Writer.AppendSemiColon();
    }

    public static void TranspileReturnStatement(ReturnStatementSyntax node, ref TranspileContext context)
    {
        context.Writer.AppendKeyword("return");
        if (node.Expression != null)
        {
            context.Writer.AppendSpace();
            context.Visitor.Visit(node.Expression);
        }

        context.Writer.AppendSemiColon();
    }
}


