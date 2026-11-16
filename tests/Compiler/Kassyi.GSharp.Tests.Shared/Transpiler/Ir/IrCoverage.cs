#nullable enable
using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Building;
using Kassyi.GSharp.Compiler.Ir.Classifying;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Statements;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>Builds the IR of every GPU method of a compilation and tallies what stayed opaque.</summary>
internal static class IrCoverage
{
    /// <summary>
    /// Builds every <c>[GpuKernel]</c> and <c>[GpuDeviceFunction]</c> method declared in <paramref name="trees"/>,
    /// checks the invariants and the spans, and adds each opaque node to <paramref name="opaque"/> under its method by
    /// node kind, reason, operation kind and syntax kind.
    /// </summary>
    public static int BuildAll(Compilation compilation, IEnumerable<SyntaxTree> trees, IrCorpusCounts opaque,
        ConcurrentBag<string> violations)
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
                Tally(method, IrCoverageInputs.Locate(tree, symbol), opaque);
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

        foreach (var violation in IrInvariants.VerifyClassified(IrClassifier.Classify(method, compilation)))
        {
            violations.Add(where + ": " + violation);
        }

        foreach (var problem in FindSpansThatAreNotNodes(method, compilation))
        {
            violations.Add(where + ": " + problem);
        }

        CheckSpellingParentheses(method, where, violations);
    }

    private static void CheckSpellingParentheses(IrMethod method, string where, ConcurrentBag<string> violations)
    {
        foreach (var parent in IrNodeChildren.DescendantsAndSelf(method.Body))
        {
            if (parent is IrExpression parentExpr && !parentExpr.Source.Synthesized && parentExpr.Parentheses > 0)
            {
                foreach (var child in IrNodeChildren.Of(parent))
                {
                    if (child is IrExpression childExpr && !childExpr.Source.Synthesized && childExpr.Parentheses > 0 &&
                        childExpr.Source.Span == parentExpr.Source.Span)
                    {
                        violations.Add(
                            $"{where}: spelling at {parentExpr.Source.Start}.." +
                            $"{parentExpr.Source.Start + parentExpr.Source.Length}: " +
                            "a parent and its child with the same syntax both have parentheses");
                    }
                }
            }
        }
    }

    // Counts the opaque nodes of one built method by node kind, reason, operation kind and syntax kind
    private static void Tally(IrMethod method, IrCorpusMethod where, IrCorpusCounts opaque)
    {
        foreach (var node in IrNodeChildren.DescendantsAndSelf(method.Body))
        {
            switch (node)
            {
                case IrOpaqueExpression expression:
                    Add(opaque, where, "expression", expression.Reason, expression.Operation, expression.Syntax);
                    break;
                case IrOpaqueStatement statement:
                    Add(opaque, where, "statement", statement.Reason, statement.Operation, statement.Syntax);
                    break;
            }
        }
    }

    /// <summary>I2: every node that is not synthesized has the span of a node of its tree.</summary>
    public static void AssertSpansAreNodes(IrMethod method, Compilation compilation) =>
        Assert.Empty(FindSpansThatAreNotNodes(method, compilation));

    private static List<string> FindSpansThatAreNotNodes(IrMethod method, Compilation compilation)
    {
        var trees = compilation.SyntaxTrees.ToArray();
        return IrNodeChildren.DescendantsAndSelf(method.Body)
            .Select(static node => node.Source)
            .Where(source => !source.Synthesized &&
                             trees[source.TreeOrdinal].GetRoot()
                                 .FindNode(source.Span, getInnermostNodeForTie: true).Span != source.Span)
            .Select(static source => $"I2 at {source.Start}..{source.Start + source.Length}: no node has the span")
            .ToList();
    }

    private static void Add(IrCorpusCounts opaque, IrCorpusMethod where, string node, IrOpaqueReason reason,
        IOperation? operation, SyntaxNode syntax)
    {
        (string Key, string Value)[] fields =
        [
            ("node", node), ("reason", reason.ToString()), ("operation", operation?.Kind.ToString() ?? "-"),
            ("syntax", syntax.Kind().ToString())
        ];
        opaque.Add(where, Detail(operation) is { } detail ? [.. fields, ("detail", detail)] : fields);
    }

    // The struct creations kept opaque by their initializer are counted apart: nested member initializers are to be
    // built as nodes, and their count is followed until it is 0
    private static string? Detail(IOperation? operation) => operation switch
    {
        IObjectCreationOperation { Type.IsValueType: false } => "class",
        IObjectCreationOperation { Initializer: { } initializer }
            when initializer.Initializers.Any(static member => member is IMemberInitializerOperation) =>
            "nested-member-initializer",
        IObjectCreationOperation { Initializer: { } initializer }
            when initializer.Initializers.Any(static member => member is not ISimpleAssignmentOperation) =>
            "collection-initializer",
        _ => null
    };
}
