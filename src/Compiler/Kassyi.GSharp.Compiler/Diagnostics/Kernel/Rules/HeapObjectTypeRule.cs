namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>
/// Reports each place where device code writes the type <c>object</c>, <c>Delegate</c> or <c>MulticastDelegate</c> in a
/// body (GSH0003): a variable, a cast, a type argument (<c>Func&lt;object, int&gt;</c>), a <c>foreach</c> variable. A
/// value of such a type is a reference to a heap object, which device code cannot hold, whatever the value is (a struct,
/// a delegate, a method group, <c>null</c>). One diagnostic is reported at the type, not another at the conversion.
/// </summary>
/// <remarks>
/// <para>
/// Left to other rules: a <c>new object()</c> or an array creation that initializes the variable (GSH0001), the type of a
/// <c>x is object</c> test (decided by the types, <c>TypeTests</c>), and the signature of a method (whose parameter
/// and return types have their own diagnostics).
/// </para>
/// </remarks>
internal sealed class HeapObjectTypeRule :
    ISyntaxValidationRule<PredefinedTypeSyntax>,
    ISyntaxValidationRule<IdentifierNameSyntax>,
    ISyntaxValidationRule<VariableDeclarationSyntax>
{
    public static readonly HeapObjectTypeRule Instance = new();

    public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(GSharpDiagnostics.UnsupportedSyntax);

    private HeapObjectTypeRule()
    {
    }

    public void Analyze(PredefinedTypeSyntax node, ref ValidationContext context)
    {
        if (node.Keyword.IsKind(SyntaxKind.ObjectKeyword) && !IsLeftToOtherRules(node, context.SemanticModel))
        {
            context.ReportDiagnostic(GSharpDiagnostics.UnsupportedSyntax, node, GpuCodeConstraints.HeapObjectType);
        }
    }

    // var o = Source() where Source returns object: no keyword names the type
    public void Analyze(VariableDeclarationSyntax node, ref ValidationContext context)
    {
        // A cast names its type (reported there), a creation is GSH0001
        if (node.Type.IsVar && GpuCodeConstraints.IsObject(context.SemanticModel.GetTypeSymbol(node.Type)) &&
            !node.Variables
                .AsStackEnumerable()
                .Any(static v => v.Initializer?.Value is CastExpressionSyntax or ObjectCreationExpressionSyntax or
                    ImplicitObjectCreationExpressionSyntax or ArrayCreationExpressionSyntax or
                    ImplicitArrayCreationExpressionSyntax))
        {
            context.ReportDiagnostic(GSharpDiagnostics.UnsupportedSyntax, node.Type, GpuCodeConstraints.HeapObjectType);
        }
    }

    // Delegate and MulticastDelegate named as a type; Delegate.Combine and the like are calls, reported with their own
    public void Analyze(IdentifierNameSyntax node, ref ValidationContext context)
    {
        if (node.Parent is MemberAccessExpressionSyntax access && access.Expression == node ||
            context.SemanticModel.GetSymbol(node) is not INamedTypeSymbol
            {
                SpecialType: SpecialType.System_Delegate or SpecialType.System_MulticastDelegate
            } ||
            IsLeftToOtherRules(node, context.SemanticModel))
        {
            return;
        }

        context.ReportDiagnostic(GSharpDiagnostics.UnsupportedSyntax, node, GpuCodeConstraints.HeapObjectType);
    }

    private static bool IsLeftToOtherRules(SyntaxNode type, SemanticModel model) =>
        UnsupportedConstructRule.IsInSignature(type) ||
        type.Parent is ObjectCreationExpressionSyntax or TypeOfExpressionSyntax or TypePatternSyntax ||
        (type.Parent is BinaryExpressionSyntax binary && binary.Right == type &&
         binary.Kind() is SyntaxKind.IsExpression or SyntaxKind.AsExpression) ||
        IsCreatedByInitializer(type, model);

    // object o = new object() and object o = new int[3]: the creation is reported (GSH0001); the creation of a struct
    // is not, and the object that it is converted to is
    private static bool IsCreatedByInitializer(SyntaxNode type, SemanticModel model) =>
        type.Parent is VariableDeclarationSyntax { Variables: [{ Initializer.Value: var value }] } declaration &&
        declaration.Type == type &&
        value switch
        {
            ArrayCreationExpressionSyntax or ImplicitArrayCreationExpressionSyntax => true,
            BaseObjectCreationExpressionSyntax creation =>
                !GpuCodeConstraints.IsSupportedObjectCreation(model.GetTypeSymbol(creation)),
            _ => false
        };
}
