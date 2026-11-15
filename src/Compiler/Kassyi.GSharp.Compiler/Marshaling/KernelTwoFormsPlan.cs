using System.Collections.Immutable;
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Helpers;
using Kassyi.GSharp.Compiler.Transpiler;
using Microsoft.CodeAnalysis;

namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// Pre-computes and holds the plan for two-form CUDA kernel emission (by-value and by-address forms),
/// tracking which kernel parameters are passed by address in the by-address form.
/// </summary>
internal readonly record struct KernelTwoFormsPlan
{
    /// <summary>Whether this kernel must be emitted in both by-value and by-address forms on CUDA.</summary>
    public bool HasTwoForms { get; }

    /// <summary>The C++ entry point name for the by-value kernel form.</summary>
    public string ByValueKernelName { get; }

    /// <summary>The C++ entry point name for the by-address kernel form.</summary>
    public string ByAddressKernelName { get; }

    /// <summary>Whether each parameter at its ordinal index is passed by address in the by-address form.</summary>
    public EquatableArray<bool> ParameterPassedByAddress { get; }

    public KernelTwoFormsPlan(
        bool hasTwoForms,
        string byValueKernelName,
        string byAddressKernelName,
        EquatableArray<bool> parameterPassedByAddress)
    {
        HasTwoForms = hasTwoForms;
        ByValueKernelName = byValueKernelName;
        ByAddressKernelName = byAddressKernelName;
        ParameterPassedByAddress = parameterPassedByAddress;
    }

    /// <summary>An empty plan representing a non-kernel or single-form kernel.</summary>
    public static KernelTwoFormsPlan Empty { get; } =
        new(false, string.Empty, string.Empty, default);

    /// <summary>Returns whether the parameter at <paramref name="ordinal"/> is passed by address.</summary>
    public bool IsPassedByAddress(int ordinal) =>
        ordinal >= 0 && ordinal < ParameterPassedByAddress.Length && ParameterPassedByAddress[ordinal];

    /// <summary>Returns whether <paramref name="parameter"/> is passed by address.</summary>
    public bool IsPassedByAddress(IParameterSymbol? parameter) =>
        parameter != null && IsPassedByAddress(parameter.Ordinal);

    /// <summary>
    /// Computes the two-forms plan from an <see cref="IMethodSymbol"/>, evaluating the predicate once per parameter.
    /// </summary>
    public static KernelTwoFormsPlan Create(IMethodSymbol kernel)
    {
        var kernelName = GlobalCppNames.GetKernelName(kernel);
        var parameters = kernel.Parameters;
        var passedByAddress = new bool[parameters.Length];
        var hasTwoForms = false;

        for (var i = 0; i < parameters.Length; i++)
        {
            var isByAddress = parameters[i].IsKernelInPassedByAddress();
            passedByAddress[i] = isByAddress;
            if (isByAddress)
            {
                hasTwoForms = true;
            }
        }

        var byAddressName = hasTwoForms
            ? kernelName + GSharpInternalNames.Kernel.InByAddressSuffix
            : kernelName;

        return new KernelTwoFormsPlan(
            hasTwoForms,
            kernelName,
            byAddressName,
            passedByAddress.ToImmutableArray().AsEquatableArray());
    }

    /// <summary>
    /// Computes the two-forms plan from pre-classified <see cref="KernelParameterShape"/> instances.
    /// </summary>
    public static KernelTwoFormsPlan Create(string kernelName, EquatableArray<KernelParameterShape> parameters)
    {
        var passedByAddress = new bool[parameters.Length];
        var hasTwoForms = false;

        for (var i = 0; i < parameters.Length; i++)
        {
            var isByAddress = parameters[i].IsInReference;
            passedByAddress[i] = isByAddress;
            if (isByAddress)
            {
                hasTwoForms = true;
            }
        }

        var byAddressName = hasTwoForms
            ? kernelName + GSharpInternalNames.Kernel.InByAddressSuffix
            : kernelName;

        return new KernelTwoFormsPlan(
            hasTwoForms,
            kernelName,
            byAddressName,
            passedByAddress.ToImmutableArray().AsEquatableArray());
    }
}
