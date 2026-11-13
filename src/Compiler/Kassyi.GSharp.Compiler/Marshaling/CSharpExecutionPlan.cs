namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// What a launcher does under <c>KernelExecution.CSharp</c> of the runtime: call the kernel's own method once per GPU
/// thread through <c>CSharpKernelRunner.Run</c>, with the launcher's arguments, or throw when the kernel cannot run one
/// thread at a time (<see cref="CannotRunReason"/>). <see cref="CSharpExecutionEmitter"/> writes it.
/// </summary>
internal readonly record struct CSharpExecutionPlan
{
    /// <summary>The kernel in the exception messages, as <c>Type.Method</c>.</summary>
    public string MessageName { get; init; }

    /// <summary>
    /// Why the kernel cannot run as C#, as the end of a sentence, or <see langword="null"/> when the launcher runs it.
    /// </summary>
    public string? CannotRunReason { get; init; }

    /// <summary>The kernel method, qualified by its class (<c>global::N.C.AddKernel</c>).</summary>
    public string Method { get; init; }

    /// <summary>The arguments of the kernel call, in parameter order; empty when the kernel does not run.</summary>
    public ImmutableArray<CSharpArgument> Arguments { get; init; }

    /// <summary>
    /// The fully qualified type of the value the kernel returns, which each GPU thread writes into its auto result
    /// slot, or <see langword="null"/> for a <see langword="void"/> kernel.
    /// </summary>
    public string? ReturnTypeName { get; init; }

    /// <summary>Whether the kernel returns a value.</summary>
    public bool ReturnsValue => ReturnTypeName is not null;

    /// <summary>The plan of the launcher of <paramref name="kernel"/>.</summary>
    /// <param name="kernel">The kernel.</param>
    /// <param name="isDevicePtr">Whether the launcher takes device pointers (<c>Launch{Kernel}Ptr</c>).</param>
    public static CSharpExecutionPlan Create(in KernelHostModel kernel, bool isDevicePtr)
    {
        var plan = new CSharpExecutionPlan
        {
            MessageName = kernel.MessageName,
            Method = kernel.ClassQualifiedName + "." + kernel.Name.ToCSharpIdentifier(),
            Arguments = ImmutableArray<CSharpArgument>.Empty,
            ReturnTypeName = kernel.ReturnTypeName
        };

        var reason = GetCannotRunReason(kernel, isDevicePtr);
        if (reason is null && TryPlanArguments(kernel, out var arguments, out reason))
        {
            return plan with { Arguments = arguments };
        }

        return plan with { CannotRunReason = reason };
    }

    // Why the launcher cannot run the kernel as C#, before its arguments are looked at
    private static string? GetCannotRunReason(in KernelHostModel kernel, bool isDevicePtr) => true switch
    {
        // The pointer launcher takes the arguments in the device's form: device pointers, array proxies, shadows
        _ when isDevicePtr => $"{KernelHostNames.GetTypedPointerLaunchName(kernel)} takes the arguments in the form " +
                              "the device uses, and the C# execution runs only through " +
                              KernelHostNames.GetLaunchName(kernel),
        _ when !kernel.IsStatic => "it is an instance method, and the launcher has no object for it",
        _ when kernel.CSharpExecutionObstacle is { } obstacle => $"it reaches {obstacle}",
        _ => null
    };

    // The arguments of the kernel call, or false with the reason when one of them cannot reach the GPU threads
    private static bool TryPlanArguments(in KernelHostModel kernel, out ImmutableArray<CSharpArgument> arguments,
        [NotNullWhen(false)] out string? reason)
    {
        var builder = ImmutableArray.CreateBuilder<CSharpArgument>(kernel.Parameters.Length);
        foreach (var parameter in kernel.Parameters)
        {
            if (PlanArgument(parameter) is not { } argument)
            {
                arguments = default;
                reason = $"its parameter '{parameter.Identifier}' is a ref struct that the GPU threads cannot share";
                return false;
            }

            builder.Add(argument);
        }

        arguments = builder.MoveToImmutable();
        reason = null;
        return true;
    }

    // [WHY] The lambda captures locals of the branch, never the launcher's parameters: a captured parameter would live
    // in a closure for the whole launcher, whose other paths take its address (CS1686). A ref or out argument goes
    // through its pinned pointer, which keeps it the caller's variable; a span, which no lambda can capture, goes
    // through its pinned pointer and length; any other ref struct cannot run (null)
    private static CSharpArgument? PlanArgument(in KernelParameterShape parameter)
    {
        var local = KernelHostNames.GetLocal(LauncherLocal.CSharpArgument, parameter);
        return parameter switch
        {
            { IsRefLike: true, IsByRef: false, Container: ArrayContainerKind.Span } =>
                PlanSpan(parameter, CSharpArgumentKind.Span, local),
            { IsRefLike: true, IsByRef: false, Container: ArrayContainerKind.ReadOnlySpan } =>
                PlanSpan(parameter, CSharpArgumentKind.ReadOnlySpan, local),
            { IsRefLike: true } => null,
            { RefKind: RefKind.Ref } => PlanReference(parameter, CSharpArgumentKind.Ref, local),
            { RefKind: RefKind.Out } => PlanReference(parameter, CSharpArgumentKind.Out, local),
            { IsInReference: true } => PlanReference(parameter, CSharpArgumentKind.InReference, local),
            { RefKind: RefKind.In or RefKind.RefReadOnlyParameter } =>
                new CSharpArgument(parameter.Identifier, CSharpArgumentKind.In, parameter.DeclaredTypeName, local),
            _ => new CSharpArgument(parameter.Identifier, CSharpArgumentKind.Value, parameter.DeclaredTypeName, local)
        };
    }

    // The pointer type has no nullable annotation (int[]?* is no type)
    private static CSharpArgument PlanReference(in KernelParameterShape parameter, CSharpArgumentKind kind,
        string local) =>
        new(
            parameter.Identifier,
            kind,
            parameter.TypeName,
            local,
            KernelHostNames.GetLocal(LauncherLocal.CSharpPinned, parameter)
        );

    private static CSharpArgument PlanSpan(in KernelParameterShape parameter, CSharpArgumentKind kind, string local) =>
        new(
            parameter.Identifier,
            kind,
            ConvertedArrayEmitter.GetElementTypeName(parameter),
            local,
            KernelHostNames.GetLocal(LauncherLocal.CSharpPinned, parameter),
            KernelHostNames.GetLocal(LauncherLocal.CSharpLength, parameter)
        );
}
