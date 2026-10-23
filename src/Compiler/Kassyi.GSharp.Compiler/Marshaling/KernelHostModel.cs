using Kassyi.GSharp.Compiler.Models;

namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// The host launch ABI of one <c>[GpuKernel]</c>: everything the launch wrappers need from the kernel's symbol, as
/// values of the cached pipeline model.
/// </summary>
/// <remarks>
/// Nothing here depends on the other kernels of the class; <see cref="KernelClassModel.Create"/> derives what does
/// (overloads, member keys, the wrappers left out) from the models of one compilation.
/// </remarks>
internal readonly record struct KernelHostModel
{
    public string Name { get; init; }

    /// <summary>The position among the ordinary methods with the same name, or -1 (<see cref="GlobalCppNames.GetOverloadOrdinal"/>).</summary>
    public int OverloadOrdinal { get; init; }

    /// <summary>The exported entry point (<see cref="GlobalCppNames.GetKernelName"/>).</summary>
    public string KernelName { get; init; }

    /// <summary>The managed fallback's per-thread context type.</summary>
    public string ContextName { get; init; }

    /// <summary>The managed fallback's state machine method.</summary>
    public string StateMachineName { get; init; }

    /// <summary>The CPU export of the launcher.</summary>
    public string LaunchExportName { get; init; }

    /// <summary>The fully qualified return type, or <see langword="null"/> for <c>void</c>.</summary>
    public string? ReturnTypeName { get; init; }

    /// <summary>A <c>[GpuKernel(Cooperative = true)]</c> kernel.</summary>
    public bool IsCooperative { get; init; }

    /// <summary>The parameter list of <c>Launch{Name}</c> as C# compares overloads.</summary>
    public string ManagedSignatureKey { get; init; }

    /// <summary>The parameter list of the typed <c>Launch{Name}Ptr</c> as C# compares overloads.</summary>
    public string TypedPointerSignatureKey { get; init; }

    /// <summary><c>Name(type, type)</c>, for the GSH0059 message.</summary>
    public string DisplaySignature { get; init; }

    public EquatableArray<KernelParameterShape> Parameters { get; init; }

    /// <summary>The user structs that the parameters marshal, one per <c>AotContexts</c> name, in first-use order.</summary>
    public EquatableArray<StructHostSource> Structs { get; init; }

    public bool ReturnsValue => ReturnTypeName is not null;

    /// <summary>Reads the host launch ABI of <paramref name="kernel"/>.</summary>
    /// <param name="kernel">The kernel.</param>
    /// <param name="structs">The rendered structs of the parameters; the analyzer, which needs none, passes <see langword="default"/>.</param>
    public static KernelHostModel Create(IMethodSymbol kernel, EquatableArray<StructHostSource> structs)
    {
        var builder = ImmutableArray.CreateBuilder<KernelParameterShape>(kernel.Parameters.Length);
        kernel.Parameters
            .AsStackEnumerable()
            .Select(KernelParameterShape.Classify)
            .AddTo(builder);
        var parameters = builder.MoveToImmutable().AsEquatableArray();

        return new KernelHostModel
        {
            Name = kernel.Name,
            OverloadOrdinal = GlobalCppNames.GetOverloadOrdinal(kernel),
            KernelName = GlobalCppNames.GetKernelName(kernel),
            ContextName = GlobalCppNames.GetKernelDerivedName(GSharpInternalNames.Kernel.ContextPrefix, kernel),
            StateMachineName = GlobalCppNames.GetKernelDerivedName(GSharpInternalNames.Kernel.StateMachinePrefix, kernel),
            LaunchExportName = GlobalCppNames.GetKernelDerivedName(GSharpInternalNames.Kernel.LaunchExportPrefix, kernel),
            ReturnTypeName = kernel.ReturnType.SpecialType == SpecialType.System_Void
                ? null
                : kernel.ReturnType.GetFullyQualifiedCsharpName(),
            IsCooperative = kernel.IsCooperativeKernel(),
            ManagedSignatureKey = KernelHostNames.GetSignatureKey(parameters, typedPointers: false),
            TypedPointerSignatureKey = KernelHostNames.GetSignatureKey(parameters, typedPointers: true),
            DisplaySignature = KernelHostNames.FormatSignature(kernel.Name, parameters),
            Parameters = parameters,
            Structs = structs
        };
    }
}

