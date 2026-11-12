using Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules.Macros;

namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>Diagnostic rule that verifies required arguments to GPU macro methods are compile-time constants.</summary>
/// <remarks>
/// Each macro family has its own checks: <see cref="UnrollChecks"/>, <see cref="MacroLoopBodyChecks"/>,
/// <see cref="InlinePtxChecks"/> and <see cref="SyncGridChecks"/>.
/// </remarks>
internal sealed class GpuMacroArgumentRule : ISyntaxValidationRule<InvocationExpressionSyntax>
{
    public static readonly GpuMacroArgumentRule Instance = new();

    public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
        GSharpDiagnostics.IntrinsicNotImplementedByDialect,
        GSharpDiagnostics.InlinePtxArgumentMustBeStringLiteral,
        GSharpDiagnostics.MacroArgumentMustBeConstant,
        GSharpDiagnostics.MacroLambdaRequiresParameter,
        GSharpDiagnostics.UnrollFactorMustBeNonNegativeConstant,
        GSharpDiagnostics.UnrollHintMustPrecedeLoop,
        GSharpDiagnostics.UnrolledLoopMustBeStatic,
        GSharpDiagnostics.UnrolledLoopMayBeIgnored,
        GSharpDiagnostics.UnsupportedSyntax,
        GSharpDiagnostics.SyncGridRequiresCooperativeKernel);

    private GpuMacroArgumentRule()
    {
    }

    public void Analyze(InvocationExpressionSyntax invocation, ref ValidationContext context)
    {
        var semanticModel = context.SemanticModel;
        if (semanticModel.GetSymbol(invocation) is not IMethodSymbol invocationSymbol)
        {
            return;
        }

        var call = GpuMacroClassifier.Classify(invocationSymbol);
        switch (call.Kind)
        {
            case GpuMacroKind.HintUnroll:
                UnrollChecks.CheckUnrollHint(invocation, ref context);
                break;

            case GpuMacroKind.ForUnroll:
                UnrollChecks.CheckForUnrollBounds(invocation, call, ref context);
                MacroLoopBodyChecks.CheckLoopBody(invocation, call, TypeConstants.GpuMacros.ForUnroll, ref context);
                break;

            case GpuMacroKind.GridStrideLoop:
                MacroLoopBodyChecks.CheckLoopBody(
                    invocation,
                    call,
                    TypeConstants.GpuMacros.GridStrideLoop,
                    ref context
                );
                break;

            case GpuMacroKind.InlinePtx:
                InlinePtxChecks.CheckInlinePtx(invocation, ref context);
                break;

            case GpuMacroKind.SyncGrid:
                SyncGridChecks.CheckSyncGrid(invocation, call, ref context);
                break;
        }
    }
}
