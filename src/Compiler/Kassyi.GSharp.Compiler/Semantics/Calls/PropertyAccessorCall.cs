using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Semantics;

/// <summary>
/// Turns a read or write of a property with accessor functions of a struct that G# emits into a call of its getter or
/// setter:
/// <c>s.Sum</c> becomes <c>s.__gsharp_mb_Sum()</c>, <c>Sum</c> inside another accessor of the struct becomes
/// <c>__gsharp_mb_Sum()</c>, and <c>s.Value = x</c> (including in an object or <c>with</c> initializer) becomes
/// <c>s.__gsharp_mb_Value(x)</c>.
/// </summary>
/// <remarks>
/// <c>StructPropertyEmitter</c> emits the getter and setter as member functions under the C++ name of the property
/// (<see cref="CppNameResolver"/>), so the caller writes the receiver and the name as for any member, a <c>ref</c>
/// receiver included. A property with accessor member functions is defined by <see
/// cref="StructAbiMembers.HasAccessorFunctions"/>.
/// </remarks>
internal static class PropertyAccessorCall
{
    /// <summary>Whether <paramref name="symbol"/> is a property with accessor functions that is read through its
    /// getter.</summary>
    public static bool IsGetter(ISymbol? symbol, ITranspilerDialect dialect) =>
        symbol is IPropertySymbol { GetMethod: not null, ContainingType: { } type } property &&
        GlobalCppNames.IsEncodedUserType(type, dialect) &&
        StructAbiMembers.HasAccessorFunctions(property);

    /// <summary>Whether <paramref name="symbol"/> is a property with accessor functions that is assigned through its
    /// setter.</summary>
    public static bool IsSetter(ISymbol? symbol, ITranspilerDialect dialect) =>
        symbol is IPropertySymbol { SetMethod: not null, ContainingType: { } type } property &&
        GlobalCppNames.IsEncodedUserType(type, dialect) &&
        StructAbiMembers.HasAccessorFunctions(property);

    /// <summary>
    /// Whether <paramref name="node"/>, the target of an assignment, names a property without a setter whose accessors
    /// use the <c>field</c> keyword: C# assigns its backing field (in a constructor of the struct or an initializer of
    /// the
    /// property, CS0200 elsewhere), so the name is the backing field rather than the getter.
    /// </summary>
    public static bool WritesBackingField(SyntaxNode node, ISymbol? symbol, ITranspilerDialect dialect) =>
        symbol is IPropertySymbol property && WritesBackingField(property, dialect) && IsAssignmentTarget(node);

    /// <summary>
    /// Whether an assignment to <paramref name="property"/> writes its backing field: a property without a setter
    /// whose accessors use the <c>field</c> keyword, of a struct that <paramref name="dialect"/> writes under its
    /// encoded name.
    /// </summary>
    public static bool WritesBackingField(IPropertySymbol property, ITranspilerDialect dialect) =>
        property is { SetMethod: null, ContainingType: { } type } &&
        GlobalCppNames.IsEncodedUserType(type, dialect) &&
        StructAbiMembers.IsFieldBackedProperty(property);

