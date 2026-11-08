namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>
/// Reports an <c>AsParallel()</c> aggregate that runs sequentially in each thread (GSH0099, Warning A of #624), with the
/// reason <see cref="ParallelAggregatePlanner"/> found, and one whose inputs are the same in the whole grid in a kernel that
/// is not cooperative, so that every block reads the whole sequence (GSH0100, Warning B). The result is the same either
/// way; only the speed differs. Reported in every dialect, as it tells what CUDA does.
/// </summary>
internal sealed class ParallelAggregateRule : ISyntaxValidationRule<InvocationExpressionSyntax>
{
    public static readonly ParallelAggregateRule Instance = new();

    public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(GSharpDiagnostics.ParallelAggregateRunsSequentially,
            GSharpDiagnostics.ParallelAggregateNotCooperative);

    private ParallelAggregateRule()
    {
    }

    public void Analyze(InvocationExpressionSyntax node, ref ValidationContext context)
    {
        var model = context.SemanticModel;
        if (model.GetSymbol(node) is not IMethodSymbol symbol || !StackLinqHelper.IsStackLinqMethod(symbol))
        {
            return;
        }

        ParallelAggregatePlan plan;
        if (StackLinqChainExtractor.TryExtract(node, model, symbol, out var chain))
        {
            if (!chain.IsParallel)
            {
                return;
            }

            plan = ParallelAggregatePlanner.Plan(node, model);
        }
        else if (IsParallelSource(symbol))
        {
            // The unfused call runs in each thread (StackLinqTranspilerRule)
            plan = new ParallelAggregatePlan(ParallelAggregateStrategy.PerThread, ParallelAggregateFallback.UnfusedChain,
                IsGridUniform: false);
        }
        else
        {
            return;
        }

        // From the name of the terminal operator to the end of its arguments: Sum()
        var start = node.Expression is MemberAccessExpressionSyntax access ? access.Name.SpanStart : node.SpanStart;
        var location = Location.Create(node.SyntaxTree, TextSpan.FromBounds(start, node.Span.End));
        if (plan.Strategy == ParallelAggregateStrategy.PerThread)
        {
            context.ReportDiagnostic(GSharpDiagnostics.ParallelAggregateRunsSequentially, location,
                $"AsParallel().{symbol.Name}()", ParallelAggregatePlanner.Describe(plan.Fallback));
        }
        else if (plan.Strategy == ParallelAggregateStrategy.Block && plan.IsGridUniform)
        {
            // [WHY] The Grid strategy needs a grid sync, which only a cooperative launch allows; G# does not choose the
            // launch for the user (semantics charter), so it says what the marker would give instead
            context.ReportDiagnostic(GSharpDiagnostics.ParallelAggregateNotCooperative, location,
                context.MethodSymbol.Name, $"AsParallel().{symbol.Name}()");
        }
    }

    // A terminal called on an AsParallel() sequence (ParallelStackEnumerable), as its receiver or first argument
    private static bool IsParallelSource(IMethodSymbol symbol)
    {
        var source = symbol.ReducedFrom is not null
            ? symbol.ReceiverType
            : symbol.Parameters.Length > 0 ? symbol.Parameters[0].Type : null;
        return source is { Name: "ParallelStackEnumerable" } && StackLinqHelper.IsStackLinqType(source);
    }
}
