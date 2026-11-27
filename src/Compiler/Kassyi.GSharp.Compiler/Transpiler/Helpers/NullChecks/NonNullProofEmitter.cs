namespace Kassyi.GSharp.Compiler.Transpiler.Helpers.NullChecks;

/// <summary>
/// Reports transpiler diagnostics for <see cref="NonNullProof"/> boundaries (#1409).
/// </summary>
internal static class NonNullProofEmitter
{
    /// <summary>
    /// Records a failure (GSH0003) when <paramref name="reference"/> names <paramref name="method"/> without calling it
    /// (a method group) and a call of the method checks its arguments: a call through a delegate cannot check them, and
    /// the method reads them without a check.
    /// </summary>
    public static void FailIfReferencedWithoutCall(ExpressionSyntax reference, IMethodSymbol method,
        ref TranspileContext context)
    {
        if (!CallTargetClassifier.IsCalledReference(reference) &&
            !method.IsExtensionMethod &&
            CppNameResolver.IsEmittedDeviceFunction(method, context.Emitter.Dialect) &&
            NonNullProof.ChecksArguments(method))
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, reference, NonNullProof.PassedAsDelegate);
        }
    }
}
