using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Splitting;

namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Checks that the split annotations of the syntax and the splits of the IR agree on a statement the split pass of the
/// IR writes out, in both directions: the syntax passes do not visit such a statement again, so a call they would split
/// must already be at the top of a statement.
/// </summary>
internal static class IrSplitAnnotationCheck
{
    /// <summary>
    /// Whether the syntax of each call the plan splits carries the annotation the lowering puts on such a call.
    /// </summary>
    public static bool SplitsAreAnnotated(ImmutableArray<IrSpillStep> steps, IrSyntaxIndex index)
    {
        foreach (var step in steps)
        {
            switch (step)
            {
                case IrSpillSplit { Node: IrCall call }:
                    if (!index.TryGet(call.Source, out var syntax) || !IsAnnotatedAs(syntax, call))
                    {
                        return false;
                    }

                    break;
                case IrSpillConditional conditional:
                    foreach (var branch in conditional.Branches)
                    {
                        if (!SplitsAreAnnotated(branch, index))
                        {
                            return false;
                        }
                    }

                    break;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether every annotated call of <paramref name="statement"/> is a statement of its own (<c>c;</c>) or the
    /// initializer of a temporary of a split (<c>var t = c;</c>), the forms the state machine splits on.
    /// </summary>
    /// <param name="statement">A statement written by the split pass of the IR.</param>
    /// <param name="conditionOnly">
    /// Whether only the condition of <paramref name="statement"/>, an <see langword="if"/> whose branches are not part
    /// of the plan, is checked.
    /// </param>
    public static bool SplitsAreAtTheTop(StatementSyntax statement, bool conditionOnly)
    {
        SyntaxNode checkedNode = conditionOnly && statement is IfStatementSyntax @if ? @if.Condition : statement;
        return checkedNode.DescendantNodesAndSelf()
            .AsStackEnumerable()
            .All(static node => node is not InvocationExpressionSyntax invocation ||
                                !BarrierHelper.IsPhaseBarrier(invocation) ||
                                IsAtTheTop(invocation));
    }

    private static bool IsAnnotatedAs(SyntaxNode syntax, IrCall call)
    {
        if (syntax is not InvocationExpressionSyntax ||
            LoweringAnnotations.CreateSplitAnnotation(call.Classification.Split, call.Method.ReturnsVoid) is not
            {
                Kind: { } kind
            } expected)
        {
            return false;
        }

        foreach (var annotation in syntax.GetAnnotations(kind))
        {
            if (annotation.Data == expected.Data)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAtTheTop(InvocationExpressionSyntax invocation) => invocation.Parent switch
    {
        ExpressionStatementSyntax => true,
        EqualsValueClauseSyntax
        {
            Parent: VariableDeclaratorSyntax
            {
                Parent.Parent: LocalDeclarationStatementSyntax
            } declarator
        } => declarator.Identifier.ValueText.StartsWith(GSharpInternalNames.Lowering.WarpTempPrefix,
                 StringComparison.Ordinal) ||
             declarator.Identifier.ValueText.StartsWith(GSharpInternalNames.Lowering.CallTempPrefix,
                 StringComparison.Ordinal),
        _ => false
    };
}
