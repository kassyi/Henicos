namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// The accessibility of the managed state machine of a kernel and of its context class (#1284). Only the launchers of
/// the kernel's class use them, so each is the widest that compiles: the context the one its field types allow, the
/// state machine the narrower of that and the one the kernel's signature allows. Public types keep them public.
/// </summary>
/// <param name="Context">The context class.</param>
/// <param name="StateMachine">The state machine method.</param>
internal readonly record struct KernelFrameAccessibility(Accessibility Context, Accessibility StateMachine)
{
    /// <summary>
    /// Decides both for <paramref name="kernel"/>, whose context holds fields of <paramref name="fieldTypes"/>.
    /// </summary>
    public static KernelFrameAccessibility Create(IMethodSymbol kernel, ImmutableArray<ITypeSymbol> fieldTypes)
    {
        var fields = default(GeneratedMemberAccessibility.NamedTypes);
        foreach (var type in fieldTypes)
        {
            fields = fields.With(type);
        }

        var context = fields.ChooseIn(kernel.ContainingType);
        var signature = GeneratedMemberAccessibility.OfSignature(kernel).ChooseIn(kernel.ContainingType);
        return new KernelFrameAccessibility(context, GeneratedMemberAccessibility.Narrowest(context, signature));
    }
}
