namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// Names and signatures of the host members that the generator emits for one kernel, so that overloaded kernels get
/// distinct members and the overload that C# selects is the one that runs.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><description><c>Launch{Name}</c> stays a C# overload: it takes the kernel's own parameter types.</description></item>
///   <item><description>An overloaded kernel's <c>Launch{Name}Ptr</c> takes <c>GpuBuffer&lt;T&gt;</c> for its device pointers and forwards to a private <c>IntPtr</c> implementation; a kernel that is not overloaded keeps the public <c>IntPtr</c> version.</description></item>
///   <item><description>The other members (<c>s_hfunc_*</c>, <c>*_Source</c>, <c>WarmupFunctionPointer*</c>, <c>*_Delegate</c>) are keyed by <see cref="KernelEmission.MemberKey"/>.</description></item>
/// </list>
/// Whether a kernel is overloaded depends on its siblings, which <see cref="KernelClassModel.Create"/> decides.
/// </remarks>
internal static class KernelHostNames
{
    private const string LaunchPrefix = "Launch";
    private const string PointerSuffix = "Ptr";

    /// <summary>The kernel in a run-time error of the CPU paths: <c>Type.Method</c>.</summary>
    public static string GetMessageName(IMethodSymbol kernel) => kernel.ContainingType.Name + "." + kernel.Name;

    /// <summary>The name of the managed-array launch wrapper, <c>Launch{Name}</c>.</summary>
    public static string GetLaunchName(in KernelHostModel kernel) => LaunchPrefix + kernel.Name;

    /// <summary>The name of the public <c>GpuBuffer&lt;T&gt;</c> launch wrapper of an overloaded kernel, <c>Launch{Name}Ptr</c>.</summary>
    public static string GetTypedPointerLaunchName(in KernelHostModel kernel) => GetLaunchName(kernel) + PointerSuffix;

    /// <summary>The name of the <c>IntPtr</c> launch wrapper: public for a kernel that is not overloaded, private otherwise.</summary>
    public static string GetPointerLaunchName(in KernelEmission kernel) => LaunchPrefix + kernel.MemberKey + PointerSuffix;

    /// <summary>Formats a kernel as <c>Name(type, type)</c> for diagnostics.</summary>
    public static string FormatSignature(string name, EquatableArray<KernelParameterShape> parameters)
    {
        using var writer = new SourceWriter();
        writer.Append(name).Append('(');
        for (var i = 0; i < parameters.Length; i++)
        {
            if (i > 0)
            {
                writer.Append(", ");
            }

            AppendRefKind(writer, parameters[i].RefKind);
            writer.Append(parameters[i].TypeName);
        }

        writer.Append(')');
        return writer.ToString();
    }

    /// <summary>
    /// The parameter list as C# sees it in the wrapper: <c>ref</c> / <c>out</c> are part of the signature, <c>in</c> is
    /// not. With <paramref name="typedPointers"/>, a device pointer is the <c>GpuBuffer&lt;T&gt;</c> of the typed wrapper.
    /// </summary>
    public static string GetSignatureKey(EquatableArray<KernelParameterShape> parameters, bool typedPointers)
    {
        using var writer = new SourceWriter();
        foreach (var parameter in parameters)
        {
            if (typedPointers && parameter.TryAppendTypedBuffer(writer))
            {
                writer.Append(';');
                continue;
            }

            if (!typedPointers)
            {
                AppendRefKind(writer, parameter.RefKind is RefKind.In ? RefKind.None : parameter.RefKind);
            }

            writer.Append(parameter.TypeName).Append(';');
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
