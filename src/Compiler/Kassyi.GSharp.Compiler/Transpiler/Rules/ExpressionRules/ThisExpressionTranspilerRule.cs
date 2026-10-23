namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>Transpiles <c>this</c> into <c>(*this)</c>, so that <c>this.A</c> keeps the C# member access <c>.</c>.</summary>
/// <remarks>
/// Device code reaches <c>this</c> only in the getter of a computed property, which <c>StructTranspiler</c> emits as a
/// <c>const</c> member function of the struct.
/// </remarks>
public sealed class ThisExpressionTranspilerRule : SyntaxNodeTranspilerRuleBase<ThisExpressionSyntax>
{
    /// <inheritdoc />
    public override void Transpile(ThisExpressionSyntax node, ref TranspileContext context) =>
        context.Emitter.Writer.Append("(*this)");
}
