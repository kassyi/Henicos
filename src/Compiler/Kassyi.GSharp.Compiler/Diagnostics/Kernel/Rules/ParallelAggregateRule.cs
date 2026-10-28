namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>
/// Reports an <c>AsParallel()</c> aggregate that runs sequentially in each thread (GSH0099, Warning A of #624), with the
/// reason <see cref="ParallelAggregatePlanner"/> found. The result is the same (every thread receives the whole sum); only
/// the speed differs. Reported in every dialect, as it tells what CUDA does.
/// </summary>
internal sealed class ParallelAggregateRule : ISyntaxValidationRule<InvocationExpressionSyntax>
{
    public static readonly ParallelAggregateRule Instance = new();

    public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(GSharpDiagnostics.ParallelAggregateRunsSequentially);

    private ParallelAggregateRule()
    {
    }

    public void Analyze(InvocationExpressionSyntax node, ref ValidationContext context)
    {
        var model = context.SemanticModel;
        if (model.GetSymbol(node) is not IMethodSymbol symbol ||
            !StackLinqChainExtractor.TryExtract(node, model, symbol, out var chain) ||
            !chain.IsParallel)
        {
            return;
        }

        var plan = ParallelAggregatePlanner.Plan(node, model);
        if (plan.Strategy != ParallelAggregateStrategy.PerThread)
        {
            return;
        }

        // From the name of the terminal operator to the end of its arguments: Sum()
        var start = node.Expression is MemberAccessExpressionSyntax access ? access.Name.SpanStart : node.SpanStart;
        var location = Location.Create(node.SyntaxTree, TextSpan.FromBounds(start, node.Span.End));
        context.ReportDiagnostic(GSharpDiagnostics.ParallelAggregateRunsSequentially, location,
            $"AsParallel().{symbol.Name}()", ParallelAggregatePlanner.Describe(plan.Fallback));
    }
}
