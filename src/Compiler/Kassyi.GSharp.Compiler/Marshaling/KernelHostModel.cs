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

    /// <summary>
    /// How the CUDA launcher reads the auto result array back: converted element by element through a shadow array
    /// when the returned user struct lays out differently on the device, otherwise <see langword="default"/> (#704).
    /// </summary>
    public ParameterTransfer CudaReturn { get; init; }

    /// <summary>The same for the CPU launcher.</summary>
    public ParameterTransfer CpuReturn { get; init; }

    /// <summary>A <c>[GpuKernel(Cooperative = true)]</c> kernel.</summary>
    public bool IsCooperative { get; init; }

    /// <summary>The kernel in a run-time error of the managed path (<see cref="KernelHostNames.GetMessageName"/>).</summary>
    public string MessageName { get; init; }

    /// <summary>
    /// The bytes of one slot of the grid scratch of the AsParallel() aggregates of the kernel (CUDA, cooperative kernels),
    /// or 0 when it has none (ParallelAggregatePlanner.GetGridScratchSlotBytes).
    /// </summary>
    public int GridScratchSlotBytes { get; init; }

    /// <summary>The parameter list of <c>Launch{Name}</c> as C# compares overloads.</summary>
    public string ManagedSignatureKey { get; init; }

    /// <summary>The parameter list of the typed <c>Launch{Name}Ptr</c> as C# compares overloads.</summary>
    public string TypedPointerSignatureKey { get; init; }

    /// <summary><c>Name(type, type)</c>, for the GSH0059 message.</summary>
    public string DisplaySignature { get; init; }

    public EquatableArray<KernelParameterShape> Parameters { get; init; }

    /// <summary>The user structs that the parameters marshal, one per <c>AotContexts</c> name, in first-use order.</summary>
    public EquatableArray<StructHostSource> Structs { get; init; }

    /// <summary>The user structs with <see cref="TransferClass.Identical"/> on CUDA that the launcher block-copies.</summary>
    public EquatableArray<StructLayoutGuard> CudaLayoutGuards { get; init; }

    /// <summary>
    /// The user structs with <see cref="TransferClass.Identical"/> on the CPU dialect that the CPU launcher passes in the
    /// host layout as it is (by pointer, pinned by <c>ref</c> or as a pinned array).
    /// </summary>
    public EquatableArray<StructLayoutGuard> CpuLayoutGuards { get; init; }

    public bool ReturnsValue => ReturnTypeName is not null;

    /// <summary>Whether the CUDA launcher converts the returned elements (<see cref="CudaReturn"/>).</summary>
    public bool IsCudaConvertedReturn => CudaReturn.ConvertedElementAotStructPath is not null;

    /// <summary>Whether the CPU launcher converts the returned elements (<see cref="CpuReturn"/>).</summary>
    public bool IsCpuConvertedReturn => CpuReturn.ConvertedElementAotStructPath is not null;

    /// <summary>Reads the host launch ABI of <paramref name="kernel"/>.</summary>
    /// <param name="kernel">The kernel.</param>
    /// <param name="structs">The rendered structs of the parameters; the analyzer, which needs none, passes <see langword="default"/>.</param>
    /// <param name="model">The semantic model of the kernel's syntax tree, or <see langword="null"/> when grid scratch analysis is not needed.</param>
    public static KernelHostModel Create(IMethodSymbol kernel, EquatableArray<StructHostSource> structs,
        SemanticModel? model = null)
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
            CudaReturn = ParameterTransfer.CreateForReturn(kernel.ReturnType, HostDialects.Cuda),
            CpuReturn = ParameterTransfer.CreateForReturn(kernel.ReturnType, HostDialects.Cpu),
            IsCooperative = kernel.IsCooperativeKernel(),
            MessageName = KernelHostNames.GetMessageName(kernel),
            GridScratchSlotBytes = model != null && kernel.IsCooperativeKernel() &&
                                   kernel.TryGetDeclaringSyntax<MethodDeclarationSyntax>(out var declaration) &&
                                   declaration.SyntaxTree == model.SyntaxTree
                ? ParallelAggregatePlanner.GetGridScratchSlotBytes(declaration, model)
                : 0,
            ManagedSignatureKey = KernelHostNames.GetSignatureKey(parameters, typedPointers: false),
            TypedPointerSignatureKey = KernelHostNames.GetSignatureKey(parameters, typedPointers: true),
            DisplaySignature = KernelHostNames.FormatSignature(kernel.Name, parameters),
            Parameters = parameters,
            Structs = structs,
            CudaLayoutGuards = CollectLayoutGuards(kernel, HostDialects.Cuda),
            CpuLayoutGuards = CollectLayoutGuards(kernel, HostDialects.Cpu)
        };
    }

    private static EquatableArray<StructLayoutGuard> CollectLayoutGuards(IMethodSymbol kernel,
        ITranspilerDialect dialect)
    {
        var visited = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var guards = ImmutableArray.CreateBuilder<StructLayoutGuard>();

        foreach (var parameter in kernel.Parameters)
        {
            CollectBlockCopiedStructs(parameter.Type, dialect, kernel.ContainingAssembly, visited, guards);
        }

        if (kernel.ReturnType.SpecialType != SpecialType.System_Void)
        {
            CollectBlockCopiedStructs(kernel.ReturnType, dialect, kernel.ContainingAssembly, visited, guards);
        }

        return guards.ToImmutable().AsEquatableArray();
    }

    // [WHY] A block-copied struct is passed in the bytes the CLR gives it, so the predicted size and the offsets of the
    // fields that the launcher can name are checked against the runtime, once per launcher, as a safety net for a layout
    // the compiler mispredicts (#511 review). A nested Identical struct is checked as well.
    private static void CollectBlockCopiedStructs(
        ITypeSymbol type,
        ITranspilerDialect dialect,
        IAssemblySymbol launcherAssembly,
        HashSet<INamedTypeSymbol> visited,
        ImmutableArray<StructLayoutGuard>.Builder guards)
    {
        type = type.UnwrapNullableArrayLike() ?? type;
        if (type.TryGetArrayLikeElementType(out var elementType))
        {
            type = elementType;
        }

        if (!TypeLayoutCalculator.IsUserStruct(type))
        {
            return;
        }

        var named = (INamedTypeSymbol)type;
        if (!visited.Add(named))
        {
            return;
        }

        var layout = TypeLayoutCalculator.GetStructLayout(named, dialect);
        if (layout.Transfer == TransferClass.Identical)
        {
            var fields = ImmutableArray.CreateBuilder<FieldOffsetGuard>();
            foreach (var field in layout.Host.Fields)
            {
                if (IsNameableField(field.Member, launcherAssembly))
                {
                    fields.Add(new FieldOffsetGuard(
                        field.Member.Name.ToCSharpIdentifier(),
                        field.Member.Type.GetFullyQualifiedCsharpName(),
                        field.Offset));
                }
            }

            guards.Add(new StructLayoutGuard(
                named.GetFullyQualifiedCsharpName(),
                GlobalCppNames.GetTypePath(named).ToDisplayString(),
                layout.Host.Size,
                fields.ToImmutable().AsEquatableArray()));
        }

        foreach (var member in StructAbiMembers.Get(named))
        {
            CollectBlockCopiedStructs(member.Type, dialect, launcherAssembly, visited, guards);
        }
    }

    // An ordinary field that the generated launcher can name: public, or internal in the launcher's own assembly. A
    // pointer cannot be a type argument of Unsafe.As, so a pointer field is left to the size check.
    private static bool IsNameableField(StructAbiMember member, IAssemblySymbol launcherAssembly) =>
        member is { Kind: StructAbiMemberKind.Field, Symbol: IFieldSymbol field } &&
        field.Type is not (IPointerTypeSymbol or IFunctionPointerTypeSymbol) &&
        (field.DeclaredAccessibility == Accessibility.Public ||
         (field.DeclaredAccessibility is Accessibility.Internal or Accessibility.ProtectedOrInternal &&
          field.ContainingAssembly.IsEqualTo(launcherAssembly)));
}

