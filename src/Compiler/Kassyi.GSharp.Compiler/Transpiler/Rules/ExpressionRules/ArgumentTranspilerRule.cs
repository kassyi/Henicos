using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

public sealed class ArgumentTranspilerRule : SyntaxNodeTranspilerRuleBase<ArgumentSyntax>
{
    public static readonly ArgumentTranspilerRule Instance = new();

    private ArgumentTranspilerRule() { }

    public override void Transpile(ArgumentSyntax node, ref TranspileContext context)
    {
        if (node.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) || node.RefKindKeyword.IsKind(SyntaxKind.OutKeyword))
        {
            context.Writer.Append("&");
        }

        context.Visitor.Visit(node.Expression);
    }
}
