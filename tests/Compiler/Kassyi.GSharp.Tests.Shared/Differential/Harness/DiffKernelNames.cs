#nullable enable
using System;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Builds the kernel names that <see cref="DifferentialCase"/> reports, from the kernels class and the kernel.
/// </summary>
internal static class DiffKernelNames
{
    private const string LaunchPrefix = "Launch";
    private const string PointerSuffix = "Ptr";

    /// <summary>
    /// Returns <c>{kernels class}.{kernel}</c>, or <c>{kernels class}.{kernel}.{suffix}</c>
    /// for a variant of the kernel.
    /// </summary>
    internal static string Of(Type kernels, string kernel, string? suffix = null) =>
        suffix is null ? $"{kernels.Name}.{kernel}" : $"{kernels.Name}.{kernel}.{suffix}";

    /// <summary>
    /// Derives <c>{Class}.{Kernel}{suffix}</c> from a <c>[CallerArgumentExpression]</c> string of the form
    /// <c>&lt;SimpleIdentifier&gt;.Launch&lt;KernelName&gt;</c>.
    /// </summary>
    /// <param name="launchExpression">The captured launch method group expression.</param>
    /// <param name="suffix">
    /// Optional suffix appended verbatim (e.g. <c>"Access"</c>, <c>"Call"</c>, <c>"WithoutData"</c>,
    /// <c>"WithData"</c>, <c>".ReversedBounds"</c>, <c>"_Null"</c>, or <c>"_WithValue"</c>).
    /// </param>
    /// <param name="paramName">The caller parameter name used in <see cref="ArgumentException"/>.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="launchExpression"/> is null/whitespace, does not have shape
    /// <c>&lt;SimpleIdentifier&gt;.Launch&lt;KernelName&gt;</c>, uses a <c>Launch...Ptr</c> wrapper, or
    /// <paramref name="suffix"/> is empty/whitespace/a bare dot.
    /// </exception>
    internal static string FromLaunchExpression(
        string launchExpression,
        string? suffix = null,
        string? paramName = null)
    {
        string effectiveParamName = paramName ?? nameof(launchExpression);
        ArgumentException.ThrowIfNullOrWhiteSpace(launchExpression, effectiveParamName);

        if (suffix is not null)
        {
            ReadOnlySpan<char> suffixSpan = suffix.AsSpan().Trim();
            if (suffix.Length == 0 ||
                !suffixSpan.SequenceEqual(suffix.AsSpan()) ||
                suffixSpan.SequenceEqual(".".AsSpan()))
            {
                throw new ArgumentException(
                    "Kernel name suffix must not be empty, whitespace, or a bare '.'.",
                    nameof(suffix));
            }
        }

        ReadOnlySpan<char> expr = launchExpression.AsSpan().Trim();
        int dotIndex = expr.LastIndexOf('.');
        if (dotIndex <= 0 || dotIndex >= expr.Length - 1)
        {
            throw new ArgumentException(
                $"Expected '<KernelsClass>.Launch<Kernel>' expression, got '{launchExpression}'.",
                effectiveParamName);
        }

        ReadOnlySpan<char> classPart = expr[..dotIndex].TrimEnd();
        ReadOnlySpan<char> methodPart = expr[(dotIndex + 1)..].TrimStart();

        if (!IsSimpleIdentifier(classPart) || !IsSimpleIdentifier(methodPart))
        {
            throw new ArgumentException(
                $"Expected '<SimpleIdentifier>.Launch<Kernel>' expression, got '{launchExpression}'.",
                effectiveParamName);
        }

        if (!methodPart.StartsWith(LaunchPrefix.AsSpan(), StringComparison.Ordinal) ||
            methodPart.Length <= LaunchPrefix.Length)
        {
            throw new ArgumentException(
                $"Expected launch method starting with '{LaunchPrefix}', got '{launchExpression}'.",
                effectiveParamName);
        }

        if (methodPart.EndsWith(PointerSuffix.AsSpan(), StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Pointer wrapper '{launchExpression}' cannot derive a kernel case name.",
                effectiveParamName);
        }

        ReadOnlySpan<char> kernelPart = methodPart[LaunchPrefix.Length..];
        return suffix is null
            ? $"{classPart}.{kernelPart}"
            : $"{classPart}.{kernelPart}{suffix}";
    }

    private static bool IsSimpleIdentifier(ReadOnlySpan<char> span)
    {
        if (span.IsEmpty || !(char.IsLetter(span[0]) || span[0] == '_'))
        {
            return false;
        }

        for (int i = 1; i < span.Length; i++)
        {
            char c = span[i];
            if (!(char.IsLetterOrDigit(c) || c == '_'))
            {
                return false;
            }
        }

        return true;
    }
}
