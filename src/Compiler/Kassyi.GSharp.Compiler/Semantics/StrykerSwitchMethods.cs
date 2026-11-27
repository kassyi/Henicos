namespace Kassyi.GSharp.Compiler.Semantics;

/// <summary>
/// Finds and recognizes the Stryker.NET <c>MutantControl.IsActive</c> switch methods added to a compilation.
/// </summary>
internal static class StrykerSwitchMethods
{
    private const string HelperNamespacePrefix = "Stryker";
    private const string HelperClassName = "MutantControl";

    /// <summary>The method name of Stryker's switch helper.</summary>
    public const string SwitchMethodName = "IsActive";

    private static readonly ConditionalWeakTable<Compilation, IMethodSymbol[]> s_switchMethods = new();

    /// <summary>
    /// Every <c>Stryker*.MutantControl.IsActive(int)</c> method declared in <paramref name="compilation"/>, found once
    /// per compilation.
    /// </summary>
    public static IMethodSymbol[] Of(Compilation compilation) => s_switchMethods.GetValue(compilation, Find);

    // public static bool IsActive(int id) of Stryker<random>.MutantControl, declared in the compilation's own source
    private static IMethodSymbol[] Find(Compilation compilation)
    {
        var methods = ImmutableArray.CreateBuilder<IMethodSymbol>();
        // GetNamespaceMembers is a lazy sequence, which StackLinq does not enumerate
        ImmutableArray.CreateRange(compilation.Assembly.GlobalNamespace.GetNamespaceMembers())
            .AsStackEnumerable()
            .Where(static helperNamespace => helperNamespace.Name.StartsWith(HelperNamespacePrefix,
                StringComparison.Ordinal))
            .SelectMany(static helperNamespace => helperNamespace.GetTypeMembers(HelperClassName)
                .AsStackEnumerable()
            ).SelectMany(static helperClass => helperClass.GetMembers(SwitchMethodName)
                .AsStackEnumerable()
            ).OfType<IMethodSymbol>()
            .Where(static method => method is
            {
                IsStatic: true,
                ReturnType.SpecialType: SpecialType.System_Boolean,
                Parameters: [{ Type.SpecialType: SpecialType.System_Int32 }]
            })
            .AddTo(methods);
        return methods.ToArray();
    }

    /// <summary>
    /// Whether <paramref name="method"/> is a Stryker mutant switch method in <paramref name="compilation"/>.
    /// </summary>
    public static bool IsSwitchMethod(IMethodSymbol method, Compilation compilation)
    {
        if (method.Name != SwitchMethodName)
        {
            return false;
        }

        return Of(compilation)
            .AsStackEnumerable()
            .Any(method, static (switchMethod, called) => switchMethod.IsEqualTo(called));
    }
}
