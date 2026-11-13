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
    /// The prefix keeps it apart from every user name (GSH0055 rejects <c>__</c> in them).
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
    /// A call of the constructed method and the closed copy registered for it in <see cref="GenericInstanceTable"/>
    /// both reach this path (<see cref="GlobalCppNames.GetFunctionName"/>), so the two name one function.
    /// </remarks>
    public static GlobalNamePath GetInstancePath(GenericInstance instance)
    {
        var method = instance.Method;
        var typePath = GlobalCppNames.GetTypePath(method.ContainingType);
        var segments = ImmutableArray.CreateBuilder<GlobalNameSegment>(typePath.Segments.Count + 1);
        foreach (var segment in typePath.Segments)
        {
            segments.Add(segment);
        }

        segments.Add(new GlobalNameSegment(instance.Definition.Name,
            typeArguments: GetTypeArgumentPaths(method.TypeArguments),
            overloadOrdinal: GlobalCppNames.GetOverloadOrdinal(instance.Definition)));
        return new GlobalNamePath(segments.MoveToImmutable());
    }

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
