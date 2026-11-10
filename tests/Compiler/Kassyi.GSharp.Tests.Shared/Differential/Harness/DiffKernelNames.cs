#nullable enable
using System;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Builds the kernel names that <see cref="DifferentialCase"/> reports, from the kernels class and the kernel.
/// </summary>
internal static class DiffKernelNames
{
    /// <summary>
    /// Returns <c>{kernels class}.{kernel}</c>, or <c>{kernels class}.{kernel}.{suffix}</c> for a variant of the kernel.
    /// </summary>
    internal static string Of(Type kernels, string kernel, string? suffix = null) =>
        suffix is null ? $"{kernels.Name}.{kernel}" : $"{kernels.Name}.{kernel}.{suffix}";
}
