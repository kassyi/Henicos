namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>How a cast to a delegate type is written (<see cref="DelegateCasts.Plan"/>).</summary>
internal enum DelegateCastForm
{
    /// <summary>The target is not a delegate type: the ordinary cast.</summary>
    Ordinary,

    /// <summary>
    /// The converted expression is written alone: on CUDA a delegate is a lambda of its own type (<c>auto</c>), which
    /// names no type to cast to, as <c>Func&lt;int, int&gt; f = Inc;</c> is written.
    /// </summary>
    OmitCast
}

/// <summary>Decides how a cast to a delegate type is written.</summary>
internal static class DelegateCasts
{
    /// <summary>The form of <paramref name="node"/>.</summary>
    public static DelegateCastForm Plan(CastExpressionSyntax node, SemanticModel model, ITranspilerDialect dialect) =>
        dialect.IsCuda && GpuCodeConstraints.IsDelegate(model.GetTypeSymbol(node.Type))
            ? DelegateCastForm.OmitCast
            : DelegateCastForm.Ordinary;
}
