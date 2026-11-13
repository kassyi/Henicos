using Kassyi.GSharp.Compiler.Models;

namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>The kernels of one class, which the generator emits as one wrapper source.</summary>
/// <param name="Class">The class.</param>
/// <param name="Kernels">The kernels, in the order given.</param>
/// <param name="Structs">The structs that <c>AotContexts</c> marshals.</param>
/// <param name="ErrorSites">The error sites of the kernels.</param>
/// <param name="WarmupRelays">The warmup relays of the containers.</param>
/// <param name="OmitsClassMembers">
/// Whether a kernel's own method has the name of a member that G# generates in every kernel class (GSH0059,
/// <see cref="LauncherSignatures.ClassMemberNames"/>): the class then gets none of them and no kernel gets host
/// members, since leaving out the kernel does not remove the collision.
/// </param>
internal readonly record struct KernelClassModel(
    HostClassHeader Class,
    EquatableArray<KernelEmission> Kernels,
    EquatableArray<StructHostSource> Structs,
    EquatableArray<ErrorSiteEntry> ErrorSites = default,
    EquatableArray<WarmupRelay> WarmupRelays = default,
    bool OmitsClassMembers = false)
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
    /// kernels whose generated members another kernel already has (<see cref="LauncherSignatures.FindConflicts"/>) are
    /// derived from these models only.
    /// </summary>
    public static KernelClassModel Create(HostClassHeader header, IReadOnlyList<TranspiledKernelResult> kernels,
        bool emitsSource)
    {
        var hosts = ImmutableArray.CreateBuilder<KernelHostModel>(kernels.Count);
        kernels
            .AsStackEnumerable()
            .Select(static kernel => kernel.Host)
            .AddTo(hosts);

        var conflicts = LauncherSignatures.FindConflicts(hosts, emitsSource);
        var omitsClassMembers = conflicts
            .AsStackEnumerable()
            .Any(static conflict => conflict.IsClassMember);
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
            emissions.Add(new KernelEmission(
                kernel.Host,
                KernelHostNames.GetMemberKey(kernel.Host, isOverloaded),
                isOverloaded,
                omitsClassMembers || conflicts[i].IsConflict,
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
            WarmupRelayPlan.Create(header, kernelEmissions),
            omitsClassMembers);
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
