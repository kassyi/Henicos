using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// A <see cref="SideEffectFreeChecker"/> that also rejects what may throw or build a value the rewrites do not copy:
/// division and remainder, object and tuple creation, <c>switch</c> expressions and <c>default</c>.
/// </summary>
internal sealed class PureExpressionChecker(EffectScope scope) : SideEffectFreeChecker(scope)
{
    /// <inheritdoc/>
    public override void VisitBinaryExpression(BinaryExpressionSyntax node)
    {
        if (node.Kind() is SyntaxKind.DivideExpression or SyntaxKind.ModuloExpression)
        {
            Reject();
            return;
        }

        base.VisitBinaryExpression(node);
    }

    /// <inheritdoc/>
    public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node) => Reject();

    /// <inheritdoc/>
    public override void VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node) => Reject();

    /// <inheritdoc/>
    public override void VisitTupleExpression(TupleExpressionSyntax node) => Reject();

    /// <inheritdoc/>
    public override void VisitSwitchExpression(SwitchExpressionSyntax node) => Reject();

    /// <inheritdoc/>
    public override void VisitDefaultExpression(DefaultExpressionSyntax node) => Reject();
}
