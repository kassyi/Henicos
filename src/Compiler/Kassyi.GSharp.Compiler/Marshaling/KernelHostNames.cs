namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// Names and signatures of the host members and per-parameter locals that the generator emits for one kernel, so that
/// overloaded kernels get distinct members and user parameter names never collide with generated locals (#406).
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><description><c>Launch{Name}</c> stays a C# overload: it takes the kernel's own parameter types.</description></item>
///   <item><description>An overloaded kernel's <c>Launch{Name}Ptr</c> takes <c>GpuBuffer&lt;T&gt;</c> for its device pointers and forwards to a private <c>IntPtr</c> implementation; a kernel that is not overloaded keeps the public <c>IntPtr</c> version.</description></item>
///   <item><description>The other members (<c>__gsharp_hfunc_*</c>, <c>*_Source</c>, <c>WarmupFunctionPointer*</c>, <c>__gsharp_delegate_*</c>) are keyed by <see cref="KernelEmission.MemberKey"/>.</description></item>
///   <item><description>Per-parameter launcher locals use <c>__gsharp_{kind}_{ordinal}</c> (<see cref="KernelParameterShape.Ordinal"/>).</description></item>
/// </list>
/// Whether a kernel is overloaded depends on its siblings, which <see cref="KernelClassModel.Create"/> decides.
/// </remarks>
internal static class KernelHostNames
{
    private const string LaunchPrefix = "Launch";
    private const string PointerSuffix = "Ptr";
    private const string WarmupPrefix = "WarmupFunctionPointer";

    /// <summary>The comma-separated list of reserved launch option parameter names for the GSH0106 diagnostic message.</summary>
    public const string ReservedLaunchParameterList =
        "gridDimX, gridDimY, gridDimZ, blockDimX, blockDimY, blockDimZ, sharedMemBytes, hStream, macros";

    /// <summary>Whether <paramref name="name"/> is one of the public launch option parameters of the generated host wrapper (GSH0106).</summary>
    public static bool IsReservedLaunchParameterName(string name) => name is
        "gridDimX" or "gridDimY" or "gridDimZ" or
        "blockDimX" or "blockDimY" or "blockDimZ" or
        "sharedMemBytes" or "hStream" or "macros";

    /// <summary>The kernel in a run-time error of the CPU paths: <c>Type.Method</c>.</summary>
    public static string GetMessageName(IMethodSymbol kernel) => kernel.ContainingType.Name + "." + kernel.Name;

    /// <summary>The name of the managed-array launch wrapper, <c>Launch{Name}</c>.</summary>
    public static string GetLaunchName(in KernelHostModel kernel) => LaunchPrefix + kernel.Name;

    /// <summary>The name of the public <c>GpuBuffer&lt;T&gt;</c> launch wrapper of an overloaded kernel, <c>Launch{Name}Ptr</c>.</summary>
    public static string GetTypedPointerLaunchName(in KernelHostModel kernel) => GetLaunchName(kernel) + PointerSuffix;

    /// <summary>The name of the <c>IntPtr</c> launch wrapper: public for a kernel that is not overloaded, private otherwise.</summary>
    public static string GetPointerLaunchName(in KernelEmission kernel) => LaunchPrefix + kernel.MemberKey + PointerSuffix;

    /// <summary>The class-qualified <see cref="GetPointerLaunchName"/> call target, so a parameter of the same name cannot shadow it.</summary>
    public static string GetQualifiedPointerLaunchName(in KernelEmission kernel) =>
        $"{kernel.Host.ClassQualifiedName}.{GetPointerLaunchName(kernel)}";

    /// <summary>The static field caching the CUDA function pointer of a kernel, <c>__gsharp_hfunc_{key}</c>.</summary>
    public static string GetFunctionPointerFieldName(string memberKey) =>
        GSharpInternalNames.Launcher.FunctionPointerFieldPrefix + memberKey;

    /// <summary>The static field tracking whether a kernel's layout guards have passed, <c>__gsharp_layout_checked_{key}</c>.</summary>
    public static string GetLayoutCheckedFieldName(string memberKey) =>
        GSharpInternalNames.Launcher.LayoutCheckedFieldPrefix + memberKey;

    /// <summary>The method that preloads the CUDA module and resolves the function pointer, <c>WarmupFunctionPointer{key}</c>.</summary>
    public static string GetWarmupMethodName(string memberKey) => WarmupPrefix + memberKey;

    /// <summary>The class-qualified <see cref="GetWarmupMethodName"/> call target, so a parameter of the same name cannot shadow it.</summary>
    public static string GetQualifiedWarmupMethodName(in KernelEmission kernel) =>
        $"{kernel.Host.ClassQualifiedName}.{GetWarmupMethodName(kernel.MemberKey)}";

    /// <summary>The private CPU P/Invoke delegate type of a kernel, <c>__gsharp_delegate_{key}</c>.</summary>
    public static string GetDelegateTypeName(in KernelEmission kernel) =>
        GSharpInternalNames.Launcher.DelegateTypePrefix + kernel.MemberKey;

    /// <summary>The CUDA device pointer local of <paramref name="parameter"/>, <c>__gsharp_d_{ordinal}</c>.</summary>
    public static string GetDevicePointerLocal(in KernelParameterShape parameter) =>
        FormatOrdinal(GSharpInternalNames.Launcher.DevicePointerPrefix, parameter.Ordinal);

