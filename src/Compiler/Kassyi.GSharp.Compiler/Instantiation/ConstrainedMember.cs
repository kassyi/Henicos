namespace Kassyi.GSharp.Compiler.Instantiation;

/// <summary>What <see cref="InstanceSymbolMap.ResolveConstrained"/> found for one member.</summary>
/// <param name="Kind">How the copy binds it.</param>
/// <param name="ClosedType">The type argument the member is reached through.</param>
/// <param name="Implementation">The implementing member (<see cref="ConstrainedMemberKind.Member"/>).</param>
/// <param name="OperatorName">The metadata name of the operator
/// (<see cref="ConstrainedMemberKind.BuiltinOperator"/>).</param>
/// <param name="Constant">The value (<see cref="ConstrainedMemberKind.IdentityConstant"/>), 0 or 1.</param>
/// <param name="Reason">Why it is unsupported.</param>
internal sealed record ConstrainedMember(
    ConstrainedMemberKind Kind,
    ITypeSymbol ClosedType,
    ISymbol? Implementation,
    string? OperatorName,
    int Constant,
    string? Reason)
{
    public static ConstrainedMember Member(ITypeSymbol closed, ISymbol implementation) =>
        new(ConstrainedMemberKind.Member, closed, implementation, null, 0, null);

    public static ConstrainedMember BuiltinOperator(ITypeSymbol closed, string name) =>
        new(ConstrainedMemberKind.BuiltinOperator, closed, null, name, 0, null);

    public static ConstrainedMember IdentityConstant(ITypeSymbol closed, int value) =>
        new(ConstrainedMemberKind.IdentityConstant, closed, null, null, value, null);

    public static ConstrainedMember Unsupported(ITypeSymbol closed, string reason) =>
        new(ConstrainedMemberKind.Unsupported, closed, null, null, 0, reason);
}
