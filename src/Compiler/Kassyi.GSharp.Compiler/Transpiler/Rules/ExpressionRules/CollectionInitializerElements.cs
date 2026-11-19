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
    /// Writes the <c>Add</c> call of <paramref name="element"/> of <paramref name="collection"/> on the collection
    /// being built, as a statement: <c>&lt;Add&gt;(&amp;&lt;target&gt;, &lt;arguments&gt;);</c>, with the arguments
    /// evaluated first, in the written order.
    /// </summary>
    public static void AppendAdd(ExpressionSyntax element, InitializerExpressionSyntax collection,
        ref TranspileContext context) =>
        Write(element, collection, Plan(element, context.Query.SemanticModel, context.Emitter.Dialect), ref context);

    /// <summary>
    /// Decides what <paramref name="element"/>, an element of a collection initializer (also of a lowered tree),
    /// writes: the <c>Add</c> C# binds for it and its arguments in parameter order, or why it has no call.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The initializer does not map to the source (lowering changed its elements), or the element binds to no
    /// <c>Add</c>.
    /// </exception>
    public static CollectionAddPlan Plan(ExpressionSyntax element, SemanticModel model, ITranspilerDialect dialect)
    {
        // The element of the user's source: lowering may have replaced the element itself with a temporary
        if (!model.TryGetOriginalInitializerElement(element, out var original, out var originalModel))
        {
            throw new InvalidOperationException(
                "A collection initializer element whose initializer does not map to the source.");
        }

        if (originalModel.GetCollectionInitializerSymbolInfo(original).Symbol is not IMethodSymbol add)
        {
            throw new InvalidOperationException("A collection initializer element binds to no Add method.");
        }

        // [WHY] Decided on the element of the source: a temporary in place of the element is no call that the user
        // wrote, and a failure is reported at the element, where the analyzer reports it
        var method = add.ReducedFrom ?? add;
        var target = CallTargetClassifier.Classify(add, original, originalModel, dialect) with { ReportAt = original };
        if (target.IsFailure || target.Kind == CallTargetKind.UnsupportedIntrinsic)
        {
            return new CollectionAddPlan(CollectionAddFailure.CallTargetReplaced, add, target, method, false, []);
        }

        var values = element.IsKind(SyntaxKind.ComplexElementInitializerExpression)
            ? ((InitializerExpressionSyntax)element).Expressions
            : SyntaxFactory.SingletonSeparatedList(element);
        // The receiver comes first, then one value per parameter. A params parameter, and an Add that reaches a barrier
        // (called through the resume protocol, which lowering writes only for a written call), have no form here
        var parameters = method.Parameters;
        if (!CppNameResolver.IsEmittedDeviceFunction(method, dialect) ||
            parameters.Length != values.Count + 1 ||
            parameters.AsStackEnumerable().Any(static p => p.IsParams) ||
            SuspensionAnalysis.IsSuspendingFunction(method, model.Compilation))
        {
            return new CollectionAddPlan(CollectionAddFailure.Unsupported, add, target, method, false, []);
        }

        // The conversions C# applies to the arguments are read from the element of the source
        var originalValues = original.IsKind(SyntaxKind.ComplexElementInitializerExpression)
            ? ((InitializerExpressionSyntax)original).Expressions
            : SyntaxFactory.SingletonSeparatedList(original);
        var arguments = new CollectionAddArgument[values.Count];
        for (var i = 0; i < arguments.Length; i++)
        {
            var parameter = parameters[i + 1];
            arguments[i] = new CollectionAddArgument(values[i], parameter.Type, parameter.RefKind != RefKind.None,
                GetLostConversion(values[i], model, originalValues[i], originalModel));
        }

        return new CollectionAddPlan(
            CollectionAddFailure.None,
            add,
            target,
            method,
            parameters[0].RefKind != RefKind.None,
            arguments);
    }

    // [WHY] A temporary that lowering put in place of the argument (a hoisted call, a lowered ?:) has the type of the
    // value, not the conversion C# applies to the argument, so the conversion is read from the element of the source
    private static IMethodSymbol? GetLostConversion(ExpressionSyntax value, SemanticModel model,
        ExpressionSyntax original, SemanticModel originalModel) =>
        originalModel.GetConversion(original) is { IsImplicit: true, IsUserDefined: true, MethodSymbol: { } method } &&
        !model.GetSafeConversion(value).IsUserDefined
            ? method
            : null;

    // <type> __gsharp_seq_<n> = <argument>; per argument, then <Add>(&<target> | <target>, [&]__gsharp_seq_<n>, ...);
    private static void Write(ExpressionSyntax element, InitializerExpressionSyntax collection, CollectionAddPlan plan,
        ref TranspileContext context)
    {
        switch (plan.Failure)
        {
            case CollectionAddFailure.CallTargetReplaced:
                if (!CallTargetEmitter.TryReplaceCall(element, plan.Add, plan.Target, ref context))
                {
                    throw new InvalidOperationException("The call target of a collection initializer Add was planned " +
                        "as replaced but was not.");
                }

                context.Emitter.Writer.Append("; ");
                return;
            case CollectionAddFailure.Unsupported:
                // At the element of the source, where the analyzer reports it
                context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, plan.Target.ReportAt ?? element,
                    UnsupportedAdd);
                context.Visitor.AppendUnsupportedPlaceholder(element);
                context.Emitter.Writer.Append("; ");
                return;
        }

        // Converted to the parameter type as C# converts it when the temporary is declared with that type
        var temps = new int[plan.Arguments.Length];
        for (var i = 0; i < temps.Length; i++)
        {
            var argument = plan.Arguments[i];
            if (argument.Conversion is not { } conversion)
            {
                temps[i] = EvaluationOrder.DeclareTemp(ref context, argument.Value, reference: false,
                    argument.ParameterType);
                continue;
            }

            // auto __gsharp_seq_<n> = <conversion>(<argument>);
            temps[i] = EvaluationOrder.BeginDeclaration(ref context);
            UserDefinedConversionHelper.AppendConversionCall(conversion, argument.Value, argument.ParameterType,
                ref context);
            context.Emitter.Writer.AppendLine(";");
        }

        CppNameResolver.AppendSymbolName(plan.Method, ref context);
        using (context.Emitter.Writer.Parentheses())
        {
            context.Emitter.Writer.AppendIf(plan.ReceiverByReference, "&");
            ObjectInitializerAssignments.AppendTarget(collection, ref context);
            for (var i = 0; i < temps.Length; i++)
            {
                context.Emitter.Writer.Append(',');
                context.Emitter.Writer.AppendIf(plan.Arguments[i].ByReference, "&");
                EvaluationOrder.AppendTempName(context.Emitter.Writer, temps[i]);
            }
        }

        context.Emitter.Writer.AppendLine(";");
    }

    /// <summary>
    /// Reports what a call of the indexer setter that <paramref name="assignment"/> (<c>[i] = v</c>) calls would
    /// report, and writes the placeholder; false when the setter can be called, or for an element of an array, which
    /// the caller writes as an element assignment.
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