/// <summary>
/// The runtime checks emitted at the start of <c>Launch</c> for a block-copied <see cref="TransferClass.Identical"/>
/// struct: <c>Unsafe.SizeOf</c> and the offsets of the fields the launcher can name (<c>Unsafe.ByteOffset</c>).
/// </summary>
/// <param name="FullyQualifiedTypeName">The <c>global::</c>-qualified C# type name.</param>
/// <param name="DisplayTypeName">The user-readable type name without <c>global::</c>.</param>
/// <param name="ExpectedSize">The predicted host size in bytes.</param>
/// <param name="Fields">The fields whose predicted host offsets are checked.</param>
internal readonly record struct StructLayoutGuard(
    string FullyQualifiedTypeName,
    string DisplayTypeName,
    int ExpectedSize,
    EquatableArray<FieldOffsetGuard> Fields);

/// <summary>One field of a <see cref="StructLayoutGuard"/>.</summary>
/// <param name="Name">The field name as a C# identifier.</param>
/// <param name="FullyQualifiedTypeName">The <c>global::</c>-qualified field type.</param>
/// <param name="ExpectedOffset">The predicted host offset in bytes.</param>
internal readonly record struct FieldOffsetGuard(string Name, string FullyQualifiedTypeName, int ExpectedOffset);

