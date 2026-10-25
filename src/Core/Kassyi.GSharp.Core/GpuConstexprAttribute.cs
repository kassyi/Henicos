namespace Kassyi.GSharp.Core;

/// <summary>
/// Marks a kernel parameter as a compile-time constant with a list of candidate values.
/// </summary>
/// <remarks>
/// The candidate values are not used yet: the parameter is passed as an ordinary runtime argument. A later release
/// emits one entry point per value, in which the parameter is a C++ <c>constexpr</c>, and the launcher selects the
/// entry point from the argument.
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter)]
// ReSharper disable UnusedMember.Global -- Consumed by GSharp Transpiler via Roslyn symbol metadata
// ReSharper disable UnusedParameter.Local -- Values are read from AttributeData.ConstructorArguments
#pragma warning disable IDE0060 // Remove unused parameter
public sealed class GpuConstexprAttribute : Attribute
{
    /// <summary>For <c>bool</c> and enum parameters: every value (<c>false</c> / <c>true</c>, every declared member).</summary>
    public GpuConstexprAttribute()
    {
    }

    /// <summary>The integer values to compile.</summary>
    /// <param name="values">The compile-time integer constant values to specialize the kernel for.</param>
    public GpuConstexprAttribute(params long[] values)
    {
    }

    /// <summary>The floating-point values to compile.</summary>
    /// <param name="values">The compile-time floating-point constant values to specialize the kernel for.</param>
    public GpuConstexprAttribute(params double[] values)
    {
    }
}
