namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>How a cast to or from a delegate type is written (<see cref="DelegateCasts.Plan"/>).</summary>
internal enum DelegateCastForm
{
    /// <summary>The target is not a delegate type, or the source needs nothing special: the ordinary cast.</summary>
    Ordinary,

    /// <summary>
    /// The converted expression is written alone: on CUDA a delegate is a lambda of its own type (<c>auto</c>), which
    /// names no type to cast to, as <c>Func&lt;int, int&gt; f = Inc;</c> is written.
    /// </summary>
    OmitCast,

    /// <summary>
    /// A delegate or method group converted to <c>object</c> or <c>Delegate</c>: a heap object that device code has no
    /// form for (GSH0003).
    /// </summary>
    HeapObject
}

/// <summary>Decides how a cast of a delegate or a method group is written.</summary>
internal static class DelegateCasts
{
    /// <summary>The form of <paramref name="node"/>.</summary>
    public static DelegateCastForm Plan(CastExpressionSyntax node, SemanticModel model, ITranspilerDialect dialect)
    {
        var target = model.GetTypeSymbol(node.Type);
        if (GpuCodeConstraints.IsHeapDelegateTarget(target) && IsDelegateValue(node.Expression, model))
        {
            return DelegateCastForm.HeapObject;
        }

        return dialect.IsCuda && GpuCodeConstraints.IsDelegate(target)
            ? DelegateCastForm.OmitCast
            : DelegateCastForm.Ordinary;
    }

    // A delegate, or a method group that converts to one
    private static bool IsDelegateValue(ExpressionSyntax expression, SemanticModel model) =>
        GpuCodeConstraints.IsDelegate(model.GetTypeSymbol(expression)) ||
        (expression is IdentifierNameSyntax or MemberAccessExpressionSyntax &&
         model.GetSymbol(expression) is IMethodSymbol);
}