/// <summary>The partial class that the generated host members of a kernel or device function go into.</summary>
/// <param name="HintName">The source hint name of the class's wrapper (<see cref="GlobalCppNames.GetHintName"/>).</param>
/// <param name="Namespace">The dot-separated namespace, or empty for the global namespace.</param>
/// <param name="Name">The class name.</param>
/// <param name="Accessibility">The declared accessibility keywords, which the generated part must repeat (CS0262).</param>
/// <param name="IsPublic">Whether the class is public, which decides the CS0649 suppression.</param>
internal readonly record struct HostClassHeader(
    string HintName,
    string Namespace,
    string Name,
    string Accessibility,
    bool IsPublic)
{
    public bool IsGlobalNamespace => Namespace.Length == 0;

    public static HostClassHeader Create(INamedTypeSymbol type) => new(
        GlobalCppNames.GetHintName(type),
        type.ContainingNamespace.GetFullNamespaceName(),
        type.Name,
        SyntaxFacts.GetText(type.DeclaredAccessibility),
        type.DeclaredAccessibility == Microsoft.CodeAnalysis.Accessibility.Public);
}

/// <summary>The native shadow struct and the marshaling methods of one user struct, as the wrapper declares them.</summary>
/// <param name="AotStructPath">The <c>AotContexts</c> path, which identifies the struct within a class.</param>
/// <param name="Text">The declaration and the <c>Marshal_</c> / <c>Unmarshal_</c> methods.</param>
internal readonly record struct StructHostSource(string AotStructPath, string Text);

/// <summary>One kernel of a <see cref="KernelClassModel"/>, with what its siblings decide.</summary>
/// <param name="Host">The kernel's own launch ABI.</param>
/// <param name="MemberKey">The suffix of the per-kernel host members: the name, or <c>{name}__o{n}</c> for an overloaded kernel.</param>
/// <param name="IsOverloaded">Whether another <c>[GpuKernel]</c> of the class has the same name.</param>
/// <param name="IsIndistinguishable">Whether an earlier overload has the same wrapper parameter list (GSH0059), so the wrappers are left out.</param>
/// <param name="CudaSource">The CUDA source of the kernel.</param>
/// <param name="CppSource">The C++ source of the kernel.</param>
/// <param name="ManagedCSharpSource">The managed fallback of the kernel.</param>
/// <param name="InternalError">The exception that stopped the transpilation (GSH0048), or empty.</param>
internal readonly record struct KernelEmission(
    KernelHostModel Host,
    string MemberKey,
    bool IsOverloaded,
    bool IsIndistinguishable,
    string CudaSource,
    string CppSource,
    string ManagedCSharpSource,
    string InternalError)
{
    public string GetSource(bool isCuda) => isCuda ? CudaSource : CppSource;
}

/// <summary>The kernels of one class, which the generator emits as one wrapper source.</summary>
internal readonly record struct KernelClassModel(
    HostClassHeader Class,
    EquatableArray<KernelEmission> Kernels,
    EquatableArray<StructHostSource> Structs)
{
    /// <summary>
    /// Builds the class model from the kernels of one class, in the order given; the overloads, member keys and the
    /// indistinguishable overloads are derived from these models only.
    /// </summary>
    public static KernelClassModel Create(HostClassHeader header, IReadOnlyList<TranspiledKernelResult> kernels)
    {
        var hosts = ImmutableArray.CreateBuilder<KernelHostModel>(kernels.Count);
        kernels
            .AsStackEnumerable()
            .Select(static kernel => kernel.Host)
            .AddTo(hosts);

        var emissions = ImmutableArray.CreateBuilder<KernelEmission>(kernels.Count);
        var structs = ImmutableArray.CreateBuilder<StructHostSource>();
        var structPaths = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < kernels.Count; i++)
        {
            var kernel = kernels[i];
            var isOverloaded = IsOverloaded(hosts, i);
            var memberKey = isOverloaded
                ? $"{kernel.Host.Name}__o{kernel.Host.OverloadOrdinal.ToString(CultureInfo.InvariantCulture)}"
                : kernel.Host.Name;
            emissions.Add(new KernelEmission(
                kernel.Host,
                memberKey,
                isOverloaded,
                TryFindIndistinguishableOverload(hosts, i, out _, out _),
                kernel.CudaSource,
                kernel.CppSource,
                kernel.ManagedCSharpSource,
                kernel.InternalError));

            kernel.Host.Structs
                .AsStackEnumerable()
                .AddIf(structs, structPaths, static (s, paths) => paths.Add(s.AotStructPath));
        }

        return new KernelClassModel(
            header,
            emissions.MoveToImmutable().AsEquatableArray(),
            structs.ToImmutable().AsEquatableArray());
    }

    /// <summary>Whether another kernel of <paramref name="kernels"/> has the name of <c>kernels[index]</c>.</summary>
    public static bool IsOverloaded(IReadOnlyList<KernelHostModel> kernels, int index) =>
        kernels
            .AsStackEnumerable()
            .Count(kernels[index].Name, static (k, name) => k.Name == name) > 1;

    /// <summary>
    /// Finds an earlier overload of <c>kernels[index]</c> whose <c>Launch{Name}</c> or typed <c>Launch{Name}Ptr</c> has the
    /// same parameter list, which C# would reject as a duplicate member (GSH0059).
    /// </summary>
    /// <param name="kernels">The kernels of one class.</param>
    /// <param name="index">The kernel to check.</param>
    /// <param name="earlier">The index of the earlier overload, or -1.</param>
    /// <param name="wrapperName">The wrapper that would be duplicated, or empty.</param>
    public static bool TryFindIndistinguishableOverload(IReadOnlyList<KernelHostModel> kernels, int index,
        out int earlier, out string wrapperName)
    {
        earlier = -1;
        wrapperName = string.Empty;
        if (!IsOverloaded(kernels, index))
        {
            return false;
        }

        var kernel = kernels[index];
        for (var i = 0; i < kernels.Count; i++)
        {
            var other = kernels[i];
            if (other.Name != kernel.Name || other.OverloadOrdinal >= kernel.OverloadOrdinal)
            {
                continue;
            }

            if (string.Equals(other.ManagedSignatureKey, kernel.ManagedSignatureKey, StringComparison.Ordinal))
            {
                earlier = i;
                wrapperName = KernelHostNames.GetLaunchName(kernel);
                return true;
            }

            if (string.Equals(other.TypedPointerSignatureKey, kernel.TypedPointerSignatureKey, StringComparison.Ordinal))
            {
                earlier = i;
                wrapperName = KernelHostNames.GetTypedPointerLaunchName(kernel);
                return true;
            }
        }

        return false;
    }
}

