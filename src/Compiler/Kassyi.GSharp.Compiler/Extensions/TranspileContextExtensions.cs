using Kassyi.GSharp.Compiler.Transpiler;

namespace Kassyi.GSharp.Compiler.Extensions;

/// <summary>Provides extension methods for <see cref="TranspileContext"/> during AST traversal and code emission.</summary>
internal static class TranspileContextExtensions
{
    /// <summary>
    /// Transpiles a separated list of syntax nodes with comma separators.
    /// </summary>
    /// <typeparam name="T">The syntax node type.</typeparam>
    /// <param name="context">The transpilation context.</param>
    /// <param name="list">The separated syntax list.</param>
    /// <param name="withSpace">Whether to append a space after each comma.</param>
    public static void TranspileSeparatedList<T>(this ref TranspileContext context, SeparatedSyntaxList<T> list, bool withSpace = false) where T : SyntaxNode
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (i > 0)
            {
                context.Writer.AppendComma();
                if (withSpace)
                {
                    context.Writer.AppendSpace();
                }
            }

            context.Visitor.Visit(list[i]);
        }
    }

    /// <summary>
    /// Transpiles a sequence of syntax nodes with comma separators.
    /// </summary>
    /// <typeparam name="T">The syntax node type.</typeparam>
    /// <param name="context">The transpilation context.</param>
    /// <param name="list">The sequence of syntax nodes.</param>
    /// <param name="withSpace">Whether to append a space after each comma.</param>
    public static void TranspileSeparatedList<T>(this ref TranspileContext context, IEnumerable<T> list, bool withSpace = false) where T : SyntaxNode
    {
        var first = true;
        foreach (var item in list)
        {
            if (!first)
            {
                context.Writer.AppendComma();
                if (withSpace)
                {
                    context.Writer.AppendSpace();
                }
            }

            context.Visitor.Visit(item);
            first = false;
        }
    }

    /// <summary>
    /// Emits an <c>auto</c> (or <c>auto&amp;</c>) local variable declaration and assignment statement.
    /// </summary>
    /// <param name="context">The transpilation context.</param>
    /// <param name="variableName">The name of the variable.</param>
    /// <param name="valueNode">The syntax node to evaluate for the assignment.</param>
    /// <param name="isRef">Whether to emit as a reference variable (<c>auto&amp;</c>).</param>
    public static void EmitAutoAssignment(this ref TranspileContext context, string variableName, SyntaxNode valueNode, bool isRef = false)
    {
        context.Writer.AppendKeyword("auto");
        if (isRef)
        {
            context.Writer.AppendOperator("&");
        }

        context.Writer.AppendSpace();
        context.Writer.Append(variableName);
        context.Writer.AppendSpace();
        context.Writer.AppendOperator("=");
        context.Writer.AppendSpace();
        context.Visitor.Visit(valueNode);
        context.Writer.AppendSemiColon();
        context.Writer.AppendLine();
    }

    /// <summary>
    /// Emits an <c>auto</c> local variable declaration and assignment from a raw string expression.
    /// </summary>
    /// <param name="context">The transpilation context.</param>
    /// <param name="variableName">The name of the variable.</param>
    /// <param name="rawValue">The raw expression string to assign.</param>
    public static void EmitAutoAssignment(this ref TranspileContext context, string variableName, string rawValue)
    {
        context.Writer.AppendKeyword("auto");
        context.Writer.AppendSpace();
        context.Writer.Append(variableName);
        context.Writer.AppendSpace();
        context.Writer.AppendOperator("=");
        context.Writer.AppendSpace();
        context.Writer.Append(rawValue);
        context.Writer.AppendSemiColon();
        context.Writer.AppendLine();
    }

    /// <summary>
    /// Emits a <c>return</c> statement evaluating the specified syntax node.
    /// </summary>
    /// <param name="context">The transpilation context.</param>
    /// <param name="valueNode">The syntax node to return.</param>
    public static void EmitReturn(this ref TranspileContext context, SyntaxNode valueNode)
    {
        context.Writer.AppendKeyword("return");
        context.Writer.AppendSpace();
        context.Visitor.Visit(valueNode);
        context.Writer.AppendSemiColon();
        context.Writer.AppendLine();
    }

    /// <summary>
    /// Emits a <c>return</c> statement for the specified raw expression string or variable name.
    /// </summary>
    /// <param name="context">The transpilation context.</param>
    /// <param name="rawExpression">The raw expression string or identifier to return.</param>
    public static void EmitReturn(this ref TranspileContext context, string rawExpression)
    {
        context.Writer.AppendKeyword("return");
        context.Writer.AppendSpace();
        context.Writer.Append(rawExpression);
        context.Writer.AppendSemiColon();
        context.Writer.AppendLine();
    }

    /// <summary>
    /// Transpiles object/struct initializer member assignments against a target variable instance.
    /// </summary>
    /// <param name="context">The transpilation context.</param>
    /// <param name="targetVariable">The target variable instance name.</param>
    /// <param name="initializer">The initializer expression containing member assignments.</param>
    public static void TranspileMemberAssignments(this ref TranspileContext context, string targetVariable, InitializerExpressionSyntax initializer)
    {
        foreach (var expr in initializer.Expressions)
        {
            if (expr is not AssignmentExpressionSyntax assignment)
            {
                continue;
            }

            context.Writer.Append($"{targetVariable}.");
            context.Visitor.Visit(assignment.Left);
            context.Writer.AppendSpace();
            context.Writer.AppendOperator("=");
            context.Writer.AppendSpace();
            context.Visitor.Visit(assignment.Right);
            context.Writer.AppendSemiColon();
            context.Writer.AppendLine();
        }
    }
}
