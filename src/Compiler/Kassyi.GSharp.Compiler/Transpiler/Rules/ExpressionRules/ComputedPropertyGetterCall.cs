namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// Turns a read or write of a computed property of a struct that G# emits into a call of its getter or setter:
/// <c>s.Sum</c> becomes <c>s.__gsharp_mb_Sum()</c>, <c>Sum</c> inside another accessor of the struct becomes
/// <c>__gsharp_mb_Sum()</c>, and <c>s.Value = x</c> (including in an object or <c>with</c> initializer) becomes
/// <c>s.__gsharp_mb_Value(x)</c>.
/// </summary>
/// <remarks>
/// <c>StructPropertyEmitter</c> emits the getter and setter as member functions under the C++ name of the property
/// (<see cref="CppNameResolver"/>), so the caller writes the receiver and the name as for any member, a <c>ref</c>
/// receiver included. A computed property is defined by <see cref="StructAbiMembers.IsComputedProperty"/>.
/// </remarks>
internal static class ComputedPropertyGetterCall
{
    /// <summary>Whether <paramref name="symbol"/> is a computed property that is read through its getter.</summary>
    public static bool IsGetter(ISymbol? symbol, ITranspilerDialect dialect) =>
        symbol is IPropertySymbol { GetMethod: not null, ContainingType: { } type } property &&
        GlobalCppNames.IsEncodedUserType(type, dialect) &&
        StructAbiMembers.IsComputedProperty(property);

    /// <summary>Whether <paramref name="symbol"/> is a computed property that is assigned through its setter.</summary>
    public static bool IsSetter(ISymbol? symbol, ITranspilerDialect dialect) =>
        symbol is IPropertySymbol { SetMethod: not null, ContainingType: { } type } property &&
        GlobalCppNames.IsEncodedUserType(type, dialect) &&
        StructAbiMembers.IsComputedProperty(property);

    /// <summary>Whether <paramref name="expression"/> resolves to a computed property on a user struct.</summary>
    public static bool IsComputedPropertyTarget(ExpressionSyntax expression, ref TranspileContext context) =>
        GetTargetSymbol(expression, context.Query.SemanticModel) is
            IPropertySymbol { ContainingType: { } type } property &&
        GlobalCppNames.IsEncodedUserType(type, context.Emitter.Dialect) &&
        StructAbiMembers.IsComputedProperty(property);

    /// <summary>
    /// Appends <c>()</c> after the name of <paramref name="symbol"/> when <paramref name="node"/> reads it through its
    /// getter: not the target of an assignment or a step, whose getter and setter calls the assignment writes
    /// (<see cref="ComputedPropertyAssignment"/>).
    /// </summary>
    public static void AppendCallIfGetter(SyntaxNode node, ISymbol? symbol, ref TranspileContext context) =>
        context.Emitter.Writer.AppendIf(
            !IsAssignmentTarget(node) && IsGetter(symbol, context.Emitter.Dialect),
            "()");

    /// <summary>
    /// Emits <paramref name="assignment"/> as a setter call (<c>&lt;left&gt;(&lt;right&gt;)</c>) when its target is a
    /// computed property (through <see cref="ComputedPropertyAssignment"/> when its value is used), or as an ordinary
    /// assignment (<c>&lt;left&gt; = &lt;right&gt;</c>) otherwise.
    /// </summary>
    public static void TranspileAssignment(AssignmentExpressionSyntax assignment, string equalsOperator,
        ref TranspileContext context)
    {
        if (IsSetter(GetTargetSymbol(assignment.Left, context.Query.SemanticModel), context.Emitter.Dialect))
        {
            if (!IsStatementOrInitializerAssignment(assignment))
            {
                ComputedPropertyAssignment.TranspileAssignment(assignment, ref context);
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

    private static ISymbol? GetTargetSymbol(ExpressionSyntax left, SemanticModel model) =>
        left is MemberAccessExpressionSyntax memberAccess
            ? model.GetSymbol(memberAccess.Name) ?? model.GetSymbol(memberAccess)
            : model.GetSymbol(left);

    // The target of an assignment (compound included) or of an increment or decrement, inside parentheses or not
    // (an element of the left side of a deconstruction, nested or not, as well)
    private static bool IsAssignmentTarget(SyntaxNode node)
    {
        while (node.Parent is ParenthesizedExpressionSyntax or ArgumentSyntax { Parent: TupleExpressionSyntax })
        {
            node = node.Parent is ArgumentSyntax { Parent: { } tuple } ? tuple : node.Parent;
        }

        return node.Parent switch
        {
            AssignmentExpressionSyntax assignment => assignment.Left == node,
            PrefixUnaryExpressionSyntax prefix => prefix.IsKind(
                SyntaxKind.PreIncrementExpression, SyntaxKind.PreDecrementExpression),
            PostfixUnaryExpressionSyntax postfix => postfix.IsKind(
                SyntaxKind.PostIncrementExpression, SyntaxKind.PostDecrementExpression),
            _ => false
        };
    }
}
