namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// Transpiles <c>this</c> into <c>(*this)</c> in a struct member function, or into <c>__gsharp_self</c> in a lowered
/// struct constructor function, so that <c>this.A</c> keeps the C# member access <c>.</c>.
/// </summary>
/// <remarks>
/// Device code reaches <c>this</c> in a struct's property accessor or <c>Dispose</c> member function, or in a
/// user-defined struct constructor emitted by <c>StructTranspiler</c> as a value-returning function.
/// </remarks>
public sealed class ThisExpressionTranspilerRule : SyntaxNodeTranspilerRuleBase<ThisExpressionSyntax>
{
    /// <inheritdoc />
    public override void Transpile(ThisExpressionSyntax node, ref TranspileContext context) =>
        context.Emitter.Writer.Append(context.Emitter.StructConstructorType != null
            ? GSharpInternalNames.Lowering.ConstructorSelf
            : "(*this)");
}
