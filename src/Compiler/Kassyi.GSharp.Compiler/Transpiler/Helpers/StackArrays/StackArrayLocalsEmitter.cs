namespace Kassyi.GSharp.Compiler.Transpiler.Helpers.StackArrays;

/// <summary>
/// Writes stack-array span views for the syntax transpiler (<see cref="StackArrayLocals"/>).
/// </summary>
internal static class StackArrayLocalsEmitter
{
    /// <summary>
    /// Writes <paramref name="expression"/> as a span: a stack array local becomes
    /// <c>__gsharp_stackalloc_span(name)</c>, a <c>GSharpArray&lt;T&gt;</c> over it, for the receiver of a span member and
    /// for a template parameter that deduces <c>GSharpArray&lt;T&gt;</c> (StackLinq), which a C++ array does not convert to.
    /// </summary>
    public static void AppendView(ExpressionSyntax expression, ref TranspileContext context)
    {
        if (!StackArrayLocals.NeedsView(expression, context.Query.SemanticModel))
        {
            context.Visitor.Visit(expression);
            return;
        }

        context.Emitter.Writer.Append(GSharpInternalNames.Prelude.StackAllocSpan);
        using (context.Emitter.Writer.Parentheses())
        {
            context.Visitor.Visit(expression);
        }
    }
}
