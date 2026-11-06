using System.Collections.Immutable;
using System.Text;

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

    /// <summary>The public launch option parameters appended to generated kernel wrappers.</summary>
    internal static readonly ImmutableArray<LaunchOption> LaunchOptions =
        ImmutableArray.Create(
            new LaunchOption("uint", "gridDimX", "1"),
            new LaunchOption("uint", "gridDimY", "1"),
            new LaunchOption("uint", "gridDimZ", "1"),
            new LaunchOption("uint", "blockDimX", "1"),
            new LaunchOption("uint", "blockDimY", "1"),
            new LaunchOption("uint", "blockDimZ", "1"),
            new LaunchOption("uint", "sharedMemBytes", "0"),
            new LaunchOption("global::System.IntPtr", "hStream", "default"),
            new LaunchOption("global::System.ReadOnlySpan<global::System.Collections.Generic.KeyValuePair<string, string>>", "macros", "default"));

    /// <summary>
    /// The launch option names joined by <c>, </c>: the arguments that forward the options to another wrapper, and the
    /// list in the GSH0106 message.
    /// </summary>
    public static readonly string LaunchOptionNames = BuildLaunchOptionNames();

    internal static readonly string LaunchOptionParameters = BuildLaunchOptionParameters();

    /// <summary>Whether <paramref name="name"/> is one of the public launch option parameters of the generated host wrapper (GSH0106).</summary>
    public static bool IsReservedLaunchParameterName(string name)
    {
        for (var i = 0; i < LaunchOptions.Length; i++)
        {
            if (LaunchOptions[i].Name == name)
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildLaunchOptionNames()
    {
        var sb = new StringBuilder();
        for (var i = 0; i < LaunchOptions.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            sb.Append(LaunchOptions[i].Name);
        }

        return sb.ToString();
    }

    private static string BuildLaunchOptionParameters()
    {
        var sb = new StringBuilder();
        for (var i = 0; i < LaunchOptions.Length; i++)
        {
            if (i > 0)
            {
                // [WHY] The wrappers have always been written "uint blockDimY = 1,uint blockDimZ = 1"; existing output is
                // not respaced (transpiler-emission section 3), so the missing space is kept, keyed by name
                sb.Append(LaunchOptions[i].Name == "blockDimZ" ? "," : ", ");
            }

            var opt = LaunchOptions[i];
            sb.Append(opt.Type).Append(' ').Append(opt.Name).Append(" = ").Append(opt.DefaultValue);
        }

        return sb.ToString();
    }

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

    /// <summary>The static field that resolves the CUDA function of a kernel per context, <c>__gsharp_hfunc_{key}</c>.</summary>
    public static string GetFunctionPointerFieldName(string memberKey) =>
        GSharpInternalNames.Launcher.FunctionPointerFieldPrefix + memberKey;

    /// <summary>The static field tracking whether a kernel's layout guards have passed, <c>__gsharp_layout_checked_{key}</c>.</summary>
    public static string GetLayoutCheckedFieldName(string memberKey) =>
        GSharpInternalNames.Launcher.LayoutCheckedFieldPrefix + memberKey;

    /// <summary>The method that preloads the CUDA module and resolves the function pointer, <c>WarmupFunctionPointer{key}</c>.</summary>
    public static string GetWarmupMethodName(string memberKey) => WarmupPrefix + memberKey;

    /// <summary>The private CPU P/Invoke delegate type of a kernel, <c>__gsharp_delegate_{key}</c>.</summary>
    public static string GetDelegateTypeName(in KernelEmission kernel) =>
        GSharpInternalNames.Launcher.DelegateTypePrefix + kernel.MemberKey;

    /// <summary>Returns the generated launcher local variable name for <paramref name="parameter"/> of kind <paramref name="kind"/>.</summary>
    public static string GetLocal(LauncherLocal kind, in KernelParameterShape parameter) =>
        FormatOrdinal(GetPrefix(kind), parameter.Ordinal);

    private static string GetPrefix(LauncherLocal kind) => kind switch
    {
        LauncherLocal.DevicePointer => GSharpInternalNames.Launcher.DevicePointerPrefix,
        LauncherLocal.Proxy => GSharpInternalNames.Launcher.ProxyPrefix,
        LauncherLocal.Native => GSharpInternalNames.Launcher.NativePrefix,
        LauncherLocal.Pinned => GSharpInternalNames.Launcher.PinnedPrefix,
        LauncherLocal.PinnedTemp => GSharpInternalNames.Launcher.PinnedTempPrefix,
        LauncherLocal.ScalarPointer => GSharpInternalNames.Launcher.ScalarPointerPrefix,
        LauncherLocal.Size => GSharpInternalNames.Launcher.SizePrefix,
        LauncherLocal.Shadow => GSharpInternalNames.Launcher.ShadowPrefix,
        LauncherLocal.SharedShadow => GSharpInternalNames.Launcher.SharedShadowPrefix,
        LauncherLocal.AllocatedPointers => GSharpInternalNames.Launcher.AllocatedPointersPrefix,
        LauncherLocal.ElementIndex => GSharpInternalNames.Launcher.ElementIndexPrefix,
        LauncherLocal.HostSpan => GSharpInternalNames.Launcher.HostSpanPrefix,
        LauncherLocal.Element => GSharpInternalNames.Launcher.ElementPrefix,
        LauncherLocal.CopySource => GSharpInternalNames.Launcher.CopySourcePrefix,
        LauncherLocal.CopyBuffer => GSharpInternalNames.Launcher.CopyBufferPrefix,
        LauncherLocal.CopyIndex => GSharpInternalNames.Launcher.CopyIndexPrefix,
        _ => throw new System.ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

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
