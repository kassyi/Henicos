namespace Kassyi.GSharp.Compiler;

/// <summary>
/// Identifies which <c>Kassyi.GSharp.Core.Gpu</c> macro, and which of its overloads, a method symbol is, so that the
/// transpiler rules and the analyzers share one definition of the macro signatures.
/// </summary>
internal static class GpuMacroClassifier
{
    public static GpuMacroCall Classify(IMethodSymbol symbol)
    {
        // A user's own type that is also named Gpu is not the macro host
        if (!symbol.ContainingNamespace.IsCoreNamespace())
        {
            return default;
        }

        var containingType = symbol.ContainingType;
        if (containingType is { Name: TypeConstants.GpuMacros.Hint, ContainingType.Name: TypeConstants.GpuType })
        {
            return symbol.Name == TypeConstants.GpuMacros.Unroll
                ? new GpuMacroCall(GpuMacroKind.HintUnroll, symbol)
                : default;
        }

        if (containingType?.Name != TypeConstants.GpuType)
        {
            return default;
        }

        var parameterCount = symbol.Parameters.Length;
        return symbol.Name switch
        {
            // ForUnroll(start, count, body) / ForUnroll(ref ctx, start, count, body)
            TypeConstants.GpuMacros.ForUnroll => parameterCount == 4
                ? new GpuMacroCall(GpuMacroKind.ForUnroll, symbol, hasContext: true, bodyOrdinal: 3)
                : new GpuMacroCall(GpuMacroKind.ForUnroll, symbol, bodyOrdinal: 2),
            // GridStrideLoop(maxBounds, body) / GridStrideLoop(ref ctx, maxBounds, body)
            TypeConstants.GpuMacros.GridStrideLoop => parameterCount == 3
                ? new GpuMacroCall(GpuMacroKind.GridStrideLoop, symbol, hasContext: true, bodyOrdinal: 2)
                : new GpuMacroCall(GpuMacroKind.GridStrideLoop, symbol, bodyOrdinal: 1),
            TypeConstants.GpuMacros.SingleThread => new GpuMacroCall(GpuMacroKind.SingleThread, symbol, bodyOrdinal: 0),
            // SyncGrid(body) / SyncGrid(mode): both take one parameter, told apart by its type
            TypeConstants.GpuMacros.SyncGrid => symbol.Parameters is [{ Type.TypeKind: TypeKind.Delegate }]
                ? new GpuMacroCall(GpuMacroKind.SyncGrid, symbol, bodyOrdinal: 0)
                : new GpuMacroCall(GpuMacroKind.SyncGrid, symbol),
            TypeConstants.GpuMacros.InlinePtx => new GpuMacroCall(GpuMacroKind.InlinePtx, symbol),
            TypeConstants.GpuMacros.Constant => new GpuMacroCall(GpuMacroKind.Constant, symbol),
            TypeConstants.GpuMacros.NotNull => new GpuMacroCall(GpuMacroKind.NotNull, symbol),
            _ => default
        };
    }

    /// <summary>
    /// The macro whose body <paramref name="lambda"/> is (the lambda written as its body argument), or the default call
    /// (<see cref="GpuMacroKind.None"/>) for any other lambda. A body runs within the call; it is never stored.
    /// </summary>
    public static GpuMacroCall ClassifyBody(SyntaxNode lambda, SemanticModel semanticModel)
    {
        if (lambda.Parent is not ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax macro } list } argument ||
            semanticModel.GetSymbol(macro) is not IMethodSymbol symbol)
        {
            return default;
        }

        var call = Classify(symbol);
        return call.HasBody && call.GetArgument(list.Arguments, call.BodyOrdinal) == argument ? call : default;
    }
}
