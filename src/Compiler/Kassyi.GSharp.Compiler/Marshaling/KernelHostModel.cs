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

    /// <summary>
    /// The accessibility of the launchers and of the managed state machine: the widest under which they name only
    /// types at least as accessible as themselves, whatever the kernel's own accessibility (#1284).
    /// </summary>
    public Accessibility LauncherAccessibility { get; init; }

    /// <summary>
    /// The method through which a container warms up the kernel of a class the module context cannot name
    /// (<see cref="HostClassHeader.WarmupRelayHosts"/>).
    /// </summary>
    public string WarmupRelayName { get; init; }

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

    /// <summary>Whether the kernel is a static method, which the C# execution can call without an object.</summary>
    public bool IsStatic { get; init; }

    /// <summary>Whether the kernel or any device function it calls reaches a warp shuffle or warp match (#1001).</summary>
    public bool ReachesWarpShuffle { get; init; }

    /// <summary>
    /// Whether the kernel may wait at a barrier that counts a fixed number of arrivals: a cooperative kernel (its grid
    /// barrier, also the implicit one of an <c>AsParallel()</c> grid aggregate), or a kernel that reaches an mbarrier
    /// or <c>Gpu.SyncGrid()</c>. A captured launch of such a kernel needs an abort word of its own (#1637).
    /// </summary>
    public bool WaitsForArrivals { get; init; }

    /// <summary>The <c>global::</c>-qualified name of the class that declares the kernel, for qualifying sibling members.</summary>
    public string ClassQualifiedName { get; init; }

    /// <summary>Whether any parameter uses a reserved launch option parameter name (GSH0106), so the wrapper is left out.</summary>
    public bool HasReservedLaunchParameterName { get; init; }

    /// <summary>
    /// Why the launcher cannot be generated (GSH0108: a field type that the kernel's class cannot name), as the message
    /// of the exception that the <c>Launch</c> method written in its place throws; <see langword="null"/> otherwise.
    /// No struct marshaling or layout guard is written for the kernel, so that the generated C# compiles and the build
    /// shows the analyzer's GSH0108.
    /// </summary>
    public string? LauncherUnavailableReason { get; init; }

    /// <summary>The kernel in a run-time error of the managed path (<see cref="KernelHostNames.GetMessageName"/>).</summary>
    public string MessageName { get; init; }

    /// <summary>
    /// The first construct that the kernel reaches and that cannot run one GPU thread at a time, with why
    /// (<see cref="CSharpExecutionAnalysis.FindObstacle"/>), or <see langword="null"/>: the launcher's C# execution
    /// then throws instead of running the kernel. Always <see langword="null"/> without a semantic model (the
    /// analyzer).
    /// </summary>
    public string? CSharpExecutionObstacle { get; init; }

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

    /// <summary>The user structs that the parameters and return type marshal, one per <c>AotContexts</c> name, in first-use order.</summary>
    public EquatableArray<StructHostSource> Structs { get; init; }

    /// <summary>The user structs with <see cref="TransferClass.Identical"/> on CUDA that the launcher block-copies.</summary>
    public EquatableArray<StructLayoutGuard> CudaLayoutGuards { get; init; }

    /// <summary>
    /// The user structs with <see cref="TransferClass.Identical"/> on the CPU dialect that the CPU launcher passes in the
    /// host layout as it is (by pointer, pinned by <c>ref</c> or as a pinned array).
    /// </summary>
    public EquatableArray<StructLayoutGuard> CpuLayoutGuards { get; init; }

    public bool IsUnsafeContext { get; init; }

    public bool ReturnsValue => ReturnTypeName is not null;

    /// <summary>Whether the CUDA launcher converts the returned elements (<see cref="CudaReturn"/>).</summary>
    public bool IsCudaConvertedReturn => CudaReturn.ConvertedElementAotStructPath is not null;

    /// <summary>Whether the CPU launcher converts the returned elements (<see cref="CpuReturn"/>).</summary>
    public bool IsCpuConvertedReturn => CpuReturn.ConvertedElementAotStructPath is not null;

    /// <summary>Reads the host launch ABI of <paramref name="kernel"/>.</summary>
    /// <param name="kernel">The kernel.</param>
    /// <param name="structs">The rendered structs of the parameters and return type; the analyzer, which needs none, passes <see langword="default"/>.</param>
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
        var unavailableReason = model is not null && LauncherTypeAccess.Find(kernel, model.Compilation) is { } found
            ? FormatUnavailableReason(kernel, found)
            : null;
        var hasLauncher = unavailableReason is null;

        return new KernelHostModel
        {
            Name = kernel.Name,
            OverloadOrdinal = GlobalCppNames.GetOverloadOrdinal(kernel),
            KernelName = GlobalCppNames.GetKernelName(kernel),
            ContextName = GlobalCppNames.GetKernelDerivedName(GSharpInternalNames.Kernel.ContextPrefix, kernel),
            StateMachineName = GlobalCppNames.GetKernelDerivedName(GSharpInternalNames.Kernel.StateMachinePrefix, kernel),
            LaunchExportName = GlobalCppNames.GetKernelDerivedName(GSharpInternalNames.Kernel.LaunchExportPrefix, kernel),
            LauncherAccessibility = GeneratedMemberAccessibility.OfSignature(kernel).ChooseIn(kernel.ContainingType),
            WarmupRelayName =
                GlobalCppNames.GetKernelDerivedName(GSharpInternalNames.Launcher.WarmupRelayPrefix, kernel),
            ReturnTypeName = kernel.ReturnType.SpecialType == SpecialType.System_Void
                ? null
                : kernel.ReturnType.GetFullyQualifiedCsharpName(),
            IsUnsafeContext = NeedsUnsafeContext(kernel),
            CudaReturn = ParameterTransfer.CreateForReturn(kernel.ReturnType, HostDialects.Cuda, kernel.ContainingType),
            CpuReturn = ParameterTransfer.CreateForReturn(kernel.ReturnType, HostDialects.Cpu, kernel.ContainingType),
            IsCooperative = kernel.IsCooperativeKernel(),
            IsStatic = kernel.IsStatic,
            ReachesWarpShuffle = model != null && SuspensionAnalysis.ReachesWarpShuffle(kernel, model.Compilation),
            WaitsForArrivals = kernel.IsCooperativeKernel() ||
                               (model != null && SuspensionAnalysis.ReachesArrivalBarrier(kernel, model.Compilation)),
            ClassQualifiedName = AotHostNames.GetHostClassQualifiedName(kernel.ContainingType),
            HasReservedLaunchParameterName = kernel.Parameters
                .AsStackEnumerable()
                .Any(static p => KernelHostNames.IsReservedLaunchParameterName(p.Name)),
            MessageName = KernelHostNames.GetMessageName(kernel),
            CSharpExecutionObstacle = model != null
                ? CSharpExecutionAnalysis.FindObstacle(kernel, model.Compilation)
                : null,
            GridScratchSlotBytes = model != null && kernel.IsCooperativeKernel() &&
                                   kernel.TryGetDeclaringSyntax<MethodDeclarationSyntax>(out var declaration) &&
                                   declaration.SyntaxTree == model.SyntaxTree
                ? ParallelAggregatePlanner.GetGridScratchSlotBytes(declaration, model)
                : 0,
            ManagedSignatureKey = KernelHostNames.GetSignatureKey(parameters, typedPointers: false),
            TypedPointerSignatureKey = KernelHostNames.GetSignatureKey(parameters, typedPointers: true),
            DisplaySignature = KernelHostNames.FormatSignature(kernel.Name, parameters),
            Parameters = parameters,
            LauncherUnavailableReason = unavailableReason,
            Structs = hasLauncher ? structs : default,
            CudaLayoutGuards = hasLauncher ? CollectLayoutGuards(kernel, HostDialects.Cuda) : default,
            CpuLayoutGuards = hasLauncher ? CollectLayoutGuards(kernel, HostDialects.Cpu) : default
        };
    }

    // <kernel> cannot be launched: the type '<T>' of field '<path>' is not accessible from '<class>' (GSH0108).
    private static string FormatUnavailableReason(IMethodSymbol kernel, InaccessibleFieldType found) =>
        $"{kernel.Name} cannot be launched: the type '{TypeLayoutCalculator.FormatTypeName(found.FieldType)}' " +
        "of field " +
        $"'{found.FieldPath}' is not accessible from '{TypeLayoutCalculator.FormatTypeName(kernel.ContainingType)}' " +
        $"({GSharpDiagnostics.StructFieldTypeInaccessibleToLauncher.Id}).";

    // The managed state machine keeps pointers in its context, so the context becomes an unsafe struct pinned by the launcher
    private static bool NeedsUnsafeContext(IMethodSymbol kernel)
    {
        if (kernel.TryGetDeclaringSyntax<MethodDeclarationSyntax>(out var decl) &&
            ManagedStateMachineAvailability.NeedsUnsafe(kernel, decl))
        {
            return true;
        }

        return kernel.Parameters
            .AsStackEnumerable()
            .Any(static p => p.Type is IPointerTypeSymbol or IFunctionPointerTypeSymbol);
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
                if (HostFieldAccess.CanCheckOffset(field.Member, launcherAssembly))
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
/// <param name="Namespace">
/// The namespace as C# source spells it (<c>@event.P3</c>), or empty for the global namespace.
/// </param>
/// <param name="Name">
/// The class name and type parameters as C# source spells them (<c>@class</c>, <c>G&lt;T&gt;</c>).
/// </param>
/// <param name="Keyword">
/// The keyword of the user's declaration after <c>partial</c> (<c>class</c>, or <c>record</c> for a record class),
/// which every part must repeat (CS0261).
/// </param>
/// <param name="Accessibility">The declared accessibility keywords, which the generated part must repeat (CS0262).</param>
/// <param name="IsPublic">Whether the class is public, which decides the CS0649 suppression.</param>
/// <param name="OuterDeclarations">
/// The partial declarations of the types that contain the class, outermost first (<c>partial class Outer</c>, names
/// as <see cref="CSharpTypeNameWriter.GetDeclaredTypeName"/> spells them), so
/// that a part of a nested class is written inside them.
/// </param>
/// <param name="FullyQualifiedName">The <c>global::</c>-qualified C# type name, including any enclosing types.</param>
/// <param name="WarmupRelayHosts">
/// The <c>global::</c>-qualified names of the containing types that relay the warmups of the class's kernels, outermost
/// first, or empty when the module context can name the class (<see cref="CreateWarmupRelayHosts"/>, #1282).
/// </param>
/// <param name="DefinedSymbols">
/// The symbols that the generated files of the class <c>#define</c>, so that C# keeps there the
/// <c>[Conditional]</c> calls that a <c>#define</c> of the user's file keeps
/// (<see cref="RemovedCalls.GetSymbolsToDefine"/>).
/// </param>
internal readonly record struct HostClassHeader(
    string HintName,
    string Namespace,
    string Name,
    string Keyword,
    string Accessibility,
    bool IsPublic,
    EquatableArray<string> OuterDeclarations,
    string FullyQualifiedName,
    EquatableArray<string> WarmupRelayHosts,
    EquatableArray<string> DefinedSymbols = default)
{
    public bool IsGlobalNamespace => Namespace.Length == 0;

    public static HostClassHeader Create(INamedTypeSymbol type, Compilation compilation) => new(
        GlobalCppNames.GetHintName(type),
        type.ContainingNamespace.GetCsharpNamespaceName(),
        type.GetDeclaredTypeName(),
        GetKeyword(type),
        SyntaxFacts.GetText(type.DeclaredAccessibility),
        type.DeclaredAccessibility == Microsoft.CodeAnalysis.Accessibility.Public,
        CreateOuterDeclarations(type),
        type.GetFullyQualifiedCsharpName(),
        CreateWarmupRelayHosts(type, compilation),
        RemovedCalls.GetSymbolsToDefine(type, compilation));

    /// <summary>
    /// The containing types of <paramref name="type"/> that relay its warmups, outermost first: the innermost container
    /// that code anywhere in the assembly can name, and each container inside it. Empty when the assembly can name
    /// <paramref name="type"/> itself.
    /// </summary>
    /// <remarks>
    /// The module context, a class of its own namespace, cannot name a private or protected nested class. Each relay
    /// host declares an internal method that calls the same method of the next host, and the last one calls the
    /// warmup method of the class; a nested type is accessible within its container whatever its accessibility.
    /// </remarks>
    public static EquatableArray<string> CreateWarmupRelayHosts(INamedTypeSymbol type, Compilation compilation)
    {
        if (compilation.IsSymbolAccessibleWithin(type, compilation.Assembly))
        {
            return default;
        }

        var hosts = ImmutableArray.CreateBuilder<string>();
        for (var outer = type.ContainingType; outer != null; outer = outer.ContainingType)
        {
            hosts.Insert(0, outer.GetFullyQualifiedCsharpName());
            if (compilation.IsSymbolAccessibleWithin(outer, compilation.Assembly))
            {
                return hosts.ToImmutable().AsEquatableArray();
            }
        }

        // No container is accessible (a file-local type): the module context names the class as before
        return default;
    }

    /// <summary>
    /// The partial declarations of the types that contain <paramref name="type"/>, outermost first
    /// (<c>partial class Outer&lt;T&gt;</c>), or empty for a type that is not nested.
    /// </summary>
    public static EquatableArray<string> CreateOuterDeclarations(INamedTypeSymbol type)
    {
        if (type.ContainingType == null)
        {
            return default;
        }

        var declarations = ImmutableArray.CreateBuilder<string>();
        for (var outer = type.ContainingType; outer != null; outer = outer.ContainingType)
        {
            declarations.Insert(0, $"partial {GetKeyword(outer)} {outer.GetDeclaredTypeName()}");
        }

        return declarations.ToImmutable().AsEquatableArray();
    }

    /// <summary>
    /// The keyword that declares <paramref name="type"/> after <c>partial</c>: <c>class</c>, <c>struct</c>,
    /// <c>interface</c>, <c>record</c> or <c>record struct</c>.
    /// </summary>
    public static string GetKeyword(INamedTypeSymbol type) => type.IsRecord
        ? type.TypeKind == TypeKind.Struct ? "record struct" : "record"
        : type.TypeKind == TypeKind.Struct ? "struct" : type.TypeKind == TypeKind.Interface ? "interface" : "class";
}

/// <summary>The native shadow struct and the marshaling methods of one user struct, as the wrapper declares them.</summary>
/// <param name="AotStructPath">The <c>AotContexts</c> path, which identifies the struct within a class.</param>
/// <param name="CudaText">The CUDA declaration and the <c>Marshal_</c> / <c>Unmarshal_</c> methods.</param>
/// <param name="CpuText">The CPU declaration and the <c>Marshal_</c> / <c>Unmarshal_</c> methods.</param>
/// <param name="Accessibility">
/// The widest accessibility of the <c>AotContexts</c> class under which its members for the struct, all public, name
/// only types at least as accessible as themselves (<see cref="GeneratedMemberAccessibility"/>).
/// </param>
internal readonly record struct StructHostSource(
    string AotStructPath,
    string CudaText,
    string CpuText,
    Accessibility Accessibility = Accessibility.Public)
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
/// <param name="ErrorSites">
/// The source locations of the run-time error sites of the kernel, which the class reports by site ID.
/// </param>
/// <param name="PointerLauncherAccessibility">
/// The accessibility of <c>Launch{Name}Ptr</c> when it is not private for an overload: the launcher's, narrowed to the
/// <c>AotContexts</c> class's when a parameter is its shadow struct.
/// </param>
internal readonly record struct KernelEmission(
    KernelHostModel Host,
    string MemberKey,
    bool IsOverloaded,
    bool IsIndistinguishable,
    string CudaSource,
    string CppSource,
    string ManagedCSharpSource,
    string InternalError,
    EquatableArray<ErrorSiteEntry> ErrorSites = default,
    Accessibility PointerLauncherAccessibility = Accessibility.Public)
{
    public string GetSource(bool isCuda) => isCuda ? CudaSource : CppSource;

    /// <summary>
    /// Whether the kernel has a CUDA warmup method for the module context to call: it has launchers (GSH0059, GSH0106
    /// and GSH0108 leave them out).
    /// </summary>
    public bool HasWarmup => !IsIndistinguishable && !Host.HasReservedLaunchParameterName &&
                             Host.LauncherUnavailableReason is null;
}

/// <summary>The kernels of one class, which the generator emits as one wrapper source.</summary>
internal readonly record struct KernelClassModel(
    HostClassHeader Class,
    EquatableArray<KernelEmission> Kernels,
    EquatableArray<StructHostSource> Structs,
    EquatableArray<ErrorSiteEntry> ErrorSites = default)
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

        NarrowPointerLaunchers(emissions, GetAotContextsAccessibility(structs));
        ErrorSiteTable.TryBuildModuleSites(rawClassSites, out var classErrorSites, out _, out _);
        return new KernelClassModel(
            header,
            emissions.MoveToImmutable().AsEquatableArray(),
            structs.ToImmutable().AsEquatableArray(),
            classErrorSites.AsEquatableArray());
    }

    // A pointer launcher that takes a shadow struct names the AotContexts class, which the structs of every kernel of
    // the class narrow
    private static void NarrowPointerLaunchers(ImmutableArray<KernelEmission>.Builder emissions,
        Accessibility aotContexts)
    {
        for (var i = 0; i < emissions.Count; i++)
        {
            var emission = emissions[i];
            var namesShadow = emission.Host.Parameters
                .AsStackEnumerable()
                .Any(static p => PointerLaunchPlan.Classify(p) == PointerArgumentKind.ShadowStruct);
            emissions[i] = emission with
            {
                PointerLauncherAccessibility = namesShadow
                    ? GeneratedMemberAccessibility.Narrowest(emission.Host.LauncherAccessibility, aotContexts)
                    : emission.Host.LauncherAccessibility
            };
        }
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

/// <summary>
/// A kernel whose function pointer the CUDA module context warms up, as the call the module context writes.
/// </summary>
/// <param name="Type">
/// The <c>global::</c>-qualified type that declares the method: the kernel's class, or the outermost relay host of a
/// class the module context cannot name (<see cref="HostClassHeader.WarmupRelayHosts"/>).
/// </param>
/// <param name="Method">
/// <c>WarmupFunctionPointer{key}</c>, or the relay (<see cref="KernelHostModel.WarmupRelayName"/>).
/// </param>
internal readonly record struct KernelWarmup(string Type, string Method)
{
    public static KernelWarmup Create(in HostClassHeader header, in KernelEmission kernel) =>
        header.WarmupRelayHosts.IsDefaultOrEmpty
            ? new KernelWarmup(header.FullyQualifiedName, KernelHostNames.GetWarmupMethodName(kernel.MemberKey))
            : new KernelWarmup(header.WarmupRelayHosts[0], kernel.Host.WarmupRelayName);
}

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
            if (emission.HasWarmup)
            {
                warmups.Add(KernelWarmup.Create(kernel.Class, emission));
            }
        }

        return new KernelModuleModel(classModels.AsEquatableArray(), warmups.ToImmutable().AsEquatableArray());
    }
}

/// <summary>The managed state machines of the device functions of one class that may suspend.</summary>
/// <param name="Class">The class that the state machines go into.</param>
/// <param name="Sources">The state machines, in declaration order.</param>
internal readonly record struct DeviceStateMachineGroup(HostClassHeader Class, EquatableArray<string> Sources);
