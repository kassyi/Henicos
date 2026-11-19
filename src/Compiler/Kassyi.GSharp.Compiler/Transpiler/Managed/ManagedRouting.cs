namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Decisions about routing calls and references to the managed versions of device functions
/// (<see cref="ManagedVersionAnalysis"/>).
/// </summary>
internal static class ManagedRouting
{
    /// <summary>
    /// The generated prefix of the target method when calls or references to <paramref name="method"/> are routed for
    /// <paramref name="bodyKind"/>, or <see langword="null"/> when <paramref name="method"/> stays the user's method.
    /// </summary>
    public static string? GetTargetPrefix(IMethodSymbol method, Compilation compilation, ManagedBodyKind bodyKind)
    {
        if (ManagedVersionAnalysis.HasManagedVersion(method, compilation))
        {
            return GSharpInternalNames.ManagedVersion.MethodPrefix;
        }

        if (bodyKind == ManagedBodyKind.StackBody &&
            SuspensionAnalysis.IsSuspendingFunction(method, compilation))
        {
            return ManagedStackEligibility.IsStackFunction(method, compilation)
                ? GSharpInternalNames.StackModel.MethodPrefix
                : throw new InvalidOperationException(
                    $"A stack-form body cannot call suspending device function '{method.Name}' without a stack form.");
        }

        return null;
    }

    /// <summary>
    /// The generated prefix when <paramref name="symbol"/> is an unreduced device function reference that is routed
    /// for <paramref name="bodyKind"/>; <see langword="null"/> otherwise.
    /// </summary>
    public static string? GetRoutedReferencePrefix(IMethodSymbol symbol, Compilation compilation,
        ManagedBodyKind bodyKind) =>
        symbol.ReducedFrom is null && symbol.HasAttribute(KnownAttributes.GpuDeviceFunction)
            ? GetTargetPrefix(symbol, compilation, bodyKind)
            : null;

    /// <summary>
    /// The declared extension method and target prefix when <paramref name="symbol"/> is a reduced extension method
    /// group with <c>[GpuDeviceFunction]</c> whose extension is routed for <paramref name="bodyKind"/>;
    /// <see langword="null"/> otherwise.
    /// </summary>
    public static (IMethodSymbol Extension, string Prefix)? GetBoundExtension(IMethodSymbol symbol,
        Compilation compilation, ManagedBodyKind bodyKind) =>
        symbol is { ReducedFrom: { } extension } &&
        extension.HasAttribute(KnownAttributes.GpuDeviceFunction) &&
        GetTargetPrefix(extension, compilation, bodyKind) is { } prefix
            ? (extension, prefix)
            : null;

    /// <summary>
    /// The declared extension method and target prefix when <paramref name="symbol"/> is a reduced extension method
    /// call whose declared extension method is routed for <paramref name="bodyKind"/>; <see langword="null"/>
    /// otherwise.
    /// </summary>
    public static (IMethodSymbol Extension, string Prefix)? GetRoutedExtensionCall(IMethodSymbol symbol,
        Compilation compilation, ManagedBodyKind bodyKind) =>
        symbol is { ReducedFrom: { } extension } &&
        GetTargetPrefix(extension, compilation, bodyKind) is { } prefix
            ? (extension, prefix)
            : null;

    /// <summary>
    /// Whether <paramref name="method"/> has a managed version.
    /// </summary>
    public static bool IsRouted(IMethodSymbol method, Compilation compilation) =>
        ManagedVersionAnalysis.HasManagedVersion(method, compilation);

    /// <summary>
    /// Whether <paramref name="symbol"/> is an unreduced device function reference that has a managed version.
    /// </summary>
    public static bool IsRoutedReference(IMethodSymbol symbol, Compilation compilation) =>
        symbol.ReducedFrom is null &&
        symbol.HasAttribute(KnownAttributes.GpuDeviceFunction) &&
        IsRouted(symbol, compilation);

    /// <summary>
    /// The declared extension method when <paramref name="symbol"/> is a reduced extension method group with
    /// <c>[GpuDeviceFunction]</c> whose extension has a managed version; null otherwise.
    /// </summary>
    public static IMethodSymbol? GetBoundExtension(IMethodSymbol symbol, Compilation compilation) =>
        symbol is { ReducedFrom: { } extension } &&
        extension.HasAttribute(KnownAttributes.GpuDeviceFunction) &&
        IsRouted(extension, compilation)
            ? extension
            : null;

    /// <summary>
    /// The declared extension method when <paramref name="symbol"/> is a reduced extension method call whose declared
    /// extension method has a managed version; null otherwise.
    /// </summary>
    public static IMethodSymbol? GetRoutedExtensionCall(IMethodSymbol symbol, Compilation compilation) =>
        symbol is { ReducedFrom: { } extension } &&
        IsRouted(extension, compilation)
            ? extension
            : null;

    /// <summary>
    /// The member name of <paramref name="callee"/>: the name of its managed version when routed, otherwise its
    /// name as a C# identifier.
    /// </summary>
    public static string GetMemberName(IMethodSymbol callee, bool routed) =>
        GetMemberName(callee, routed ? GSharpInternalNames.ManagedVersion.MethodPrefix : null);

    /// <summary>
    /// The member name of <paramref name="callee"/>: the derived name with <paramref name="prefix"/> when non-null,
    /// otherwise its name as a C# identifier.
    /// </summary>
    public static string GetMemberName(IMethodSymbol callee, string? prefix) =>
        prefix is not null
            ? GlobalCppNames.GetKernelDerivedName(prefix, callee)
            : callee.Name.ToCSharpIdentifier();

    /// <summary>
    /// Spells the accessor stub name: kernel-derived managed version name if routed, otherwise raw callee name.
    /// </summary>
    public static string GetAccessorName(IMethodSymbol callee, bool routed) =>
        GetAccessorName(callee, routed ? GSharpInternalNames.ManagedVersion.MethodPrefix : null);

    /// <summary>
    /// Spells the accessor stub name: kernel-derived name with <paramref name="prefix"/> when non-null, otherwise raw
    /// callee name.
    /// </summary>
    public static string GetAccessorName(IMethodSymbol callee, string? prefix) =>
        prefix is not null
            ? GlobalCppNames.GetKernelDerivedName(prefix, callee)
            : callee.Name;

    /// <summary>
    /// Computes the type argument list for a <c>__gsharp_bind</c> call: receiver type, parameter types, and return type
    /// (if non-void).
    /// </summary>
    public static ImmutableArray<ITypeSymbol> GetBindTypeArguments(IMethodSymbol reduced, IMethodSymbol extension)
    {
        var builder = ImmutableArray.CreateBuilder<ITypeSymbol>(reduced.Parameters.Length + 2);
        builder.Add(reduced.ReceiverType ?? extension.Parameters[0].Type);
        for (var i = 0; i < reduced.Parameters.Length; i++)
        {
            builder.Add(reduced.Parameters[i].Type);
        }

        if (!reduced.ReturnsVoid)
        {
            builder.Add(reduced.ReturnType);
        }

        return builder.ToImmutable();
    }
}
