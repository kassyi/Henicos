namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// Names and signatures of the host members that the generator emits for one kernel, so that overloaded kernels get
/// distinct members and the overload that C# selects is the one that runs.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><description><c>Launch{Name}</c> stays a C# overload: it takes the kernel's own parameter types.</description></item>
///   <item><description>An overloaded kernel's <c>Launch{Name}Ptr</c> takes <c>GpuBuffer&lt;T&gt;</c> for its device pointers and forwards to a private <c>IntPtr</c> implementation; a kernel that is not overloaded keeps the public <c>IntPtr</c> version.</description></item>
///   <item><description>The other members (<c>s_hfunc_*</c>, <c>*_Source</c>, <c>WarmupFunctionPointer*</c>, <c>*_Delegate</c>) are keyed by <see cref="GetMemberKey"/>.</description></item>
/// </list>
/// </remarks>
internal static class KernelHostNames
{
    private const string LaunchPrefix = "Launch";
    private const string PointerSuffix = "Ptr";
    private const string GpuBufferType = "global::Kassyi.GSharp.Runtime.GpuBuffer";

    /// <summary>Whether another <c>[GpuKernel]</c> in the containing type has the same name.</summary>
    public static bool IsOverloadedKernel(IMethodSymbol kernel)
    {
        var count = 0;
        foreach (var member in kernel.ContainingType.GetMembers(kernel.Name))
        {
            if (member is IMethodSymbol method && method.HasAttribute(KnownAttributes.GpuKernel))
            {
                count++;
            }
        }

        return count > 1;
    }

    /// <summary>
    /// The suffix of the per-kernel host members: the kernel name, or <c>{name}__o{n}</c> for an overloaded kernel.
    /// GSH0055 rejects <c>__</c> in user identifiers, so the key cannot collide with another kernel's name.
    /// </summary>
    public static string GetMemberKey(IMethodSymbol kernel) =>
        IsOverloadedKernel(kernel)
            ? $"{kernel.Name}__o{GlobalCppNames.GetOverloadOrdinal(kernel).ToString(CultureInfo.InvariantCulture)}"
            : kernel.Name;

    /// <summary>The name of the managed-array launch wrapper, <c>Launch{Name}</c>.</summary>
    public static string GetLaunchName(IMethodSymbol kernel) => LaunchPrefix + kernel.Name;

    /// <summary>The name of the public <c>GpuBuffer&lt;T&gt;</c> launch wrapper of an overloaded kernel, <c>Launch{Name}Ptr</c>.</summary>
    public static string GetTypedPointerLaunchName(IMethodSymbol kernel) => GetLaunchName(kernel) + PointerSuffix;

    /// <summary>The name of the <c>IntPtr</c> launch wrapper: public for a kernel that is not overloaded, private otherwise.</summary>
    public static string GetPointerLaunchName(IMethodSymbol kernel) => LaunchPrefix + GetMemberKey(kernel) + PointerSuffix;

    /// <summary>
    /// Gets the element type <c>T</c> of the <c>GpuBuffer&lt;T&gt;</c> that the typed wrapper takes for a parameter
    /// which the <c>IntPtr</c> wrapper passes as a device pointer (array-like, <c>ref</c> or <c>out</c>).
    /// </summary>
    /// <returns><see langword="false"/> when the parameter is not a device pointer or its element type is not unmanaged.</returns>
    public static bool TryGetBufferElementType(IParameterSymbol parameter, [NotNullWhen(true)] out ITypeSymbol? element)
    {
        var marshaled = KernelLaunchEmitter.GetMarshaledType(parameter);
        element = marshaled.TryGetArrayLikeElementType(out var arrayElement) ? arrayElement
            : parameter.RefKind is RefKind.Ref or RefKind.Out ? parameter.Type
            : null;
        return element is { IsUnmanagedType: true };
    }

