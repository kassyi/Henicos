namespace Kassyi.GSharp.Compiler.Dialects;

/// <summary>
/// C++ dialect for kernels that execute on one native CPU stack per GPU thread (<c>GSharpNativeBarrierModel=Stack</c>):
/// keeps <see cref="StandardCppDialect"/>'s types and expressions while disabling state-machine lowering and mapping
/// block barriers to <c>__gsharp_stack_barrier</c>.
/// </summary>
internal sealed class StackCppDialect : StandardCppDialect
{
    public override DialectCapabilities Capabilities =>
        base.Capabilities | DialectCapabilities.HardwareThreadScheduling;

    public override bool TryMapIntrinsic(ISymbol symbol, out string mappedExpression)
    {
        if (symbol is IMethodSymbol method &&
            SyncIntrinsicClassifier.Classify(method).Kind == SyncIntrinsicKind.BlockBarrier)
        {
            mappedExpression = GSharpInternalNames.NativeStack.Barrier;
            return true;
        }

        return base.TryMapIntrinsic(symbol, out mappedExpression);
    }
}
