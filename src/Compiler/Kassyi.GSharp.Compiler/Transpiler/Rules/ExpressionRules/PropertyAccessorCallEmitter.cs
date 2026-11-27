namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// Writes C++ getter and setter calls for properties with accessor functions decided by
/// <see cref="PropertyAccessorCall"/>.
/// </summary>
internal static class PropertyAccessorCallEmitter
{
    /// <summary><c>WritesBackingField</c> of the target <paramref name="expression"/> of an assignment or a
    /// step.</summary>
    public static bool WritesBackingField(ExpressionSyntax expression, ref TranspileContext context) =>
        PropertyAccessorCall.WritesBackingField(
            expression,
            PropertyAccessorCall.GetTargetSymbol(expression.UnwrapParentheses(), context.Query.SemanticModel),
            context.Emitter.Dialect
        );

    /// <summary>
    /// Whether a store into <paramref name="target"/> calls the setter of a property with accessor functions: not a
    /// property without a setter whose backing field the constructor writes
    /// (<see cref="WritesBackingField(ExpressionSyntax, ref TranspileContext)"/>).
    /// </summary>
    public static bool CallsSetter(ExpressionSyntax target, ref TranspileContext context) =>
        IsAccessorPropertyTarget(target, ref context) && !WritesBackingField(target, ref context);

    /// <summary>
    /// Whether <paramref name="expression"/> resolves to a property with accessor functions on a user struct.
    /// </summary>
    public static bool IsAccessorPropertyTarget(ExpressionSyntax expression, ref TranspileContext context) =>
        PropertyAccessorCall.GetTargetSymbol(expression, context.Query.SemanticModel) is
            IPropertySymbol { ContainingType: { } type } property &&
        EmittedUserTypes.IsEncodedUserType(type, context.Emitter.Dialect) &&
        StructAbiMembers.HasAccessorFunctions(property);

    /// <summary>
    /// <c>PlanCopyType</c> with the model, dialect and current struct of <paramref name="context"/>.
    /// </summary>
    public static INamedTypeSymbol? PlanCopyType(SyntaxNode node, ISymbol? symbol, ref TranspileContext context) =>
        PropertyAccessorCall.PlanCopyType(node, symbol, context.Query.SemanticModel, context.Emitter.Dialect,
            context.Emitter.CurrentStructType);

    /// <summary>
    /// Appends <c>()</c> after the name of <paramref name="symbol"/> when <paramref name="node"/> reads it through its
    /// getter: not the target of an assignment or a step, whose getter and setter calls the assignment writes
    /// (<see cref="PropertyAccessorAssignment"/>).
    /// </summary>
    public static void AppendCallIfGetter(SyntaxNode node, ISymbol? symbol, ref TranspileContext context) =>
        context.Emitter.Writer.AppendIf(
            !PropertyAccessorCall.IsAssignmentTarget(node) && PropertyAccessorCall.IsGetter(symbol, context.Emitter.Dialect),
            "()");

    /// <summary>
    /// Emits <paramref name="assignment"/> as a setter call (<c>&lt;left&gt;(&lt;right&gt;)</c>) when its target is a
    /// property with accessor functions (through <see cref="PropertyAccessorAssignment"/> when its value is used), or
    /// as an ordinary
    /// assignment (<c>&lt;left&gt; = &lt;right&gt;</c>) otherwise.
    /// </summary>
    public static void TranspileAssignment(AssignmentExpressionSyntax assignment, string equalsOperator,
        ref TranspileContext context)
    {
        if (PropertyAccessorCall.IsSetter(
                PropertyAccessorCall.GetTargetSymbol(assignment.Left, context.Query.SemanticModel),
                context.Emitter.Dialect))
        {
            if (!IsStatementOrInitializerAssignment(assignment))
            {
                PropertyAccessorAssignment.TranspileAssignment(assignment, ref context);
                return;
            }

            context.Visitor.Visit(assignment.Left);
            using (context.Emitter.Writer.Parentheses())
            {
                context.Visitor.Visit(assignment.Right);
            }

            return;
        }

        context.Visitor.Visit(assignment.Left);
        context.Emitter.Writer.Append(equalsOperator);
        context.Visitor.Visit(assignment.Right);
    }

    private static bool IsStatementOrInitializerAssignment(AssignmentExpressionSyntax assignment) =>
        assignment.Parent switch
        {
            ExpressionStatementSyntax => true,
            InitializerExpressionSyntax => true,
            ForStatementSyntax forStatement => forStatement.Incrementors.Contains(assignment),
            _ => false
        };
}
