namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>
/// Reports a call in GPU code, or a method group it names, whose target is a method of another assembly that the
/// project's dialect does not implement (GSH0109), and a call of an intrinsic that the project's dialect declares it
/// does not implement (GSH0054, a warning: the transpiler writes a runtime failure), from the decision the transpiler
/// writes the call by (<see cref="CallTargetClassifier"/>). The calls that C# makes without a call in the source are
/// decided the same way: the builder of a collection expression (<see cref="CollectionBuilderMethod"/>) and the
/// <c>GetPinnableReference</c> of a <c>fixed</c> statement (<see cref="PinnableReferenceMethod"/>).
/// </summary>
/// <remarks>
/// [WHY] The analyzer runs the transpiler for the CUDA dialect only (<see cref="TranspilerFailureCheck"/>), whose
/// mapping is not the one of a project that targets another dialect; this rule asks the classifier with the dialect of
/// the project (<see cref="ValidationContext.Dialect"/>), and <see cref="TranspilerFailureCheck"/> drops the GSH0109 of
/// its CUDA run. A method of the project without a device definition (GSH0107) does not depend on the dialect and is
/// reported through <see cref="TranspilerFailureCheck"/>.
/// </remarks>
internal sealed class CallTargetRule :
    ISyntaxValidationRule<InvocationExpressionSyntax>,
    ISyntaxValidationRule<MemberAccessExpressionSyntax>,
    ISyntaxValidationRule<IdentifierNameSyntax>,
    ISyntaxValidationRule<CollectionExpressionSyntax>,
    ISyntaxValidationRule<VariableDeclaratorSyntax>
{
    public static readonly CallTargetRule Instance = new();

    public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            GSharpDiagnostics.UnmappedMethodCalledFromGpuCode,
            GSharpDiagnostics.IntrinsicNotImplementedByDialect
        );

    private CallTargetRule()
    {
    }

    public void Analyze(InvocationExpressionSyntax node, ref ValidationContext context)
    {
        if (context.SemanticModel.GetSymbol(node) is IMethodSymbol called)
        {
            Report(node, called, ref context, reportsUnsupportedIntrinsic: true);
        }
    }

    // A method group named with its type or receiver (U.F)
    public void Analyze(MemberAccessExpressionSyntax node, ref ValidationContext context)
    {
        if (context.SemanticModel.GetSymbol(node) is IMethodSymbol called && CallTargetClassifier.IsMethodGroup(node))
        {
            Report(node, called, ref context, reportsUnsupportedIntrinsic: false);
        }
    }

    // A method group named alone (F, also through using static); the name of a member access is analyzed there
    public void Analyze(IdentifierNameSyntax node, ref ValidationContext context)
    {
        if (node.Parent is MemberAccessExpressionSyntax access && access.Name == node)
        {
            return;
        }

        if (context.SemanticModel.GetSymbol(node) is IMethodSymbol called && CallTargetClassifier.IsMethodGroup(node))
        {
            Report(node, called, ref context, reportsUnsupportedIntrinsic: false);
        }
    }

    // Coll c = [a, b] calls the [CollectionBuilder] method of Coll
    public void Analyze(CollectionExpressionSyntax node, ref ValidationContext context)
    {
        if (CollectionBuilderMethod.Find(context.SemanticModel.GetConvertedTypeSymbol(node)) is { } builder)
        {
            Report(node, builder, ref context, reportsUnsupportedIntrinsic: true);
        }
    }

    // fixed (int* p = s) calls s.GetPinnableReference()
    public void Analyze(VariableDeclaratorSyntax node, ref ValidationContext context)
    {
        if (node.Parent?.Parent is FixedStatementSyntax &&
            node.Initializer?.Value is { } value &&
            PinnableReferenceMethod.Find(value, context.SemanticModel) is { } method)
        {
            Report(value, method, ref context, reportsUnsupportedIntrinsic: true);
        }
    }

    // [WHY] Only the plans that depend on the dialect; a call in the arguments of a call that C# removes is never
    // emitted. Only a call of an intrinsic the dialect does not implement becomes a runtime failure
    private static void Report(SyntaxNode node, IMethodSymbol called, ref ValidationContext context,
        bool reportsUnsupportedIntrinsic)
    {
        var plan = CallTargetClassifier.Classify(called, node, context.SemanticModel, context.Dialect);
        var reported = plan.Kind == CallTargetKind.Unmapped ||
                       (reportsUnsupportedIntrinsic && plan.Kind == CallTargetKind.UnsupportedIntrinsic);
        if (!reported || RemovedCalls.IsInRemovedCall(node, context.SemanticModel))
        {
            return;
        }

        context.ReportDiagnostic(plan.Descriptor, node, plan.MessageArgs);
    }
}
