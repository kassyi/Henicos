namespace Kassyi.GSharp.Compiler.Instantiation;

/// <summary>
/// The closed copies of generic declarations and the instance each one stands for. A copy is an ordinary method that
/// the compiler adds to a derived compilation; everything that names it (<see cref="GlobalCppNames"/>) reads its
/// instance here, so that the copy is named like a call of the constructed method.
/// </summary>
/// <remarks>
/// [WHY] Keyed by the symbol of the copy, which belongs to one derived compilation only; the table drops it together
/// with that compilation. A copy is registered as soon as it is bound, before anything asks for its name.
/// </remarks>
internal static class GenericInstanceTable
{
    private static readonly ConditionalWeakTable<IMethodSymbol, GenericInstance> s_instances = new();

    /// <summary>Records that <paramref name="copy"/> is the closed copy of <paramref name="instance"/>.</summary>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="copy"/> is already the copy of another instance.
    /// </exception>
    public static void Register(IMethodSymbol copy, GenericInstance instance)
    {
        var recorded = s_instances.GetValue(copy, _ => instance);
        if (!ReferenceEquals(recorded, instance) && !recorded.Method.IsEqualTo(instance.Method))
        {
            throw new InvalidOperationException(
                $"'{copy.Name}' is already the copy of another instance of '{recorded.Definition.Name}'.");
        }
    }

    /// <summary>The instance that <paramref name="method"/> is the closed copy of, if it is one.</summary>
    public static bool TryGet(IMethodSymbol method, [NotNullWhen(true)] out GenericInstance? instance) =>
        s_instances.TryGetValue(method, out instance);
}
