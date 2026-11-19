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
/// called; any other one gets the decision a written call of the same method gets (<see cref="CallTargetEmitter"/>):
/// an instance method of a struct has no device form yet (#451). An index initializer gets that decision for its
/// indexer setter; one that device code may call still has no element access form for a struct indexer (#1033), so it
/// is left to the element access path.
/// </remarks>
internal static class CollectionInitializerElements
{
    /// <summary>
    /// Writes the <c>Add</c> call of <paramref name="element"/> of <paramref name="collection"/> on the collection
    /// being built, as statements: <c>&lt;Add&gt;(&amp;&lt;target&gt;, &lt;arguments&gt;);</c>, with the arguments
    /// evaluated first, in the written order; each statement ends with a line break when <paramref name="lineBreaks"/>
    /// is set, otherwise with a space.
    /// </summary>
    public static void AppendAdd(ExpressionSyntax element, InitializerExpressionSyntax collection, bool lineBreaks,
        ref TranspileContext context)
    {
        var plan = Plan(element, context.Query.SemanticModel, context.Emitter.Dialect);
        if (plan.Failure != CollectionAddFailure.None)
        {
            AppendFailure(element, plan, lineBreaks, ref context);
            return;
        }

        var temps = AppendArgumentTemps(plan, lineBreaks, ref context);
        AppendCall(collection, plan, temps, lineBreaks, ref context);
    }

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
        var target = CallTargetClassifier.Classify(add, original, originalModel, dialect) with { ReportAt = original };
        var failure = target.IsFailure || target.Kind == CallTargetKind.UnsupportedIntrinsic
            ? CollectionAddFailure.CallTargetReplaced
            : FindUnsupported(add, ElementValues(element).Count, model.Compilation, dialect);
        if (failure != CollectionAddFailure.None)
        {
            return CollectionAddPlan.Failed(failure, add, target);
        }

