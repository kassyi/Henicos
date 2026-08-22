using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.Generators.GSharp.Transpiler;

internal static class StatementTranspilers
{
    public static void TranspileIfStatement(IfStatementSyntax node, RoslynToCudaVisitor context)
    {
        context.Writer.AppendKeyword("if");
        using (context.Writer.Parentheses())
        {
            context.Visit(node.Condition);
        }

        context.Writer.AppendLine();

        context.VisitStatementOrBlock(node.Statement);

        if (node.Else != null)
        {
            context.Writer.AppendKeyword("else");
            context.Writer.AppendLine();
            context.VisitStatementOrBlock(node.Else.Statement);
        }
    }

    public static void TranspileForStatement(ForStatementSyntax node, RoslynToCudaVisitor context)
    {
        context.Writer.AppendLine("#pragma unroll");
        context.Writer.AppendKeyword("for");
        using (context.Writer.Parentheses())
        {
            TranspileForInitializers(node, context);
            context.Writer.AppendSemiColon();

            TranspileForCondition(node, context);
            context.Writer.AppendSemiColon();

            TranspileForIncrementors(node, context);
        }

        context.Writer.AppendLine();
        context.VisitStatementOrBlock(node.Statement);
    }

    private static void TranspileForInitializers(ForStatementSyntax node, RoslynToCudaVisitor context)
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
                    context.Visit(v.Initializer.Value);
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
                context.Visit(node.Initializers[i]);
                if (i < node.Initializers.Count - 1)
                {
                    context.Writer.AppendComma();
                }
            }
        }
    }

    private static void TranspileForCondition(ForStatementSyntax node, RoslynToCudaVisitor context)
    {
        if (node.Condition != null)
        {
            context.Visit(node.Condition);
        }
    }

    private static void TranspileForIncrementors(ForStatementSyntax node, RoslynToCudaVisitor context)
    {
        for (var i = 0; i < node.Incrementors.Count; i++)
        {
            context.Visit(node.Incrementors[i]);
            if (i < node.Incrementors.Count - 1)
            {
                context.Writer.AppendComma();
            }
        }
    }

    public static void TranspileWhileStatement(WhileStatementSyntax node, RoslynToCudaVisitor context)
    {
        context.Writer.AppendKeyword("while");
        using (context.Writer.Parentheses())
        {
            context.Visit(node.Condition);
        }

        context.Writer.AppendLine();
        context.VisitStatementOrBlock(node.Statement);
    }

    public static void TranspileDoStatement(DoStatementSyntax node, RoslynToCudaVisitor context)
    {
        context.Writer.AppendKeyword("do");
        context.Writer.AppendLine();
        context.VisitStatementOrBlock(node.Statement);
        context.Writer.AppendKeyword("while");
        using (context.Writer.Parentheses())
        {
            context.Visit(node.Condition);
        }

        context.Writer.Append(";");
        context.Writer.AppendLine();
    }

    public static void TranspileSwitchStatement(SwitchStatementSyntax node, RoslynToCudaVisitor context)
    {
        context.Writer.AppendKeyword("switch");
        using (context.Writer.Parentheses())
        {
            context.Visit(node.Expression);
        }

        context.Writer.AppendLine();
        using (context.Writer.Scope())
        {
            foreach (var section in node.Sections)
            {
                context.Visit(section);
            }
        }
    }

    public static void TranspileSwitchSection(SwitchSectionSyntax node, RoslynToCudaVisitor context)
    {
        foreach (var label in node.Labels)
        {
            context.Visit(label);
            context.Writer.AppendLine();
        }

        foreach (var stmt in node.Statements)
        {
            context.Visit(stmt);
            context.Writer.AppendLine();
        }
    }

    public static void TranspileBreakStatement(BreakStatementSyntax node, RoslynToCudaVisitor context)
    {
        context.Writer.AppendKeyword("break");
        context.Writer.AppendSemiColon();
    }

    public static void TranspileContinueStatement(ContinueStatementSyntax node, RoslynToCudaVisitor context)
    {
        context.Writer.AppendKeyword("continue");
        context.Writer.AppendSemiColon();
    }

    public static void TranspileReturnStatement(ReturnStatementSyntax node, RoslynToCudaVisitor context)
    {
        context.Writer.AppendKeyword("return");
        if (node.Expression != null)
        {
            context.Writer.AppendSpace();
            context.Visit(node.Expression);
        }

        context.Writer.AppendSemiColon();
    }
}
