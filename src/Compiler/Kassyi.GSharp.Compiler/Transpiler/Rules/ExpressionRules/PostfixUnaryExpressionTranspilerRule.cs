using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

public sealed class PostfixUnaryExpressionTranspilerRule : SyntaxNodeTranspilerRuleBase<PostfixUnaryExpressionSyntax>
{
    public static readonly PostfixUnaryExpressionTranspilerRule Instance = new();

    private PostfixUnaryExpressionTranspilerRule() { }

    public override void Transpile(PostfixUnaryExpressionSyntax node, ref TranspileContext context)
    {
        context.Visitor.Visit(node.Operand);
        context.Writer.Append(node.OperatorToken.Text);
    }
}