/// <summary>A kernel whose function pointer the CUDA module context warms up.</summary>
/// <param name="Class">The class that declares the warmup method.</param>
/// <param name="MemberKey">The suffix of <c>WarmupFunctionPointer{key}</c>.</param>
internal readonly record struct KernelWarmup(HostClassHeader Class, string MemberKey);

/// <summary>The kernels of one compilation: the class models, and the warmups in the order of the kernels.</summary>
internal readonly record struct KernelModuleModel(
    EquatableArray<KernelClassModel> Classes,
    EquatableArray<KernelWarmup> Warmups)
{
    /// <summary>Groups <paramref name="kernels"/> by class, in the order the classes first appear.</summary>
    public static KernelModuleModel Create(ImmutableArray<TranspiledKernelResult> kernels)
    {
        var kernelsByClass = new Dictionary<string, ImmutableArray<TranspiledKernelResult>.Builder>(StringComparer.Ordinal);
        var classIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        var classOrder = ImmutableArray.CreateBuilder<HostClassHeader>();
        foreach (var kernel in kernels)
        {
            if (!kernelsByClass.TryGetValue(kernel.Class.HintName, out var group))
            {
                group = ImmutableArray.CreateBuilder<TranspiledKernelResult>();
                kernelsByClass[kernel.Class.HintName] = group;
                classIndices[kernel.Class.HintName] = classOrder.Count;
                classOrder.Add(kernel.Class);
            }

            group.Add(kernel);
        }

        var classes = ImmutableArray.CreateBuilder<KernelClassModel>(classOrder.Count);
        classOrder
            .AsStackEnumerable()
            .Select(kernelsByClass, static (header, groups) => KernelClassModel.Create(header, groups[header.HintName]))
            .AddTo(classes);
        var classModels = classes.MoveToImmutable();

        // The module context warms the kernels up in their own order, which may interleave the classes
        var positions = new Dictionary<string, int>(StringComparer.Ordinal);
        var warmups = ImmutableArray.CreateBuilder<KernelWarmup>(kernels.Length);
        foreach (var kernel in kernels)
        {
            var hintName = kernel.Class.HintName;
            positions.TryGetValue(hintName, out var position);
            positions[hintName] = position + 1;
            var emission = classModels[classIndices[hintName]].Kernels[position];
            if (!emission.IsIndistinguishable)
            {
                warmups.Add(new KernelWarmup(kernel.Class, emission.MemberKey));
            }
        }

        return new KernelModuleModel(classModels.AsEquatableArray(), warmups.ToImmutable().AsEquatableArray());
    }
}

/// <summary>The managed state machines of the device functions of one class that may suspend.</summary>
/// <param name="Class">The class that the state machines go into.</param>
/// <param name="Sources">The state machines, in declaration order.</param>
internal readonly record struct DeviceStateMachineGroup(HostClassHeader Class, EquatableArray<string> Sources);
