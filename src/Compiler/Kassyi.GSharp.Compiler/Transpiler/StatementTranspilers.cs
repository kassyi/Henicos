namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>Transpiles the if statement and the local function statement to CUDA C++.</summary>
internal static class StatementTranspilers
{
    /// <summary>
    /// Transpiles a C# local function into a C++ capture-by-reference auto lambda expression.
    /// </summary>
    public static void TranspileLocalFunctionStatement(LocalFunctionStatementSyntax node, ref TranspileContext context)
    {
        context.Emitter.Writer.Append("auto ");
        CppNameResolver.AppendDeclaredName(node, node.Identifier, ref context);
        context.Emitter.Writer.Append(" = [&]");
        context.Visitor.Visit(node.ParameterList);
        context.Emitter.Writer.Append(' ');

        LambdaTranspilerHelper.TranspileLambdaBody(ref context, node.Body, node.ExpressionBody?.Expression);

        context.Emitter.Writer.AppendLine(";");
    }

    /// <summary>
    /// Transpiles an if-else control-flow statement, hoisting inline declarations from the condition.
    /// </summary>
    public static void TranspileIfStatement(IfStatementSyntax node, ref TranspileContext context)
    {
        StatementPrelude.Emit(node.Condition, ref context);
        context.Emitter.Writer.Append("if");
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
}
