namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Emits the <c>operator true</c> and <c>operator false</c> of a struct as value-returning device functions
/// (<c>__gsharp_fn_...__op_True</c> / <c>__op_False</c>), which a <c>&amp;&amp;</c> or <c>||</c> over an operator macro
/// of the struct calls to test its left operand (<see cref="OperatorMacroResolver.PlanConditional"/>).
/// </summary>
/// <remarks>
/// [WHY] Only the operators that such a <c>&amp;&amp;</c> or <c>||</c> can call are emitted: <c>operator false</c> when
/// the struct maps <c>&amp;</c> to an operator macro, <c>operator true</c> when it maps <c>|</c>. Nothing else calls them
/// on the device, and a struct's definition is written wherever the struct is used, so emitting every truth operator
/// would transpile bodies that no kernel runs.
/// </remarks>
internal static class StructTruthOperatorEmitter
{
    /// <summary>Whether <paramref name="method"/> is a user-defined <c>operator true</c> or <c>operator false</c>.</summary>
    public static bool IsTruthOperator(IMethodSymbol? method) =>
        method is
        {
            MethodKind: MethodKind.UserDefinedOperator,
            Name: WellKnownMemberNames.TrueOperatorName or WellKnownMemberNames.FalseOperatorName
        };

    /// <summary>
    /// Whether <paramref name="method"/> is an <c>operator true</c> or <c>operator false</c> emitted as a device
    /// function: one of a struct emitted for the device (not a <c>ref struct</c>) that maps the matching <c>|</c> or
    /// <c>&amp;</c> to an operator macro, with a body, or recorded by the assembly that declares it
    /// (<see cref="DeviceExportTable"/>).
    /// </summary>
    public static bool IsEmittedTruthOperator(IMethodSymbol? method, Compilation compilation) =>
        IsTruthOperator(method) &&
        method is { ContainingType: { IsRefLikeType: false } containingType } &&
        GlobalCppNames.IsEmittedUserType(containingType) &&
        MapsConditionalOperator(containingType, method.Name) &&
        (TryGetDeclaration(method, out _) ||
         (DeviceExportTable.IsOutside(method, compilation) && DeviceExportTable.TryFind(method, out _)));

    /// <summary>Writes the truth operators of <paramref name="structSym"/> that <see cref="IsEmittedTruthOperator"/> emits.</summary>
    public static void WriteTruthOperators(INamedTypeSymbol structSym, ref TranspileContext context)
    {
        if (structSym.IsRefLikeType)
        {
            return;
        }

        var compilation = context.Query.SemanticModel.Compilation;
        foreach (var member in structSym.GetMembers())
        {
            if (member is IMethodSymbol method &&
                IsEmittedTruthOperator(method, compilation) &&
                TryGetDeclaration(method, out var declaration) &&
                compilation.ContainsSyntaxTree(declaration.SyntaxTree))
            {
                StructConversionEmitter.WriteOperatorFunction(method, declaration, ref context);
            }
        }
    }

    // Whether <type> maps the operator that && (for operator false) or || (for operator true) calls to an operator macro
    private static bool MapsConditionalOperator(INamedTypeSymbol type, string truthOperatorName) =>
        type.GetMembers(truthOperatorName == WellKnownMemberNames.FalseOperatorName
                ? WellKnownMemberNames.BitwiseAndOperatorName
                : WellKnownMemberNames.BitwiseOrOperatorName)
            .AsStackEnumerable()
            .Any(static member => member.HasAttribute(KnownAttributes.GpuOperatorMacro));

    private static bool TryGetDeclaration(IMethodSymbol method,
        [NotNullWhen(true)] out OperatorDeclarationSyntax? declaration) =>
        method.TryGetDeclaringSyntax(out declaration) &&
        (declaration.Body != null || declaration.ExpressionBody != null);
}
