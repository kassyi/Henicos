using Kassyi.GSharp.Compiler.Models;

namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>The kernels of one class, which the generator emits as one wrapper source.</summary>
internal readonly record struct KernelClassModel(
    HostClassHeader Class,
    EquatableArray<KernelEmission> Kernels,
    EquatableArray<StructHostSource> Structs,
    EquatableArray<ErrorSiteEntry> ErrorSites = default,
    EquatableArray<WarmupRelay> WarmupRelays = default)
{
    /// <summary>
    /// The accessibility of the <c>AotContexts</c> class: the narrowest that its structs need
    /// (<see cref="StructHostSource.Accessibility"/>), <see cref="Accessibility.Public"/> without any.
    /// </summary>
    public Accessibility AotContextsAccessibility => GetAotContextsAccessibility(Structs);

    private static Accessibility GetAotContextsAccessibility(IEnumerable<StructHostSource> structs)
    {
        var accessibility = Accessibility.Public;
        foreach (var s in structs)
        {
            accessibility = GeneratedMemberAccessibility.Narrowest(accessibility, s.Accessibility);
        }

        return accessibility;
    }

    /// <summary>
    /// The accessibility of each launcher of <paramref name="kernel"/>: <c>Launch{Name}</c> takes the one its
    /// parameter and return types allow (<see cref="KernelHostModel.LauncherAccessibility"/>); a pointer launcher that
    /// names the <c>AotContexts</c> class (a shadow struct, or a <c>GpuBuffer</c> of one) is narrowed to that class's
    /// (<see cref="AotContextsAccessibility"/>). The untyped <c>Launch{Name}Ptr</c> of an overloaded kernel is private:
    /// callers pick the overload through the typed one.
    /// </summary>
    public LauncherAccessibilities GetLauncherAccessibilities(in KernelEmission kernel)
    {
        var launcher = kernel.Host.LauncherAccessibility;
        var aotContexts = AotContextsAccessibility;
        var pointer = kernel.Host.Parameters
            .AsStackEnumerable()
            .Any(static p => PointerLaunchPlan.Classify(p) == PointerArgumentKind.ShadowStruct)
            ? GeneratedMemberAccessibility.Narrowest(launcher, aotContexts)
            : launcher;
        var typedPointer = kernel.Host.Parameters
            .AsStackEnumerable()
            .Any(TypedPointerNamesShadowStruct)
            ? GeneratedMemberAccessibility.Narrowest(launcher, aotContexts)
            : launcher;
        return new LauncherAccessibilities(
            launcher,
            kernel.IsOverloaded ? Accessibility.Private : pointer,
            typedPointer
        );
    }

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
        var structIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        var rawClassSites = ImmutableArray.CreateBuilder<ErrorSiteEntry>();
        for (var i = 0; i < kernels.Count; i++)
        {
            var kernel = kernels[i];
            if (!kernel.ErrorSites.IsDefaultOrEmpty)
            {
                foreach (var site in kernel.ErrorSites)
                {
                    rawClassSites.Add(site);
                }
            }

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
                kernel.InternalError,
                kernel.ErrorSites));

            foreach (var s in kernel.Host.Structs)
            {
                if (!structIndices.TryGetValue(s.AotStructPath, out var index))
                {
                    structIndices.Add(s.AotStructPath, structs.Count);
                    structs.Add(s);
                }
                else if (structs[index].CpuText.Length == 0 && s.CpuText.Length > 0)
                {
                    // One kernel converts an array of the struct on CUDA only, another passes the struct on the CPU too
                    structs[index] = s;
                }
            }
        }

        ErrorSiteTable.TryBuildModuleSites(rawClassSites, out var classErrorSites, out _, out _);
        var kernelEmissions = emissions.MoveToImmutable();
        return new KernelClassModel(
            header,
            kernelEmissions.AsEquatableArray(),
            structs.ToImmutable().AsEquatableArray(),
            classErrorSites.AsEquatableArray(),
            WarmupRelayPlan.Create(header, kernelEmissions));
    }

    /// <summary>
    /// Whether the typed <c>Launch{Name}Ptr</c> of an overloaded kernel names the <c>AotContexts</c> class for
    /// <paramref name="parameter"/>: a <c>GpuBuffer</c> of a shadow struct, or the shadow struct itself where it writes
    /// no buffer.
    /// </summary>
    public static bool TypedPointerNamesShadowStruct(KernelParameterShape parameter) =>
        parameter.BufferElementTypeName is not null
            ? parameter.IsShadowStructBuffer
            : PointerLaunchPlan.Classify(parameter) == PointerArgumentKind.ShadowStruct;

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

/// <summary>
/// The accessibility of each launcher of one kernel (<see cref="KernelClassModel.GetLauncherAccessibilities"/>).
/// </summary>
/// <param name="Launcher"><c>Launch{Name}</c>.</param>
/// <param name="Pointer">The untyped <c>Launch{Name}Ptr</c>.</param>
/// <param name="TypedPointer">The typed <c>Launch{Name}Ptr</c> of an overloaded kernel.</param>
internal readonly record struct LauncherAccessibilities(
    Accessibility Launcher,
    Accessibility Pointer,
    Accessibility TypedPointer);
