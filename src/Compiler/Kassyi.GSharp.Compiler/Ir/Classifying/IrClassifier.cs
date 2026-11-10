using Kassyi.GSharp.Compiler.Analysis;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Statements;

namespace Kassyi.GSharp.Compiler.Ir.Classifying;

/// <summary>
/// The classify pass: decides, once per call of a built method, whether the call splits the state machine of the CPU
/// paths (<see cref="IrSplitClassifier"/>) and what it may write (<see cref="DeviceFunctionEffects"/>), and records
/// both in <see cref="IrCall.Classification"/>; records on each opaque node whether its syntax holds a split point
/// (<see cref="IrOpaqueSplit"/>). The decisions do not depend on the dialect.
/// </summary>
/// <remarks>
/// The later passes read the decision about a call from the node and never ask its symbol again; a call they find
/// unclassified is an internal error (<see cref="IrCallClassification"/>). The other nodes that run code (properties,
/// operators, conversions, creations) carry no decision yet: <see cref="IrEffects"/> asks their symbols when it walks
/// them. A statement that holds a split point is either fully in the tree or under an opaque node marked
/// <see cref="IrOpaqueSplit.HoldsSplit"/>.
/// </remarks>
internal sealed class IrClassifier : IrRewriter
{
    private readonly Compilation _compilation;

    private IrClassifier(Compilation compilation) => _compilation = compilation;

    /// <summary>The body of <paramref name="method"/> with every call and opaque node classified.</summary>
    public static IrMethod Classify(IrMethod method, Compilation compilation) =>
        new IrClassifier(compilation).RewriteMethod(method);

    /// <summary>The expression <paramref name="expression"/> with every call and opaque node classified.</summary>
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

    /// <inheritdoc/>
    public override IrStatement VisitOpaqueStatement(IrOpaqueStatement statement) =>
        statement.WithSplit(SplitOf(statement.Syntax));

    /// <inheritdoc/>
    public override IrExpression VisitOpaqueExpression(IrOpaqueExpression expression) =>
        expression.WithSplit(SplitOf(expression.Syntax));

    // Whether an invocation in the syntax (lambda bodies included, as the lowering annotates them) splits
    private IrOpaqueSplit SplitOf(SyntaxNode syntax)
    {
        var model = _compilation.GetCachedSemanticModel(syntax.SyntaxTree);
        var holdsSplit = syntax.DescendantNodesAndSelf()
            .AsStackEnumerable()
            .Any((Model: model, Compilation: _compilation), static (node, state) =>
                node is InvocationExpressionSyntax invocation &&
                state.Model.GetSymbol(invocation) is IMethodSymbol callee &&
                IrSplitClassifier.Classify(callee, state.Compilation).IsSplit
            );
        return holdsSplit ? IrOpaqueSplit.HoldsSplit : IrOpaqueSplit.None;
    }
}