    /// <summary><c>WritesBackingField</c> of the target <paramref name="expression"/> of an assignment or a
    /// step.</summary>
    public static bool WritesBackingField(ExpressionSyntax expression, ref TranspileContext context) =>
        WritesBackingField(
            expression,
            GetTargetSymbol(expression.UnwrapParentheses(), context.Query.SemanticModel),
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
        GetTargetSymbol(expression, context.Query.SemanticModel) is
            IPropertySymbol { ContainingType: { } type } property &&
        GlobalCppNames.IsEncodedUserType(type, context.Emitter.Dialect) &&
        StructAbiMembers.HasAccessorFunctions(property);

    /// <summary>
    /// Whether the getter of <paramref name="property"/> is a <c>const</c> member function: a <c>readonly</c> getter
    /// (of a <c>readonly</c> struct, or marked <c>readonly</c>), or one that writes nothing the caller can see
    /// (<see cref="DeviceFunctionEffects"/>). Any other getter may change its receiver (#610).
    /// </summary>
    public static bool IsConstGetter(IPropertySymbol property, Compilation compilation) =>
        property.GetMethod is not { } getter ||
        getter.IsReadOnly ||
        DeviceFunctionEffects.Classify(getter, compilation) == DeviceFunctionEffect.None;

    /// <summary>
    /// The struct type of the copy that <paramref name="node"/> calls the getter of <paramref name="symbol"/> on, or
    /// <see langword="null"/> when it calls on the receiver itself. A copy is made when the getter may change its
    /// receiver (not <see cref="IsConstGetter"/>) and C# reads the receiver as a readonly variable: an <c>in</c> or
    /// <c>ref readonly</c> parameter, a <c>readonly</c> field outside a constructor of its type, a <c>ref readonly</c>
    /// local or result, <c>this</c> in a <c>readonly</c> member, or a field of one of those. C# then calls the getter
    /// on a copy, which the caller writes as <c>&lt;type&gt;(&lt;receiver&gt;)</c>.
    /// </summary>
    /// <param name="node">The member access or the name (implicit <c>this</c>) that reads the property.</param>
    /// <param name="symbol">The member that <paramref name="node"/> names.</param>
    /// <param name="model">The model of the transpilation.</param>
    /// <param name="dialect">The dialect.</param>
    /// <param name="currentStruct">
    /// The struct whose definition is being written: its type arguments replace the type parameters of the type, which
    /// a member of a generic struct names open (<c>Box&lt;T&gt;</c>, #610).
    /// </param>
    /// <remarks>
    /// A <c>using</c> or <c>foreach</c> variable is not copied: C# calls on the variable itself, as a struct enumerator
    /// in a <c>using</c> relies on.
    /// </remarks>
    public static INamedTypeSymbol? PlanCopyType(SyntaxNode node, ISymbol? symbol, SemanticModel model,
        ITranspilerDialect dialect, INamedTypeSymbol? currentStruct)
    {
        if (IsAssignmentTarget(node) ||
            symbol is not IPropertySymbol property ||
            !NeedsCopy(property, dialect, model.Compilation) ||
            model.GetSafeOperation(node) is not IPropertyReferenceOperation { Instance: { } receiver } ||
            !ReadOnlyReceivers.IsReadOnlyVariable(receiver))
        {
            return null;
        }

        return CopyTypeOf(property, currentStruct, model.Compilation);
    }

    /// <summary>
    /// Whether a read of <paramref name="property"/> on a readonly variable calls its getter on a copy: a property
    /// with accessor functions (<see cref="IsGetter"/>) whose getter may change its receiver (not
    /// <see cref="IsConstGetter"/>).
    /// </summary>
    public static bool NeedsCopy(IPropertySymbol property, ITranspilerDialect dialect, Compilation compilation) =>
        IsGetter(property, dialect) && !IsConstGetter(property, compilation);

    /// <summary>
    /// The struct type of the copy that the getter of <paramref name="property"/> is called on: the containing type,
    /// with the type arguments of <paramref name="currentStruct"/> in place of its type parameters.
    /// </summary>
    public static INamedTypeSymbol? CopyTypeOf(IPropertySymbol property, INamedTypeSymbol? currentStruct,
        Compilation compilation) =>
        CopyTypeOf(property.ContainingType, currentStruct, compilation);

    /// <summary>
    /// <paramref name="containingType"/> with the type arguments of <paramref name="currentStruct"/> in place of its
    /// type parameters, for a defensive copy of a struct receiver.
    /// </summary>
    public static INamedTypeSymbol? CopyTypeOf(INamedTypeSymbol containingType, INamedTypeSymbol? currentStruct,
        Compilation compilation) =>
        StructTypeSubstitution.SubstituteTypeParameters(containingType, currentStruct, compilation) as
            INamedTypeSymbol;

    /// <summary>
    /// <c>PlanCopyType</c> with the model, dialect and current struct of <paramref name="context"/>.
    /// </summary>
    public static INamedTypeSymbol? PlanCopyType(SyntaxNode node, ISymbol? symbol, ref TranspileContext context) =>
        PlanCopyType(node, symbol, context.Query.SemanticModel, context.Emitter.Dialect,
            context.Emitter.CurrentStructType);

    /// <summary>
    /// Appends <c>()</c> after the name of <paramref name="symbol"/> when <paramref name="node"/> reads it through its
    /// getter: not the target of an assignment or a step, whose getter and setter calls the assignment writes
    /// (<see cref="PropertyAccessorAssignment"/>).
    /// </summary>
    public static void AppendCallIfGetter(SyntaxNode node, ISymbol? symbol, ref TranspileContext context) =>
        context.Emitter.Writer.AppendIf(
            !IsAssignmentTarget(node) && IsGetter(symbol, context.Emitter.Dialect),
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
        if (IsSetter(GetTargetSymbol(assignment.Left, context.Query.SemanticModel), context.Emitter.Dialect))
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

    /// <summary>
    /// The property that the target <paramref name="target"/> of an assignment or a step names, if any.
    /// </summary>
    public static IPropertySymbol? GetTargetProperty(ExpressionSyntax target, SemanticModel model) =>
        GetTargetSymbol(target.UnwrapParentheses(), model) as IPropertySymbol;

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
