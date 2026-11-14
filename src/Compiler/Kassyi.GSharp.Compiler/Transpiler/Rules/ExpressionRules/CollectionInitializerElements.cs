namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// Writes an element of a collection initializer (<c>new T { a, { b, c } }</c>, also <c>Member = { a }</c> in an
/// object initializer) as the call of the <c>Add</c> that C# binds for it, and decides what an index initializer
/// (<c>[i] = v</c>) calls (#1899).
/// </summary>
/// <remarks>
/// [WHY] C# turns each element into a call of <c>Add</c> on the collection being built, in the written order, and an
/// index initializer into an element assignment. The call has no invocation syntax, so the call rules never see it; an
/// element was dropped. An <c>Add</c> that device code can call (a <c>[GpuDeviceFunction]</c> extension method) is
/// called; any other one gets the decision a written call of the same method gets
/// (<see cref="CallTargetEmitter.TryReplaceCall"/>): an instance method of a struct has no device form yet (#451). An
/// index initializer gets that decision for its indexer setter; one that device code may call still has no element
/// access form for a struct indexer (#1033), so it is left to the element access path.
/// </remarks>
internal static class CollectionInitializerElements
{
    /// <summary>
    /// Writes the <c>Add</c> call of <paramref name="element"/> of <paramref name="collection"/> on the collection being
    /// built, as a statement: <c>&lt;Add&gt;(&amp;&lt;target&gt;, &lt;arguments&gt;);</c>, with the arguments evaluated
    /// first, in the written order.
    /// </summary>
    public static void AppendAdd(ExpressionSyntax element, InitializerExpressionSyntax collection,
        ref TranspileContext context)
    {
        if (context.Query.SemanticModel.GetCollectionInitializerAdd(element) is not { } add)
        {
            throw new InvalidOperationException("A collection initializer element binds to no Add method.");
        }

        if (CallTargetEmitter.TryReplaceCall(element, add, ref context))
        {
            context.Emitter.Writer.Append("; ");
            return;
        }

        var extension = add.ReducedFrom ?? add;
        var arguments = element.IsKind(SyntaxKind.ComplexElementInitializerExpression)
            ? ((InitializerExpressionSyntax)element).Expressions
            : SyntaxFactory.SingletonSeparatedList(element);
        // The receiver comes first, then one value per parameter. A params parameter, and an Add that reaches a barrier
        // (called through the resume protocol, which lowering writes only for a written call), have no form here
        var parameters = extension.Parameters;
        if (!CppNameResolver.IsEmittedDeviceFunction(extension, context.Emitter.Dialect) ||
            parameters.Length != arguments.Count + 1 ||
            parameters.AsStackEnumerable().Any(static p => p.IsParams) ||
            SuspensionAnalysis.IsSuspendingFunction(extension, context.Query.SemanticModel.Compilation))
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, element, UnsupportedAdd);
            context.Visitor.AppendUnsupportedPlaceholder(element);
            context.Emitter.Writer.Append("; ");
            return;
        }

        // <type> __gsharp_seq_<n> = <argument>; per argument, converted to its parameter type as C# converts it
        var temps = new int[arguments.Count];
        for (var i = 0; i < arguments.Count; i++)
        {
            temps[i] = EvaluationOrder.DeclareTemp(ref context, arguments[i], reference: false, parameters[i + 1].Type);
        }

        // <Add>(&<target> | <target>, [&]__gsharp_seq_<n>, ...);
        CppNameResolver.AppendSymbolName(extension, ref context);
        using (context.Emitter.Writer.Parentheses())
        {
            context.Emitter.Writer.AppendIf(parameters[0].RefKind != RefKind.None, "&");
            ObjectInitializerAssignments.AppendTarget(collection, ref context);
            for (var i = 0; i < temps.Length; i++)
            {
                context.Emitter.Writer.Append(',');
                context.Emitter.Writer.AppendIf(parameters[i + 1].RefKind != RefKind.None, "&");
                EvaluationOrder.AppendTempName(context.Emitter.Writer, temps[i]);
            }
        }

        context.Emitter.Writer.AppendLine(";");
    }

    /// <summary>
    /// Reports what a call of the indexer setter that <paramref name="assignment"/> (<c>[i] = v</c>) calls would report,
    /// and writes the placeholder; false when the setter can be called, or for an element of an array, which the
    /// caller writes as an element assignment.
    /// </summary>
    public static bool TryFailIndexer(AssignmentExpressionSyntax assignment, ref TranspileContext context)
    {
        // [WHY] Read through the assignment: the implicit element access has the span of its bracketed argument list,
        // so the original node of a lowered copy would be the list, which binds to nothing
        if (!context.Query.SemanticModel.TryGetOriginalNode(assignment, out var original, out var model) ||
            original is not AssignmentExpressionSyntax { Left: var target } ||
            model.GetSymbolInfo(target).Symbol is not IPropertySymbol { SetMethod: { } setter })
        {
            return false;
        }

        if (!CallTargetEmitter.TryReplaceCall(assignment, setter, ref context))
        {
            return false;
        }

        context.Emitter.Writer.Append("; ");
        return true;
    }

    private const string UnsupportedAdd =
        "a collection initializer element whose Add takes params, has optional parameters it omits, or reaches a " +
        "barrier";
}
