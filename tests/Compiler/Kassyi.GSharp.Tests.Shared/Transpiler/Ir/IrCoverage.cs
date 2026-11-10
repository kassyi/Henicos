#nullable enable
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Building;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Statements;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>Builds the IR of every GPU method of a compilation and tallies what stayed opaque.</summary>
internal static class IrCoverage
{
    /// <summary>
    /// Builds every <c>[GpuKernel]</c> and <c>[GpuDeviceFunction]</c> method declared in <paramref name="trees"/>,
    /// checks the invariants and the spans, and adds each opaque node to <paramref name="opaque"/> by
    /// <c>reason, operation kind, syntax kind</c>.
    /// </summary>
    public static int BuildAll(Compilation compilation, IEnumerable<SyntaxTree> trees,
        ConcurrentDictionary<string, int> opaque, ConcurrentBag<string> violations)
    {
        var built = 0;
        foreach (var tree in trees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(declaration) is not { } symbol || !GpuExecutionSpace.IsGpuFunction(symbol))
                {
                    continue;
                }

                if (IrBuilder.TryBuild(symbol, compilation, CancellationToken.None) is not { } method)
                {
                    continue;
                }

                built++;
                var where = Path.GetFileName(tree.FilePath) + " " + symbol.ContainingType.Name + "." + symbol.Name;
                Check(method, compilation, where, violations);
                Tally(method, opaque);
            }
        }

        return built;
    }

    // The invariants and the spans of one built method
    private static void Check(IrMethod method, Compilation compilation, string where, ConcurrentBag<string> violations)
    {
        foreach (var violation in IrInvariants.Verify(method, compilation))
        {
            violations.Add(where + ": " + violation);
        }

        foreach (var problem in FindSpansThatAreNotNodes(method, compilation))
        {
            violations.Add(where + ": " + problem);
        }
    }

    // Counts the opaque nodes of one built method by reason, operation kind and syntax kind
    private static void Tally(IrMethod method, ConcurrentDictionary<string, int> opaque) =>
        IrNodeChildren.Visit(method.Body, node =>
        {
            var key = node switch
            {
                IrOpaqueExpression expression => Key(expression.Reason, expression.Operation, expression.Syntax),
                IrOpaqueStatement statement => "statement " + Key(statement.Reason, statement.Operation,
                    statement.Syntax),
                _ => null
            };
            if (key is not null)
            {
                opaque.AddOrUpdate(key, 1, static (_, count) => count + 1);
            }
        });

    /// <summary>I2: every node that is not synthesized has the span of a node of its tree.</summary>
    public static void AssertSpansAreNodes(IrMethod method, Compilation compilation) =>
        Assert.Empty(FindSpansThatAreNotNodes(method, compilation));

    private static List<string> FindSpansThatAreNotNodes(IrMethod method, Compilation compilation)
    {
        var problems = new List<string>();
        var trees = compilation.SyntaxTrees.ToArray();
        IrNodeChildren.Visit(method.Body, node =>
        {
            var source = node.Source;
            if (source.Synthesized)
            {
                return;
            }

            var found = trees[source.TreeOrdinal].GetRoot().FindNode(source.Span, getInnermostNodeForTie: true);
            if (found.Span != source.Span)
            {
                problems.Add($"I2 at {source.Start}..{source.Start + source.Length}: no node has the span");
            }
        });
        return problems;
    }

    private static string Key(IrOpaqueReason reason, IOperation? operation, SyntaxNode syntax) =>
        $"{reason}\t{operation?.Kind.ToString() ?? "-"}\t{syntax.Kind()}{Detail(operation)}";

    // The struct creations kept opaque by their initializer are counted apart: nested member initializers are to be
    // built as nodes, and their count is followed until it is 0
    private static string Detail(IOperation? operation) => operation switch
    {
        IObjectCreationOperation { Type.IsValueType: false } => " class",
        IObjectCreationOperation { Initializer: { } initializer }
            when initializer.Initializers.Any(static member => member is IMemberInitializerOperation) =>
            " nested-member-initializer",
        IObjectCreationOperation { Initializer: { } initializer }
            when initializer.Initializers.Any(static member => member is not ISimpleAssignmentOperation) =>
            " collection-initializer",
        _ => ""
    };
}
