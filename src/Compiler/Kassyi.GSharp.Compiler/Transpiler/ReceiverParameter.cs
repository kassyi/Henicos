namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// The explicit receiver parameter (<c>&lt;const &gt;&lt;struct&gt;* __gsharp_this</c>) of an emitted non-suspending,
/// non-generic struct instance <c>[GpuDeviceFunction]</c> method, or whether an instance device function stays
/// unsupported (<c>GSH0066</c>).
/// </summary>
internal readonly record struct ReceiverParameter(
    INamedTypeSymbol? Type,
    bool IsConst,
    bool FailsUnsupported)
{
    /// <summary>Whether the method signature emits the explicit <c>__gsharp_this</c> receiver parameter.</summary>
    public bool HasParameter => Type != null;

    /// <summary>Plans the receiver parameter for <paramref name="plan"/>.</summary>
    public static ReceiverParameter Plan(in MethodEmissionPlan plan) =>
        Plan(plan.Symbol, plan.Kind, plan.SemanticModel.Compilation);

    /// <summary>Plans the receiver parameter for <paramref name="method"/> under <paramref name="kind"/>.</summary>
    public static ReceiverParameter Plan(IMethodSymbol? method, MethodEmissionKind kind, Compilation? compilation = null)
    {
        if (kind is MethodEmissionKind.Kernel or MethodEmissionKind.Iterator ||
            method == null ||
            method.IsStatic ||
            !method.HasAttribute(KnownAttributes.GpuDeviceFunction))
        {
            return default;
        }

        if (GpuCodeConstraints.GetInstanceDeviceFunctionProblem(method, compilation) is not null ||
            kind == MethodEmissionKind.SuspendingDeviceFunction)
        {
            return new ReceiverParameter(null, false, FailsUnsupported: true);
        }

        return new ReceiverParameter(method.ContainingType, method.IsReadOnly, FailsUnsupported: false);
    }
}
