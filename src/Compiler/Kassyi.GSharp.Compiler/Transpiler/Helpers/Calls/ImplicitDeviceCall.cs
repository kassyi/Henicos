namespace Kassyi.GSharp.Compiler.Transpiler.Helpers.Calls;

/// <summary>
/// The call of a method that C# makes for a form without a call in the source: the builder of a collection expression
/// (<see cref="CollectionBuilderMethod"/>) and the <c>GetPinnableReference</c> of a <c>fixed</c> statement
/// (<see cref="PinnableReferenceMethod"/>). The target is decided as for a written call
/// (<see cref="CallTargetClassifier"/>), and only a static <c>[GpuDeviceFunction]</c> is called.
/// </summary>
/// <remarks>
/// [WHY] The analyses that follow calls through the device functions (barriers and warp shuffles, dynamic shared
/// memory) see the calls that Roslyn shows as operations, and Roslyn 4.8 shows neither of these; a callee that needs
/// them is reported instead of being called without its frame or its table.
/// </remarks>
internal static class ImplicitDeviceCall
{
    /// <summary>The GSH0003 text for an implicit call of a generic method.</summary>
    public const string GenericMethod =
        "implicit call of a generic method (collection builder or GetPinnableReference)";

    /// <summary>The GSH0003 text for an implicit call of a method that is not a static device function.</summary>
    public const string NotStaticDeviceFunction =
        "implicit call of a method that is not a static [GpuDeviceFunction] method (collection builder or " +
        "GetPinnableReference)";

    /// <summary>
    /// The GSH0003 text for an implicit call of a device function that reaches a barrier or a warp shuffle.
    /// </summary>
    public const string SuspendingMethod =
        "implicit call of a device function that reaches a barrier or a warp shuffle (collection builder or " +
        "GetPinnableReference)";

    /// <summary>The GSH0003 text for an implicit call of a device function that uses dynamic shared memory.</summary>
    public const string DynamicSharedMemoryMethod =
        "implicit call of a device function that uses dynamic shared memory (collection builder or " +
        "GetPinnableReference)";

    /// <summary>
    /// Whether <paramref name="node"/> can call <paramref name="method"/>; when it cannot, reports why at
    /// <paramref name="node"/> and writes the placeholder.
    /// </summary>
    public static bool CheckCallable(SyntaxNode node, IMethodSymbol method, ref TranspileContext context)
    {
        // GSH0107 and GSH0109 for a method without a device definition, as for a written call
        if (CallTargetEmitter.TryReplaceCall(node, method, ref context))
        {
            return false;
        }

        if (GetProblem(method.ReducedFrom ?? method, ref context) is not { } problem)
        {
            return true;
        }

        context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, node, problem);
        context.Visitor.AppendUnsupportedPlaceholder(node);
        return false;
    }

    /// <summary>
    /// Writes the C++ name of the function that calls <paramref name="method"/> (an extension method in its static
    /// form), which <see cref="CheckCallable"/> accepted.
    /// </summary>
    public static void AppendName(IMethodSymbol method, ref TranspileContext context)
    {
        var function = method.ReducedFrom ?? method;
        StructOrdering.RegisterUsedStructIfApplicable(function.ReturnType, ref context);
        CppNameResolver.AppendSymbolName(function, ref context);
    }

    private static string? GetProblem(IMethodSymbol function, ref TranspileContext context)
    {
        var compilation = context.Query.SemanticModel.Compilation;
        if (function.IsGenericMethod)
        {
            return GenericMethod;
        }

        if (!CppNameResolver.IsEmittedDeviceFunction(function, context.Emitter.Dialect))
        {
            return NotStaticDeviceFunction;
        }

        if (SuspensionAnalysis.IsSuspendingFunction(function, compilation))
        {
            return SuspendingMethod;
        }

        return DynamicSharedMemoryEmitter.TakesTable(function, context.Emitter.Dialect, compilation)
            ? DynamicSharedMemoryMethod
            : null;
    }
}
