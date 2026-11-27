namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// Transpiles <c>this</c> into <c>(*this)</c> in a struct member function, <c>__gsharp_self</c> in a lowered struct
/// constructor function, or <c>(*__gsharp_this)</c> in a struct instance device function, so that <c>this.A</c> keeps
/// the C# member access <c>.</c>.
/// </summary>
public sealed class ThisExpressionTranspilerRule : SyntaxNodeTranspilerRuleBase<ThisExpressionSyntax>
{
    /// <inheritdoc />
    public override void Transpile(ThisExpressionSyntax node, ref TranspileContext context) =>
        AppendSelf(ref context);

    /// <summary>
    /// Writes the C++ struct value expression for <see langword="this"/> under the active
    /// <see cref="Emitter.StructSelf"/>.
    /// </summary>
    internal static void AppendSelf(ref TranspileContext context)
    {
        switch (context.Emitter.StructSelf.Form)
        {
            case SelfForm.ConstructorSelf:
                context.Emitter.Writer.Append(GSharpInternalNames.Lowering.ConstructorSelf);
                break;
            case SelfForm.ReceiverParameter:
                context.Emitter.Writer.Append("(*")
                    .Append(GSharpInternalNames.Lowering.ReceiverParameter)
                    .Append(')');
                break;
            default:
                context.Emitter.Writer.Append("(*this)");
                break;
        }
    }
}