        var method = add.ReducedFrom ?? add;
        return CollectionAddPlan.Call(
            add,
            target,
            method.Parameters[0].RefKind != RefKind.None,
            PlanArguments(element, method, model, original, originalModel));
    }

    // Why an Add that device code may call has no call form for an element: the receiver comes first, then one value
    // per parameter
    private static CollectionAddFailure FindUnsupported(IMethodSymbol add, int valueCount, Compilation compilation,
        ITranspilerDialect dialect)
    {
        var method = add.ReducedFrom ?? add;
        if (add.IsGenericMethod || method.IsGenericMethod)
        {
            return CollectionAddFailure.Generic;
        }

        if (!CppNameResolver.IsEmittedDeviceFunction(method, dialect))
        {
            return CollectionAddFailure.NotEmitted;
        }

        if (method.Parameters.AsStackEnumerable().Any(static p => p.IsParams))
        {
            return CollectionAddFailure.Params;
        }

        if (method.Parameters.Length != valueCount + 1)
        {
            return CollectionAddFailure.OmittedArguments;
        }

        return SuspensionAnalysis.IsSuspendingFunction(method, compilation)
            ? CollectionAddFailure.Suspending
            : CollectionAddFailure.None;
    }

    // The arguments in parameter order, with the user-defined conversion that lowering took from each, if any; the
    // conversions are read from the element of the source
    private static CollectionAddArgument[] PlanArguments(ExpressionSyntax element, IMethodSymbol method,
        SemanticModel model, ExpressionSyntax original, SemanticModel originalModel)
    {
        var values = ElementValues(element);
        var originalValues = ElementValues(original);
        var arguments = new CollectionAddArgument[values.Count];
        for (var i = 0; i < arguments.Length; i++)
        {
            var parameter = method.Parameters[i + 1];
            arguments[i] = new CollectionAddArgument(values[i], parameter.Type, parameter.RefKind != RefKind.None,
                GetLostConversion(values[i], model, originalValues[i], originalModel));
        }

        return arguments;
    }

    // The values of an element: those of { a, b }, or the element itself
    private static SeparatedSyntaxList<ExpressionSyntax> ElementValues(ExpressionSyntax element) =>
        element is InitializerExpressionSyntax complex && element.IsKind(SyntaxKind.ComplexElementInitializerExpression)
            ? complex.Expressions
            : SyntaxFactory.SingletonSeparatedList(element);

    // [WHY] A temporary that lowering put in place of the argument (a hoisted call, a lowered ?:) has the type of the
    // value, not the conversion C# applies to the argument, so the conversion is read from the element of the source
    private static IMethodSymbol? GetLostConversion(ExpressionSyntax value, SemanticModel model,
        ExpressionSyntax original, SemanticModel originalModel) =>
        originalModel.GetConversion(original) is { IsImplicit: true, IsUserDefined: true, MethodSymbol: { } method } &&
        !model.GetSafeConversion(value).IsUserDefined
            ? method
            : null;

    // The diagnostic of a written call of the same Add, or GSH0003 with the reason, and the placeholder
    private static void AppendFailure(ExpressionSyntax element, CollectionAddPlan plan, bool lineBreaks,
        ref TranspileContext context)
    {
        if (plan.Failure == CollectionAddFailure.CallTargetReplaced)
        {
            if (!CallTargetEmitter.TryReplaceCall(element, plan.Add, plan.Target, ref context))
            {
                throw new InvalidOperationException(
                    "The call target of a collection initializer Add was planned as replaced but was not.");
            }
        }
        else
        {
            // At the element of the source, where the analyzer reports it
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, plan.Target.ReportAt ?? element,
                GetUnsupportedSyntax(plan.Failure));
            context.Visitor.AppendUnsupportedPlaceholder(element);
        }

        ObjectInitializerAssignments.AppendStatementEnd(lineBreaks, ref context);
    }

    private static string GetUnsupportedSyntax(CollectionAddFailure failure) => failure switch
    {
        CollectionAddFailure.Generic => "a collection initializer element whose Add is a generic method",
        CollectionAddFailure.NotEmitted =>
            "a collection initializer element whose Add is not a device function that G# emits",
        CollectionAddFailure.Params => "a collection initializer element whose Add takes params",
        CollectionAddFailure.OmittedArguments =>
            "a collection initializer element that omits optional parameters of its Add",
        CollectionAddFailure.Suspending => "a collection initializer element whose Add reaches a barrier",
        _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, "Not a failure of an unsupported Add.")
    };

    // <type> __gsharp_seq_<n> = <argument>; or auto __gsharp_seq_<n> = <conversion>(<argument>); per argument,
    // converted to the parameter type as C# converts it
    private static int[] AppendArgumentTemps(CollectionAddPlan plan, bool lineBreaks, ref TranspileContext context)
    {
        var temps = new int[plan.Arguments.Length];
        for (var i = 0; i < temps.Length; i++)
        {
            var argument = plan.Arguments[i];
            if (argument.Conversion is not { } conversion)
            {
                temps[i] = EvaluationOrder.DeclareTemp(ref context, argument.Value, reference: false,
                    argument.ParameterType, lineBreaks);
                continue;
            }

            temps[i] = EvaluationOrder.BeginDeclaration(ref context);
            UserDefinedConversionHelper.AppendConversionCall(conversion, argument.Value, argument.ParameterType,
                ref context);
            ObjectInitializerAssignments.AppendStatementEnd(lineBreaks, ref context);
        }

        return temps;
    }

    // <Add>(&<target> | <target>, [&]__gsharp_seq_<n>, ...);
    private static void AppendCall(InitializerExpressionSyntax collection, CollectionAddPlan plan, int[] temps,
        bool lineBreaks, ref TranspileContext context)
    {
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

        ObjectInitializerAssignments.AppendStatementEnd(lineBreaks, ref context);
    }

    /// <summary>
    /// Reports what a call of the indexer setter that <paramref name="assignment"/> (<c>[i] = v</c>) calls would
    /// report, and writes the placeholder, ended as <see cref="AppendAdd"/> ends a statement; false when the setter can
    /// be called, or for an element of an array, which the caller writes as an element assignment.
    /// </summary>
    public static bool TryFailIndexer(AssignmentExpressionSyntax assignment, bool lineBreaks,
        ref TranspileContext context)
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

        ObjectInitializerAssignments.AppendStatementEnd(lineBreaks, ref context);
        return true;
    }
}
