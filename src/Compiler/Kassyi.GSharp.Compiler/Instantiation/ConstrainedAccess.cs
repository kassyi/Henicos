using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Instantiation;

/// <summary>
/// Finds the type parameter through which a node reaches a static abstract or virtual interface member
/// (<c>T.Zero</c>, <c>a + b</c> on <c>T : INumber&lt;T&gt;</c>), from the <c>ConstrainedToType</c> of its operation.
/// </summary>
internal static class ConstrainedAccess
{
    // A name inside a call (T.Max in T.Max(a, b)) has no operation of its own; its call is at most this far up
    private const int MaxDepth = 4;

    /// <summary>
    /// Whether <paramref name="symbol"/> is a static abstract or virtual member of an interface, which a type parameter
    /// reaches by its constraint (<c>T.Zero</c>, an operator).
    /// </summary>
    public static bool IsConstrainable(ISymbol? symbol) =>
        symbol is { IsStatic: true, ContainingType.TypeKind: TypeKind.Interface } &&
        (symbol.IsAbstract || symbol.IsVirtual);

    /// <summary>
    /// Whether <paramref name="symbol"/> is an instance method, property or event of an interface, which a value of a
    /// type parameter reaches by its constraint (<c>x.CompareTo(y)</c> on <c>T : IComparable&lt;T&gt;</c>).
    /// </summary>
    public static bool IsInterfaceInstanceMember(ISymbol? symbol) =>
        symbol is IMethodSymbol or IPropertySymbol or IEventSymbol &&
        symbol is { IsStatic: false, ContainingType.TypeKind: TypeKind.Interface };

    // [WHY] Static abstract/virtual interface members record ConstrainedToType on the IOperation, while instance
    // interface members (e.g. a.CompareTo(b), x.M()) have ConstrainedToType == null and constrain through Instance.Type.
    /// <summary>
    /// The type <paramref name="node"/> reaches <paramref name="member"/> through, or <see langword="null"/> when it
    /// reaches the member some other way.
    /// </summary>
    public static ITypeSymbol? Find(SyntaxNode node, SemanticModel model, ISymbol member)
    {
        var depth = 0;
        for (SyntaxNode? current = node; current is not null && depth < MaxDepth; current = current.Parent, depth++)
        {
            var (target, constrainedTo, receiver) = GetOperationAccess(model.GetOperation(current));
            if (target is null || !target.OriginalDefinition.IsEqualTo(member.OriginalDefinition))
            {
                continue;
            }

            if (constrainedTo is not null)
            {
                return constrainedTo;
            }

            if (IsInterfaceInstanceMember(member) && receiver is not null)
            {
                return receiver;
            }
        }

        return null;
    }

    private static (ISymbol? Target, ITypeSymbol? ConstrainedTo, ITypeSymbol? Receiver) GetOperationAccess(
        IOperation? operation) =>
        operation switch
        {
            IInvocationOperation invocation =>
                (invocation.TargetMethod, invocation.ConstrainedToType, invocation.Instance?.Type),
            IMemberReferenceOperation reference =>
                (reference.Member, reference.ConstrainedToType, reference.Instance?.Type),
            IBinaryOperation binary => (binary.OperatorMethod, binary.ConstrainedToType, null),
            IUnaryOperation unary => (unary.OperatorMethod, unary.ConstrainedToType, null),
            ICompoundAssignmentOperation compound => (compound.OperatorMethod, compound.ConstrainedToType, null),
            IIncrementOrDecrementOperation increment => (increment.OperatorMethod, increment.ConstrainedToType, null),
            IConversionOperation conversion => (conversion.OperatorMethod, conversion.ConstrainedToType, null),
            _ => (null, null, null),
        };
}
