namespace Kassyi.GSharp.Compiler.Semantics;

/// <summary>
/// Looks up the <c>Dispose</c> method that a C# <c>using</c> calls on a resource struct.
/// </summary>
internal static class UsingDisposeLookup
{
    /// <summary>
    /// The method that a <c>using</c> of <paramref name="type"/> calls: the implementation of
    /// <see cref="IDisposable.Dispose"/>, or for a <c>ref struct</c> a public parameterless <c>void Dispose()</c>.
    /// </summary>
    public static IMethodSymbol? Find(INamedTypeSymbol type, Compilation compilation)
    {
        var interfaceMethod = compilation.GetSpecialType(SpecialType.System_IDisposable)
            .GetMembers(nameof(IDisposable.Dispose))
            .AsStackEnumerable()
            .OfType<IMethodSymbol>()
            .FirstOrDefault(static method => method.Parameters.Length == 0);
        if (interfaceMethod != null &&
            type.FindImplementationForInterfaceMember(interfaceMethod) is IMethodSymbol implementation)
        {
            return implementation;
        }

        if (!type.IsRefLikeType)
        {
            return null;
        }

        return type.GetMembers(nameof(IDisposable.Dispose))
            .AsStackEnumerable()
            .OfType<IMethodSymbol>()
            .FirstOrDefault(static method => method is
            {
                IsStatic: false,
                ReturnsVoid: true,
                Parameters.Length: 0,
                DeclaredAccessibility: Accessibility.Public
            });
    }
}
