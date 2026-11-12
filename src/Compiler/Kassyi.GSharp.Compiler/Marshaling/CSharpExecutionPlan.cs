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

    /// <summary>Whether the kernel returns a value, which each GPU thread writes into its auto result slot.</summary>
    public bool ReturnsValue { get; init; }

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
            ReturnsValue = kernel.ReturnsValue
        };

        if (isDevicePtr)
        {
            return plan with
            {
                CannotRunReason = "the pointer launcher takes device memory, which the C# body cannot read; pass the " +
                                  $"host values to {KernelHostNames.GetLaunchName(kernel)}"
            };
        }

        if (!kernel.IsStatic)
        {
            return plan with { CannotRunReason = "it is an instance method, and the launcher has no object for it" };
        }

        if (kernel.CSharpExecutionObstacle is { } obstacle)
        {
            return plan with { CannotRunReason = $"it reaches {obstacle}" };
        }

        var arguments = ImmutableArray.CreateBuilder<CSharpArgument>(kernel.Parameters.Length);
        foreach (var parameter in kernel.Parameters)
        {
            if (PlanArgument(parameter) is not { } argument)
            {
                return plan with
                {
                    CannotRunReason = $"its parameter '{parameter.Identifier}' is a ref struct that the GPU threads " +
                                      "cannot share"
                };
            }

            arguments.Add(argument);
        }

        return plan with { Arguments = arguments.MoveToImmutable() };
    }

    // [WHY] The lambda captures locals of the branch, never the launcher's parameters: a captured parameter would live
    // in a closure for the whole launcher, whose other paths take its address (CS1686). A ref or out argument is copied
    // back after the run; a span, which no lambda can capture, goes through its pinned pointer and length; any other
    // ref struct cannot run (null)
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
            { RefKind: RefKind.Ref } =>
                new CSharpArgument(parameter.Identifier, CSharpArgumentKind.Ref, parameter.DeclaredTypeName, local),
            { RefKind: RefKind.Out } =>
                new CSharpArgument(parameter.Identifier, CSharpArgumentKind.Out, parameter.DeclaredTypeName, local),
            { RefKind: RefKind.In or RefKind.RefReadOnlyParameter } =>
                new CSharpArgument(parameter.Identifier, CSharpArgumentKind.In, parameter.DeclaredTypeName, local),
            _ => new CSharpArgument(parameter.Identifier, CSharpArgumentKind.Value, parameter.DeclaredTypeName, local)
        };
    }

    private static CSharpArgument PlanSpan(in KernelParameterShape parameter, CSharpArgumentKind kind, string local) =>
        new(
            parameter.Identifier,
            kind,
            parameter.ElementTypeName,
            local,
            KernelHostNames.GetLocal(LauncherLocal.CSharpPinned, parameter),
            KernelHostNames.GetLocal(LauncherLocal.CSharpLength, parameter)
        );
}