    /// <summary>Appends <c>GpuBuffer&lt;T&gt;</c> when <see cref="TryGetBufferElementType"/> succeeds.</summary>
    public static bool TryAppendTypedBuffer(SourceWriter writer, IParameterSymbol parameter)
    {
        if (!TryGetBufferElementType(parameter, out var element))
        {
            return false;
        }

        // A user struct lives on the device in its native shadow layout, which the wrapper class declares as AotContexts.*.
        writer.Append(GpuBufferType).Append('<');
        if (CudaLayoutRules.IsCustomStruct(element))
        {
            writer.AppendAotStructPath(element);
        }
        else
        {
            writer.AppendFullyQualifiedCsharpName(element);
        }

        writer.Append('>');
        return true;
    }

    /// <summary>
    /// Finds an earlier overload of <paramref name="kernel"/> whose <c>Launch{Name}</c> or typed <c>Launch{Name}Ptr</c>
    /// has the same parameter list, which C# would reject as a duplicate member.
    /// </summary>
    public static bool TryFindIndistinguishableOverload(IMethodSymbol kernel, [NotNullWhen(true)] out IMethodSymbol? earlier,
        out string wrapperName)
    {
        earlier = null;
        wrapperName = string.Empty;
        if (!IsOverloadedKernel(kernel))
        {
            return false;
        }

        var ordinal = GlobalCppNames.GetOverloadOrdinal(kernel);
        var managed = GetSignatureKey(kernel, typedPointers: false);
        var typed = GetSignatureKey(kernel, typedPointers: true);
        foreach (var member in kernel.ContainingType.GetMembers(kernel.Name))
        {
            if (member is not IMethodSymbol other || !other.HasAttribute(KnownAttributes.GpuKernel) ||
                GlobalCppNames.GetOverloadOrdinal(other) >= ordinal)
            {
                continue;
            }

            if (string.Equals(GetSignatureKey(other, typedPointers: false), managed, StringComparison.Ordinal))
            {
                earlier = other;
                wrapperName = GetLaunchName(kernel);
                return true;
            }

            if (string.Equals(GetSignatureKey(other, typedPointers: true), typed, StringComparison.Ordinal))
            {
                earlier = other;
                wrapperName = GetTypedPointerLaunchName(kernel);
                return true;
            }
        }

        return false;
    }

    /// <summary>Formats a kernel as <c>Name(type, type)</c> for diagnostics.</summary>
    public static string FormatSignature(IMethodSymbol kernel)
    {
        using var writer = new SourceWriter();
        writer.Append(kernel.Name).Append('(');
        for (var i = 0; i < kernel.Parameters.Length; i++)
        {
            if (i > 0)
            {
                writer.Append(", ");
            }

            AppendRefKind(writer, kernel.Parameters[i].RefKind);
            writer.AppendFullyQualifiedCsharpName(kernel.Parameters[i].Type);
        }

        writer.Append(')');
        return writer.ToString();
    }

    // The parameter list as C# sees it in the wrapper: ref / out are part of the signature, in is not.
    private static string GetSignatureKey(IMethodSymbol kernel, bool typedPointers)
    {
        using var writer = new SourceWriter();
        foreach (var parameter in kernel.Parameters)
        {
            if (typedPointers && TryAppendTypedBuffer(writer, parameter))
            {
                writer.Append(';');
                continue;
            }

            if (!typedPointers)
            {
                AppendRefKind(writer, parameter.RefKind is RefKind.In ? RefKind.None : parameter.RefKind);
            }

            writer.AppendFullyQualifiedCsharpName(parameter.Type).Append(';');
        }

        return writer.ToString();
    }

    private static void AppendRefKind(SourceWriter writer, RefKind refKind)
    {
        switch (refKind)
        {
            case RefKind.Ref:
                writer.Append("ref ");
                break;
            case RefKind.Out:
                writer.Append("out ");
                break;
            case RefKind.In:
                writer.Append("in ");
                break;
        }
    }
}
