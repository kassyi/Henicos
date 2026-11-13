namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// Transpiles target-typed implicit object creation expressions (<c>new(...)</c>) into target dialect C++ struct initializers.
/// </summary>
/// <remarks>
/// <para><b>Thread Safety:</b> This rule is stateless and thread-safe.</para>
/// <para><b>Execution Target:</b> Host-only (Roslyn compiler environment).</para>
/// </remarks>
public sealed class ImplicitObjectCreationExpressionTranspilerRule : SyntaxNodeTranspilerRuleBase<ImplicitObjectCreationExpressionSyntax>
{
    /// <inheritdoc />
    public override void Transpile(ImplicitObjectCreationExpressionSyntax node, ref TranspileContext context) =>
        ObjectCreationHelper.TranspileObjectCreation(node, ref context);
}
