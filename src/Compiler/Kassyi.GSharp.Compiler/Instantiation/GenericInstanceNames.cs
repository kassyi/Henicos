namespace Kassyi.GSharp.Compiler.Instantiation;

/// <summary>
/// The names of the instances of generic kernels and device functions: the global C++ names (one per set of type
/// arguments, like the constructed structs of <see cref="GlobalCppNames.GetTypeName"/>) and the C# name of the closed
/// copy of the declaration.
/// </summary>
internal static class GenericInstanceNames
{
    /// <summary>
    /// Returns <c>__gsharp_fn_</c> followed by the encoded <see cref="GetInstancePath">path of the instance</see>, the
    /// name of a device function instance.
    /// </summary>
    public static string GetFunctionName(GenericInstance instance) =>
        GlobalCppNames.Shorten(GSharpNameEncoding.FunctionPrefix, GSharpNameEncoding.Encode(GetInstancePath(instance)));

    /// <summary>
    /// Returns <c>__gsharp_k_</c> followed by the encoded <see cref="GetInstancePath">path of the instance</see>, the
    /// entry point of a kernel instance.
    /// </summary>
    public static string GetKernelName(GenericInstance instance) =>
        GlobalCppNames.Shorten(GSharpNameEncoding.KernelPrefix, GSharpNameEncoding.Encode(GetInstancePath(instance)));

    /// <summary>
    /// Returns <c>__gsharp_gi_</c> followed by the encoded name of the instance within its declaring type: the C#
    /// name of the closed copy of the declaration and of its host members.
    /// </summary>
    /// <remarks>
    /// The segment carries every type argument, those of the containing types first, since the copy of a member of
    /// <c>Outer&lt;T&gt;</c> is declared in <c>Outer&lt;T&gt;</c> itself, and the overload ordinal of the declaration.
    /// The prefix belongs to the reserved <c>__gsharp_</c> family; GSH0055 rejects <c>__</c> in the names declared in
    /// GPU code, so only a host member of the same type could take it.
    /// </remarks>
    public static string GetCopyName(GenericInstance instance)
    {
        var segment = new GlobalNameSegment(instance.Definition.Name,
            typeArguments: GetTypeArgumentPaths(instance.TypeArguments),
            overloadOrdinal: GlobalCppNames.GetOverloadOrdinal(instance.Definition));
        return GlobalCppNames.Shorten(GSharpNameEncoding.InstancePrefix,
            GSharpNameEncoding.Encode(new GlobalNamePath(ImmutableArray.Create(segment))));
    }

    /// <summary>
    /// The C# path of an instance: the path of its constructed containing type and the method with its type arguments,
    /// for example <c>Ns.Kernels.Scale&lt;System.Int32&gt;</c>.
    /// </summary>
    /// <remarks>
    /// The closed copy registered for the instance in <see cref="GenericInstanceTable"/> is named by this path
    /// (<see cref="GlobalCppNames.GetFunctionPath"/>). A call of the constructed method does not reach it yet: it still
    /// gets the path of the definition until the calls are switched to the copies.
    /// </remarks>
    public static GlobalNamePath GetInstancePath(GenericInstance instance) =>
        GlobalCppNames.GetTypePath(instance.Method.ContainingType).Append(
            new GlobalNameSegment(instance.Definition.Name,
                typeArguments: GetTypeArgumentPaths(instance.Method.TypeArguments),
                overloadOrdinal: GlobalCppNames.GetOverloadOrdinal(instance.Definition))
        );

    private static ImmutableArray<GlobalNamePath> GetTypeArgumentPaths(ImmutableArray<ITypeSymbol> arguments)
    {
        var paths = ImmutableArray.CreateBuilder<GlobalNamePath>(arguments.Length);
        foreach (var argument in arguments)
        {
            paths.Add(GlobalCppNames.GetTypePath(argument));
        }

        return paths.MoveToImmutable();
    }
}
