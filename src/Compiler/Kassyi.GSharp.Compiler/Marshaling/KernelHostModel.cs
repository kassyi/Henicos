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

    /// <summary>The parameter types of <c>Launch{Name}</c> as C# compares overloads (<see cref="LauncherSignatures"/>).</summary>
    public string ManagedParameterTypes { get; init; }

    /// <summary>The parameter types of the untyped <c>Launch{Name}Ptr</c> as C# compares overloads.</summary>
    public string PointerParameterTypes { get; init; }

    /// <summary>The parameter types of the typed <c>Launch{Name}Ptr</c> of an overloaded kernel as C# compares overloads.</summary>
    public string TypedPointerParameterTypes { get; init; }

    /// <summary>The parameter types of the kernel's own method as C# compares overloads.</summary>
    public string OwnParameterTypes { get; init; }

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
            ManagedParameterTypes = LauncherSignatures.FormatParameterTypes(parameters, LauncherKind.Managed),
            PointerParameterTypes = LauncherSignatures.FormatParameterTypes(parameters, LauncherKind.Pointer),
            TypedPointerParameterTypes = LauncherSignatures.FormatParameterTypes(parameters, LauncherKind.TypedPointer),
            OwnParameterTypes = LauncherSignatures.FormatOwnParameterTypes(parameters),
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

/// <summary>The managed state machines of the device functions of one class that may suspend.</summary>
/// <param name="Class">The class that the state machines go into.</param>
/// <param name="Sources">The state machines, in declaration order.</param>
internal readonly record struct DeviceStateMachineGroup(HostClassHeader Class, EquatableArray<string> Sources);
