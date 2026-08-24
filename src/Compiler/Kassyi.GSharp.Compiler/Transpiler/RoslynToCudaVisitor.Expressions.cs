using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Compiler.Transpiler;

public partial class RoslynToCudaVisitor
{
    public override void VisitExpressionStatement(ExpressionStatementSyntax node)
    {
        Visit(node.Expression);
        Writer.AppendSemiColon();
    }

    public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
        => ExpressionTranspilers.TranspileAssignmentExpression(node, this);

    public override void VisitBinaryExpression(BinaryExpressionSyntax node)
        => ExpressionTranspilers.TranspileBinaryExpression(node, this);

    public override void VisitIdentifierName(IdentifierNameSyntax node)
    {
        var symbol = SemanticModel.GetSymbolInfo(node).Symbol;
        if (symbol is IFieldSymbol { IsConst: true, HasConstantValue: true } field)
        {
            Writer.Append(field.ConstantValue!.ToString());
            switch (field.Type.SpecialType)
            {
                case SpecialType.System_UInt64:
                    Writer.Append("ULL");
                    break;
                case SpecialType.System_UInt32:
                    Writer.Append("U");
                    break;
                case SpecialType.System_Int64:
                    Writer.Append("LL");
                    break;
            }

            return;
        }

        Writer.Append(node.Identifier.Text);
    }

    public override void VisitLiteralExpression(LiteralExpressionSyntax node) => Writer.Append(node.Token.Text);

    public override void VisitElementAccessExpression(ElementAccessExpressionSyntax node)
        => ExpressionTranspilers.TranspileElementAccessExpression(node, this);

    public override void VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
        => ExpressionTranspilers.TranspilePrefixUnaryExpression(node, this);

    public override void VisitCastExpression(CastExpressionSyntax node)
        => ExpressionTranspilers.TranspileCastExpression(node, this);

    public override void VisitParenthesizedExpression(ParenthesizedExpressionSyntax node)
        => ExpressionTranspilers.TranspileParenthesizedExpression(node, this);

    public override void VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node)
        => ExpressionTranspilers.TranspilePostfixUnaryExpression(node, this);

    public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        => ExpressionTranspilers.TranspileMemberAccessExpression(node, this);

    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        => ExpressionTranspilers.TranspileInvocationExpression(node, this);

    public override void VisitArgument(ArgumentSyntax node)
        => ExpressionTranspilers.TranspileArgument(node, this);

    public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        => ExpressionTranspilers.TranspileObjectCreationExpression(node, this);

    public override void VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node)
        => ExpressionTranspilers.TranspileImplicitObjectCreationExpression(node, this);
}