/// <summary>The partial class that the generated host members of a kernel or device function go into.</summary>
/// <param name="HintName">The source hint name of the class's wrapper (<see cref="GlobalCppNames.GetHintName"/>).</param>
/// <param name="Namespace">The dot-separated namespace, or empty for the global namespace.</param>
/// <param name="Name">The class name.</param>
/// <param name="Accessibility">The declared accessibility keywords, which the generated part must repeat (CS0262).</param>
/// <param name="IsPublic">Whether the class is public, which decides the CS0649 suppression.</param>
/// <param name="OuterDeclarations">
/// The partial declarations of the types that contain the class, outermost first (<c>partial class Outer</c>), so
/// that a part of a nested class is written inside them.
/// </param>
internal readonly record struct HostClassHeader(
    string HintName,
    string Namespace,
    string Name,
    string Accessibility,
    bool IsPublic,
    EquatableArray<string> OuterDeclarations)
{
    public bool IsGlobalNamespace => Namespace.Length == 0;

    public static HostClassHeader Create(INamedTypeSymbol type) => new(
        GlobalCppNames.GetHintName(type),
        type.ContainingNamespace.GetFullNamespaceName(),
        type.Name,
        SyntaxFacts.GetText(type.DeclaredAccessibility),
        type.DeclaredAccessibility == Microsoft.CodeAnalysis.Accessibility.Public,
        CreateOuterDeclarations(type));

    private static EquatableArray<string> CreateOuterDeclarations(INamedTypeSymbol type)
    {
        if (type.ContainingType == null)
        {
            return default;
        }

        var declarations = ImmutableArray.CreateBuilder<string>();
        for (var outer = type.ContainingType; outer != null; outer = outer.ContainingType)
        {
            var keyword = outer.IsRecord
                ? outer.TypeKind == TypeKind.Struct ? "record struct" : "record"
                : outer.TypeKind == TypeKind.Struct ? "struct" : outer.TypeKind == TypeKind.Interface ? "interface" : "class";
            var name = outer.Name;
            for (var i = 0; i < outer.TypeParameters.Length; i++)
            {
                name += (i == 0 ? "<" : ", ") + outer.TypeParameters[i].Name;
            }

            name += outer.TypeParameters.IsEmpty ? "" : ">";
            declarations.Insert(0, $"partial {keyword} {name}");
        }

        return declarations.ToImmutable().AsEquatableArray();
    }
}

/// <summary>The native shadow struct and the marshaling methods of one user struct, as the wrapper declares them.</summary>
/// <param name="AotStructPath">The <c>AotContexts</c> path, which identifies the struct within a class.</param>
/// <param name="CudaText">The CUDA declaration and the <c>Marshal_</c> / <c>Unmarshal_</c> methods.</param>
/// <param name="CpuText">The CPU declaration and the <c>Marshal_</c> / <c>Unmarshal_</c> methods.</param>
internal readonly record struct StructHostSource(string AotStructPath, string CudaText, string CpuText)
{
    public string GetText(bool isCuda) => isCuda ? CudaText : CpuText;
}

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
        var structIndices = new Dictionary<string, int>(StringComparer.Ordinal);
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
