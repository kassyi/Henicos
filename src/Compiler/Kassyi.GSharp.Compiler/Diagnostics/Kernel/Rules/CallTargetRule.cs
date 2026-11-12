namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>
/// Reports a call in GPU code, or a method group it names, whose target is a method of another assembly that the
/// project's dialect does not implement (GSH0109), from the decision the transpiler fails the call by
/// (<see cref="CallTargetClassifier"/>).
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
    ISyntaxValidationRule<IdentifierNameSyntax>
{
    public static readonly CallTargetRule Instance = new();

    public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(GSharpDiagnostics.UnmappedMethodCalledFromGpuCode);

    private CallTargetRule()
    {
    }

    public void Analyze(InvocationExpressionSyntax node, ref ValidationContext context)
    {
        if (context.SemanticModel.GetSymbol(node) is IMethodSymbol called)
        {
            Report(node, called, ref context);
        }
    }

    // A method group named with its type or receiver (U.F)
    public void Analyze(MemberAccessExpressionSyntax node, ref ValidationContext context)
    {
        if (context.SemanticModel.GetSymbol(node) is IMethodSymbol called && CallTargetClassifier.IsMethodGroup(node))
        {
            Report(node, called, ref context);
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
            Report(node, called, ref context);
        }
    }

    // [WHY] Only the failures that depend on the dialect; a call in the arguments of a call that C# removes is never
    // emitted
    private static void Report(SyntaxNode node, IMethodSymbol called, ref ValidationContext context)
    {
        var plan = CallTargetClassifier.Classify(called, node, context.SemanticModel, context.Dialect);
        if (plan.Kind != CallTargetKind.Unmapped || RemovedCalls.IsInRemovedCall(node, context.SemanticModel))
        {
            return;
        }

        context.ReportDiagnostic(plan.Descriptor, node, plan.MessageArgs);
    }
}
