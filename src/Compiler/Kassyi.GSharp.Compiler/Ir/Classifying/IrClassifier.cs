using Kassyi.GSharp.Compiler.Analysis;
using Kassyi.GSharp.Compiler.Ir.Expressions;

namespace Kassyi.GSharp.Compiler.Ir.Classifying;

/// <summary>
/// The classify pass: decides, once per call of a built method, whether the call splits the state machine of the CPU
/// paths (<see cref="IrSplitClassifier"/>) and what it may write (<see cref="DeviceFunctionEffects"/>), and records
/// both in <see cref="IrCall.Classification"/>. The decisions do not depend on the dialect.
/// </summary>
/// <remarks>
/// The later passes read the decisions from the nodes and never ask the symbol again; a call they find unclassified
/// is an internal error (<see cref="IrCallClassification"/>). Calls inside opaque nodes are not part of the tree and
/// stay with the syntax they came from.
/// </remarks>
internal sealed class IrClassifier : IrRewriter
{
    private readonly Compilation _compilation;

    private IrClassifier(Compilation compilation) => _compilation = compilation;

    /// <summary>The body of <paramref name="method"/> with every call classified.</summary>
    public static IrMethod Classify(IrMethod method, Compilation compilation) =>
        new IrClassifier(compilation).RewriteMethod(method);

    /// <summary>The expression <paramref name="expression"/> with every call classified.</summary>
    public static IrExpression Classify(IrExpression expression, Compilation compilation) =>
        new IrClassifier(compilation).VisitExpression(expression);

    /// <inheritdoc/>
    public override IrExpression VisitCall(IrCall call)
    {
        var method = call.Method;
        var classification = IrCallClassification.Of(
            IrSplitClassifier.Classify(method, _compilation),
            DeviceFunctionEffects.Classify(method, _compilation)
        );
        return call.Update(VisitOptional(call.Receiver), VisitArguments(call.Arguments), classification);
    }
}
