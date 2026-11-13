namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>
/// Reports a call from a kernel to a device function of this compilation that waits at a barrier and whose managed
/// state machine the kernel's context cannot hold (GSH0115), on the C++ dialect, whose managed fallback runs the kernel
/// through that context.
/// </summary>
/// <remarks>
/// [WHY] The context of the kernel holds the frame of every such call. A frame that holds a private nested type of the
/// function's class is private to that class (<see cref="ManagedStateMachineAvailability"/>), and a frame that would hold
/// a file-local type is not emitted; a kernel elsewhere cannot name either, and the generated C# would not compile. The
/// managed state machine of such a kernel throws instead, and this rule stops the build. The frames of device functions
/// are left out without a diagnostic (an exported library on CUDA keeps building); a kernel that reaches one through
/// another device function is reported at its own call, with the chain in the message.
/// </remarks>
internal sealed class SuspendingCallFrameRule : ISyntaxValidationRule<InvocationExpressionSyntax>
{
    public static readonly SuspendingCallFrameRule Instance = new();

    private SuspendingCallFrameRule()
    {
    }

    public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(GSharpDiagnostics.SuspendingCallFrameUnreachable);

    public void Analyze(InvocationExpressionSyntax node, ref ValidationContext context)
    {
        var compilation = context.SemanticModel.Compilation;
        if (context.Dialect.IsCuda ||
            !context.MethodSymbol.HasAttribute(KnownAttributes.GpuKernel) ||
            context.MethodSymbol.ContainingType is not { } within ||
            context.SemanticModel.GetSymbol(node) is not IMethodSymbol symbol)
        {
            return;
        }

        var callee = (symbol.ReducedFrom ?? symbol).OriginalDefinition;
        if (!callee.HasAttribute(KnownAttributes.GpuDeviceFunction) ||
            !SuspensionAnalysis.RequiresStateMachine(callee, compilation) ||
            ManagedStateMachineAvailability.GetCallFailure(callee, within, compilation) is not { } failure)
        {
            return;
        }

        var location = node.Expression switch
        {
            MemberAccessExpressionSyntax access => access.Name.GetLocation(),
            _ => node.Expression.GetLocation()
        };
        context.ReportDiagnostic(GSharpDiagnostics.SuspendingCallFrameUnreachable, location,
            $"{callee.ContainingType.Name}.{callee.Name}", failure);
    }
}