    /// <summary>The stackalloc'd <c>GSharpArrayProxy</c> pointer local of <paramref name="parameter"/>, <c>__gsharp_proxy_{ordinal}</c>.</summary>
    public static string GetProxyLocal(in KernelParameterShape parameter) =>
        FormatOrdinal(GSharpInternalNames.Launcher.ProxyPrefix, parameter.Ordinal);

    /// <summary>The native shadow struct or span local of <paramref name="parameter"/>, <c>__gsharp_native_{ordinal}</c>.</summary>
    public static string GetNativeLocal(in KernelParameterShape parameter) =>
        FormatOrdinal(GSharpInternalNames.Launcher.NativePrefix, parameter.Ordinal);

    /// <summary>The <c>fixed</c> pointer local of <paramref name="parameter"/>, <c>__gsharp_p_{ordinal}</c>.</summary>
    public static string GetPinnedLocal(in KernelParameterShape parameter) =>
        FormatOrdinal(GSharpInternalNames.Launcher.PinnedPrefix, parameter.Ordinal);

    /// <summary>The <c>fixed</c> temporary pointer local of a <c>ref</c> scalar <paramref name="parameter"/>, <c>__gsharp_p_temp_{ordinal}</c>.</summary>
    public static string GetPinnedTempLocal(in KernelParameterShape parameter) =>
        FormatOrdinal(GSharpInternalNames.Launcher.PinnedTempPrefix, parameter.Ordinal);

    /// <summary>The <c>fixed</c> copy-back pointer local of a <c>ref</c> / <c>out</c> scalar <paramref name="parameter"/>, <c>__gsharp_ptr_{ordinal}</c>.</summary>
    public static string GetScalarPointerLocal(in KernelParameterShape parameter) =>
        FormatOrdinal(GSharpInternalNames.Launcher.ScalarPointerPrefix, parameter.Ordinal);

    /// <summary>The byte size local of <paramref name="parameter"/>, <c>__gsharp_size_{ordinal}</c>.</summary>
    public static string GetSizeLocal(in KernelParameterShape parameter) =>
        FormatOrdinal(GSharpInternalNames.Launcher.SizePrefix, parameter.Ordinal);

    /// <summary>The rented <c>ArrayPool&lt;byte&gt;</c> shadow buffer local of <paramref name="parameter"/>, <c>__gsharp_shadow_{ordinal}</c>.</summary>
    public static string GetShadowLocal(in KernelParameterShape parameter) =>
        FormatOrdinal(GSharpInternalNames.Launcher.ShadowPrefix, parameter.Ordinal);

    /// <summary>The shared shadow buffer local of <paramref name="parameter"/> on the CPU, <c>__gsharp_shared_shadow_{ordinal}</c>.</summary>
    public static string GetSharedShadowLocal(in KernelParameterShape parameter) =>
        FormatOrdinal(GSharpInternalNames.Launcher.SharedShadowPrefix, parameter.Ordinal);

    /// <summary>The list of pinned <c>GCHandle</c> pointers of <paramref name="parameter"/> on the CPU, <c>__gsharp_allocated_ptrs_{ordinal}</c>.</summary>
    public static string GetAllocatedPointersLocal(in KernelParameterShape parameter) =>
        FormatOrdinal(GSharpInternalNames.Launcher.AllocatedPointersPrefix, parameter.Ordinal);

    /// <summary>The element loop index local of a converted array <paramref name="parameter"/>, <c>__gsharp_index_{ordinal}</c>.</summary>
    public static string GetElementIndexLocal(in KernelParameterShape parameter) =>
        FormatOrdinal(GSharpInternalNames.Launcher.ElementIndexPrefix, parameter.Ordinal);

    /// <summary>The host span local of a converted array <paramref name="parameter"/>, <c>__gsharp_host_{ordinal}</c>.</summary>
    public static string GetHostSpanLocal(in KernelParameterShape parameter) =>
        FormatOrdinal(GSharpInternalNames.Launcher.HostSpanPrefix, parameter.Ordinal);

    /// <summary>The temporary element copy local of a read-only converted array <paramref name="parameter"/>, <c>__gsharp_element_{ordinal}</c>.</summary>
    public static string GetElementLocal(in KernelParameterShape parameter) =>
        FormatOrdinal(GSharpInternalNames.Launcher.ElementPrefix, parameter.Ordinal);

    /// <summary>The saved original array reference local of an <c>[In]</c> array <paramref name="parameter"/> on the CPU, <c>__gsharp_source_{ordinal}</c>.</summary>
    public static string GetCopySourceLocal(in KernelParameterShape parameter) =>
        FormatOrdinal(GSharpInternalNames.Launcher.CopySourcePrefix, parameter.Ordinal);

    /// <summary>The cloned array buffer local of an <c>[In]</c> array <paramref name="parameter"/> on the CPU, <c>__gsharp_copy_{ordinal}</c>.</summary>
    public static string GetCopyBufferLocal(in KernelParameterShape parameter) =>
        FormatOrdinal(GSharpInternalNames.Launcher.CopyBufferPrefix, parameter.Ordinal);

    /// <summary>The loop index local that copies nested arrays of <paramref name="parameter"/> on the CPU, <c>__gsharp_copy_index_{ordinal}</c>.</summary>
    public static string GetCopyIndexLocal(in KernelParameterShape parameter) =>
        FormatOrdinal(GSharpInternalNames.Launcher.CopyIndexPrefix, parameter.Ordinal);

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

    private static string FormatOrdinal(string prefix, int ordinal) =>
        prefix + ordinal.ToString(CultureInfo.InvariantCulture);

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
