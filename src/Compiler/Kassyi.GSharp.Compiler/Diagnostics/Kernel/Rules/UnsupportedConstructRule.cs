using Kassyi.GSharp.Compiler.Transpiler.Rules.MacroRules;

namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>
/// Reports the constructs that the transpiler cannot emit: <c>new</c> of a class (GSH0001), <see cref="string"/>
/// (GSH0002), and function pointer variables, stack arrays of a dynamic size, unsupported switch expression arms and
/// operator macros without a target (GSH0003).
/// </summary>
/// <remarks>The transpiler decides with the same <see cref="GpuCodeConstraints"/> members and emits nothing for these.</remarks>
internal sealed class UnsupportedConstructRule :
    ISyntaxValidationRule<VariableDeclarationSyntax>,
    ISyntaxValidationRule<PredefinedTypeSyntax>,
    ISyntaxValidationRule<BaseObjectCreationExpressionSyntax>,
    ISyntaxValidationRule<SwitchExpressionArmSyntax>,
    ISyntaxValidationRule<AssignmentExpressionSyntax>
{
    public static readonly UnsupportedConstructRule Instance = new();

    public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
        GSharpDiagnostics.ObjectCreationNotSupported,
        GSharpDiagnostics.StringNotSupported,
        GSharpDiagnostics.UnsupportedSyntax);

    private UnsupportedConstructRule()
    {
    }

    public void Analyze(VariableDeclarationSyntax node, ref ValidationContext context)
    {
        var type = context.SemanticModel.GetTypeSymbol(node.Type);
        if (GpuCodeConstraints.IsString(type))
        {
            context.ReportDiagnostic(GSharpDiagnostics.StringNotSupported, node.Type);
        }

        if (GpuCodeConstraints.IsFunctionPointer(type))
        {
            context.ReportDiagnostic(GSharpDiagnostics.UnsupportedSyntax, node.Type, node.Type.Kind().ToString());
        }

        // The transpiler declares a stack array only for a local declaration or a fixed statement
        if (node.Parent is not (LocalDeclarationStatementSyntax or FixedStatementSyntax))
        {
            return;
        }

        foreach (var variable in node.Variables)
        {
            if (GpuCodeConstraints.FindDynamicArraySize(variable, type, context.SemanticModel) is { } size)
            {
                context.ReportDiagnostic(GSharpDiagnostics.UnsupportedSyntax, size, GpuCodeConstraints.DynamicArraySize);
            }
        }
    }

    // A declaration's type is checked with the declaration, which also catches 'var'; the signature is not device code
    public void Analyze(PredefinedTypeSyntax node, ref ValidationContext context)
    {
        if (!node.Keyword.IsKind(SyntaxKind.StringKeyword) ||
            node.Parent is VariableDeclarationSyntax declaration && declaration.Type == node ||
            IsInSignature(node))
        {
            return;
        }

        context.ReportDiagnostic(GSharpDiagnostics.StringNotSupported, node);
    }

    public void Analyze(BaseObjectCreationExpressionSyntax node, ref ValidationContext context)
    {
        if (!GpuCodeConstraints.IsSupportedObjectCreation(context.SemanticModel.GetTypeSymbol(node)))
        {
            context.ReportDiagnostic(GSharpDiagnostics.ObjectCreationNotSupported, node);
        }
    }

    public void Analyze(SwitchExpressionArmSyntax node, ref ValidationContext context)
    {
        if (node.WhenClause != null)
        {
            context.ReportDiagnostic(
                GSharpDiagnostics.UnsupportedSyntax,
                node.WhenClause,
                GpuCodeConstraints.SwitchArmWhenClauseNotSupported);
        }
        else if (!GpuCodeConstraints.IsSupportedSwitchArmPattern(node.Pattern))
        {
            context.ReportDiagnostic(
                GSharpDiagnostics.UnsupportedSyntax,
                node.Pattern,
                GpuCodeConstraints.SwitchArmPatternNotSupported);
        }
    }

    // An operator macro is expanded only as an assignment statement (MacroTranspiler)
    public void Analyze(AssignmentExpressionSyntax node, ref ValidationContext context)
    {
        if (node.Parent is not ExpressionStatementSyntax ||
            !GpuOperatorMacroResolver.TryResolve(node, context.SemanticModel, out var macro))
        {
            return;
        }

        var failure = macro.Failure ??
                      GpuOperatorMacroResolver.GetUnassignedTargetFailure(node, macro, context.SemanticModel) ??
                      (context.NeedsManagedStateMachines && context.MethodSymbol.ContainingType is { } within
                          ? GetUnreachableTargetFailure(macro, within, context.SemanticModel.Compilation)
                          : null);
        if (failure is not null)
        {
            context.ReportDiagnostic(GSharpDiagnostics.UnsupportedSyntax, node, failure);
        }
    }

    // The first target of <macro> or of an operator macro nested in it that the managed expansion cannot call
    // (ManagedAccessorSet.GetUnreachableTargetFailure)
    private static string? GetUnreachableTargetFailure(GpuOperatorMacroResolver.OperatorMacro macro,
        INamedTypeSymbol within, Compilation compilation)
    {
        if (macro.TargetMethod is { } target &&
            ManagedAccessorSet.GetUnreachableTargetFailure(target, within, compilation) is { } failure)
        {
            return failure;
        }

        if (macro.Nested.IsDefaultOrEmpty)
        {
            return null;
        }

        foreach (var nested in macro.Nested)
        {
            if (GetUnreachableTargetFailure(nested.Inner, within, compilation) is { } innerFailure)
            {
                return innerFailure;
            }
        }

        return null;
    }

    private static bool IsInSignature(SyntaxNode node)
    {
        for (var current = node.Parent; current != null; current = current.Parent)
        {
            switch (current)
            {
                case ParameterSyntax or AttributeListSyntax or TypeParameterConstraintClauseSyntax:
                    return true;
                case LocalFunctionStatementSyntax localFunction when localFunction.ReturnType.FullSpan.Contains(node.Span):
                    return true;
                case MethodDeclarationSyntax method:
                    return method.ReturnType.FullSpan.Contains(node.Span);
            }
        }

        return false;
    }
}
