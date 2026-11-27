using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Statements;

namespace Kassyi.GSharp.Compiler.Ir.Bodies;

/// <summary>
/// The walk over a classified body that collects the dialect-independent facts used to decide eligibility before the
/// decisions pass: mutant switches, split calls, opaque nodes, removed calls, macros, and shared-memory locals.
/// </summary>
internal static class IrBodyScan
{
    /// <summary>
    /// Walks the body of <paramref name="method"/> once in pre-order (not inside an opaque node or a call that C#
    /// removes) and returns the recorded facts.
    /// </summary>
    public static IrBodyScanResult Scan(IrMethod method, Compilation compilation)
    {
        var collector = new Collector();
        Walk(method.Body, compilation, collector);
        return new IrBodyScanResult(collector.HasStrykerMutant, collector.SplitCalls.ToImmutable(),
            collector.FirstOpaqueDetail ?? "", collector.HasRemovedCall, collector.FirstMacroDetail ?? "",
            collector.HasSharedMemoryLocal);
    }

    private static void Walk(IrNode node, Compilation compilation, Collector collector)
    {
        switch (node)
        {
            // C# evaluates nothing of a removed call, its arguments included, so nothing below it counts as a split
            case IrCall call when IsRemoved(call, compilation):
                collector.HasRemovedCall = true;
                return;
            case IrCall call:
                CheckCall(call, compilation, collector);
                break;
            case IrOpaqueStatement statement:
                collector.FirstOpaqueDetail ??= statement.Reason + "/" + statement.Syntax.Kind();
                break;
            case IrOpaqueExpression expression:
                collector.FirstOpaqueDetail ??= expression.Reason + "/" + expression.Syntax.Kind();
                break;
            case IrLocalDeclaration declaration:
                CheckSharedMemory(declaration.Variable, collector);
                break;
            case IrVariableReference { IsDeclaration: true } reference:
                CheckSharedMemory(reference.Variable, collector);
                break;
        }

        foreach (var child in IrNodeChildren.Of(node))
        {
            Walk(child, compilation, collector);
        }
    }

    private static void CheckCall(IrCall call, Compilation compilation, Collector collector)
    {
        var method = call.Method;
        if (StrykerSwitchMethods.IsSwitchMethod(method, compilation))
        {
            collector.HasStrykerMutant = true;
        }

        var split = call.Classification.Split;
        if (split.IsSplit)
        {
            collector.SplitCalls.Add(split.Kind);
        }

        var macro = GpuMacroClassifier.Classify(method).Kind;
        if (macro != GpuMacroKind.None)
        {
            collector.FirstMacroDetail ??= macro.ToString();
        }
    }

    private static bool IsRemoved(IrCall call, Compilation compilation) =>
        RemovedCalls.IsRemoved(call.Method,
            SemanticModelExtensions.GetSyntaxTrees(compilation)[call.Source.TreeOrdinal]);

    private static void CheckSharedMemory(IrVariable variable, Collector collector)
    {
        if (variable.Type.IsSharedMemoryType())
        {
            collector.HasSharedMemoryLocal = true;
        }
    }

    private sealed class Collector
    {
        public bool HasStrykerMutant { get; set; }
        public ImmutableArray<IrSplitKind>.Builder SplitCalls { get; } = ImmutableArray.CreateBuilder<IrSplitKind>();
        public string? FirstOpaqueDetail { get; set; }
        public bool HasRemovedCall { get; set; }
        public string? FirstMacroDetail { get; set; }
        public bool HasSharedMemoryLocal { get; set; }
    }
}
