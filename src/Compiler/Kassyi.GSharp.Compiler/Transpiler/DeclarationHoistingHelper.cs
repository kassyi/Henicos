namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Helper class for hoisting inline variable declaration expressions to separate statements in CUDA C++.
/// </summary>
internal static class DeclarationHoistingHelper
{
    public static void HoistDeclarationExpressions(SyntaxNode node, ref TranspileContext context)
    {
        // [WHY] C++ does not support inline declaration expressions (e.g. out-variable declarations inside conditions or calls).
        // Pre-emitting them as declarations in the surrounding scope guarantees variable availability and correct evaluation semantics.
        foreach (var desc in node.DescendantNodes(n => n == node || n is not BlockSyntax))
        {
            if (desc is DeclarationExpressionSyntax { Designation: SingleVariableDesignationSyntax singleVar } declExpr)
            {
                var typeSymbol = context.SemanticModel.GetTypeInfo(declExpr).Type;
                if (typeSymbol != null)
                {
                    context.AppendType(typeSymbol);
                }
                else
                {
                    context.Writer.Append("auto");
                }
                context.Writer.AppendLine($" {singleVar.Identifier.Text};");
            }
        }
    }
}
