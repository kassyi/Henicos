using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Ir.Building;

/// <summary>
/// Reads the spelling of nodes (parentheses around expressions, literal text of constants, comments before statements,
/// and how a local's type was declared) from the source.
/// </summary>
internal static class IrSpelling
{
    /// <summary>The count of parentheses that directly enclosed <paramref name="operation"/> in the source.</summary>
    public static int ParenthesesAround(IOperation operation)
    {
        Measure(operation, out var parentheses, out _, out _);
        return parentheses;
    }

    /// <summary>
    /// Measures the leading parentheses count and outermost spelling span (including outer parentheses and
    /// null-forgiving <c>!</c> operators) around <paramref name="operation"/>, and whether that spelling holds a
    /// <c>!</c>.
    /// </summary>
    public static void Measure(IOperation operation, out int parentheses, out TextSpan? outerSpan,
        out bool nullForgiving)
    {
        parentheses = 0;
        nullForgiving = false;
        if (!operation.IsImplicit)
        {
            outerSpan = OuterSpanAround(operation.Syntax, countParentheses: true, out parentheses, out nullForgiving);
            return;
        }

        // [WHY] An implicit conversion (of any kind) has the syntax of the expression it converts, written with its
        // parentheses and !; an error site at it reports that spelling. The parentheses stay with the operand
        outerSpan = operation is IConversionOperation
            ? OuterSpanAround(operation.Syntax, countParentheses: false, out _, out nullForgiving)
            : null;
    }

    private static TextSpan? OuterSpanAround(SyntaxNode syntax, bool countParentheses, out int parentheses,
        out bool nullForgiving)
    {
        parentheses = 0;
        nullForgiving = false;
        var start = syntax;
        var counting = countParentheses;
        while (true)
        {
            if (syntax.Parent is ParenthesizedExpressionSyntax parenthesized)
            {
                if (counting)
                {
                    parentheses++;
                }

                syntax = parenthesized;
            }
            else if (syntax.Parent is PostfixUnaryExpressionSyntax suppressed &&
                     suppressed.IsKind(SyntaxKind.SuppressNullableWarningExpression))
            {
                counting = false;
                nullForgiving = true;
                syntax = suppressed;
            }
            else
            {
                break;
            }
        }

        return ReferenceEquals(syntax, start) ? null : syntax.Span;
    }

    /// <summary>
    /// The literal as written in the source when <paramref name="operation"/> is a literal expression; null otherwise.
    /// </summary>
    public static string? LiteralText(IOperation operation) =>
        !operation.IsImplicit && operation.Syntax is LiteralExpressionSyntax literal
            ? literal.Token.Text
            : null;

    /// <summary>
    /// The leading trivia comments on <paramref name="statement"/>, in order, without trailing line breaks.
    /// </summary>
    public static ImmutableArray<string> CommentsBefore(SyntaxNode statement, bool isImplicit)
    {
        if (isImplicit)
        {
            return ImmutableArray<string>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var trivia in statement.GetFirstToken().LeadingTrivia)
        {
            var kind = trivia.Kind();
            if (kind is SyntaxKind.SingleLineCommentTrivia or SyntaxKind.MultiLineCommentTrivia
                or SyntaxKind.SingleLineDocumentationCommentTrivia or SyntaxKind.MultiLineDocumentationCommentTrivia)
            {
                // A documentation comment ends with its line break; a line comment does not
                builder.Add(trivia.ToFullString().AsSpan().TrimEnd("\r\n".AsSpan()).ToString());
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>How the source declared the type of <paramref name="local"/>.</summary>
    public static IrDeclaredTypeForm DeclaredTypeFormOf(ILocalSymbol local, CancellationToken token)
    {
        if (local.DeclaringSyntaxReferences is not [var reference, ..])
        {
            return IrDeclaredTypeForm.Named;
        }

        var syntax = reference.GetSyntax(token);
        TypeSyntax? typeSyntax = null;

        switch (syntax)
        {
            case VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax declaration }:
                typeSyntax = declaration.Type;
                break;
            case SingleVariableDesignationSyntax designation:
                var current = designation.Parent;
                while (current is ParenthesizedVariableDesignationSyntax)
                {
                    current = current.Parent;
                }

                if (current is DeclarationExpressionSyntax declarationExpression)
                {
                    typeSyntax = declarationExpression.Type;
                }
                else if (current is VarPatternSyntax)
                {
                    return IrDeclaredTypeForm.Deduced;
                }
                else
                {
                    return IrDeclaredTypeForm.Named;
                }
                break;
            case ForEachStatementSyntax forEach:
                typeSyntax = forEach.Type;
                break;
            default:
                return IrDeclaredTypeForm.Named;
        }

        if (typeSyntax is null)
        {
            return IrDeclaredTypeForm.Named;
        }

        while (true)
        {
            if (typeSyntax is RefTypeSyntax refType)
            {
                typeSyntax = refType.Type;
            }
            else if (typeSyntax is ScopedTypeSyntax scopedType)
            {
                typeSyntax = scopedType.Type;
            }
            else
            {
                break;
            }
        }

        return typeSyntax.IsVar ? IrDeclaredTypeForm.Deduced : IrDeclaredTypeForm.Named;
    }
}
