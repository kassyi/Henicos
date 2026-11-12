namespace Kassyi.GSharp.Compiler.NativeApi;

/// <summary>A kernel argument of a module launcher, in the order the kernel declares it.</summary>
/// <param name="Name">The C# parameter name.</param>
/// <param name="Kind">How the launcher passes the argument.</param>
/// <param name="TypeName">
/// The C# type of the value whose address the kernel receives: the parameter type, the element type of an array, or
/// the referenced type of a <c>ref</c>, <c>out</c> or <c>in</c> parameter.
/// </param>
/// <param name="CopyIn">
/// Whether an array or a value passed by reference is copied to the device before the launch; a value passed by
/// reference that is not (<c>out</c>) is set to <see langword="default"/> first, and that is copied.
/// </param>
/// <param name="CopyOut">
/// Whether an array or a value passed by reference is copied back to the host after the launch.
/// </param>
/// <param name="AllowsNull">
/// Whether an array is declared nullable (<c>T[]?</c>, <c>T[,]?</c>): null then reaches the kernel as a null pointer.
/// Null for another array throws <see cref="ArgumentNullException"/> before the launch.
/// </param>
internal readonly record struct ModuleKernelArgument(
    string Name,
    ModuleKernelParameterKind Kind,
    string TypeName,
    bool CopyIn,
    bool CopyOut,
    bool AllowsNull = false)
{
    /// <summary>Whether the kernel receives the device pointer of a buffer the launcher allocates.</summary>
    public bool IsStaged => Kind is ModuleKernelParameterKind.Array or ModuleKernelParameterKind.MultidimensionalArray
        or ModuleKernelParameterKind.ByReference;

    /// <summary>Whether the parameter is an array, one-dimensional or multidimensional.</summary>
    public bool IsArray => Kind is ModuleKernelParameterKind.Array or ModuleKernelParameterKind.MultidimensionalArray;
}

/// <summary>
/// The parameters of a module launcher that set the launch instead of passing a kernel argument; each is the parameter
/// name, or <see langword="null"/> when the launcher uses the default.
/// </summary>
internal readonly record struct ModuleLaunchConfiguration(
    string? GridDimX,
    string? GridDimY,
    string? GridDimZ,
    string? BlockDimX,
    string? BlockDimY,
    string? BlockDimZ,
    string? SharedMemBytes,
    string? Stream)
{
    public bool HasDimensions =>
        GridDimX is not null || GridDimY is not null || GridDimZ is not null ||
        BlockDimX is not null || BlockDimY is not null || BlockDimZ is not null;

    /// <summary>
    /// Returns this configuration with <paramref name="setting"/> taken from parameter <paramref name="name"/>.
    /// </summary>
    public ModuleLaunchConfiguration With(ModuleLaunchSetting setting, string name) => setting switch
    {
        ModuleLaunchSetting.GridDimX => this with { GridDimX = name },
        ModuleLaunchSetting.GridDimY => this with { GridDimY = name },
        ModuleLaunchSetting.GridDimZ => this with { GridDimZ = name },
        ModuleLaunchSetting.BlockDimX => this with { BlockDimX = name },
        ModuleLaunchSetting.BlockDimY => this with { BlockDimY = name },
        ModuleLaunchSetting.BlockDimZ => this with { BlockDimZ = name },
        ModuleLaunchSetting.SharedMemBytes => this with { SharedMemBytes = name },
        ModuleLaunchSetting.Stream => this with { Stream = name },
        _ => throw new ArgumentOutOfRangeException(nameof(setting), setting, "Not a launch setting."),
    };
}

/// <summary>A parameter that a module launcher cannot pass, with the reason.</summary>
/// <param name="ParameterOrdinal">The position of the parameter in the declaration.</param>
/// <param name="ParameterName">The parameter name.</param>
/// <param name="TypeName">The parameter type as a diagnostic shows it.</param>
/// <param name="Reason">Why the launcher cannot pass it, as the end of a diagnostic sentence.</param>
internal readonly record struct ModuleKernelProblem(int ParameterOrdinal, string ParameterName, string TypeName,
    string Reason);

/// <summary>
/// Everything a module launcher body depends on, decided before any text is written: the kernel arguments and the
/// launch settings. <see cref="ModuleKernelLaunchEmitter"/> only reads it.
/// </summary>
/// <param name="Arguments">The kernel arguments in declaration order.</param>
/// <param name="Launch">The parameters that set the launch.</param>
/// <param name="Problems">
/// The parameters the launcher cannot pass; when there is one, the launcher throws instead of launching.
/// </param>
internal readonly record struct ModuleKernelLaunchPlan(
    EquatableArray<ModuleKernelArgument> Arguments,
    ModuleLaunchConfiguration Launch,
    EquatableArray<ModuleKernelProblem> Problems)
{
    public bool CanLaunch => Problems.IsDefaultOrEmpty;
}

/// <summary>Builds a <see cref="ModuleKernelLaunchPlan"/> one parameter at a time, in declaration order.</summary>
internal sealed class ModuleKernelLaunchPlanBuilder(int parameterCount)
{
    private readonly ImmutableArray<ModuleKernelArgument>.Builder _arguments =
        ImmutableArray.CreateBuilder<ModuleKernelArgument>(parameterCount);

    private readonly ImmutableArray<ModuleKernelProblem>.Builder _problems =
        ImmutableArray.CreateBuilder<ModuleKernelProblem>();

    private ModuleLaunchConfiguration _launch;

    public void AddArgument(ModuleKernelArgument argument) => _arguments.Add(argument);

    public void AddLaunchSetting(ModuleLaunchSetting setting, string name) => _launch = _launch.With(setting, name);

    public void AddProblem(int ordinal, string name, string typeName, string reason) =>
        _problems.Add(new ModuleKernelProblem(ordinal, name, typeName, reason));

    public ModuleKernelLaunchPlan ToPlan() =>
        new(_arguments.ToImmutable().AsEquatableArray(), _launch, _problems.ToImmutable().AsEquatableArray());
}
