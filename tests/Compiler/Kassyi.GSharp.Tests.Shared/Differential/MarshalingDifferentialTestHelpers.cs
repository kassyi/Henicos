#nullable enable
using System;
using System.Numerics;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Shared test helpers, comparers, cloning delegates, and naming utilities for marshaling and aliasing differential tests.
/// </summary>
internal static class MarshalingDifferentialTestHelpers
{
    /// <summary>
    /// Builds the fully qualified kernel case name for <see cref="DifferentialCase"/>.
    /// </summary>
    internal static string KernelName(string kernelMemberName) =>
        $"{nameof(MarshalingAliasingDifferentialKernels)}.{kernelMemberName}";

    /// <summary>
    /// Builds the fully qualified kernel case name with a variant suffix for <see cref="DifferentialCase"/>.
    /// </summary>
    internal static string KernelName(string kernelMemberName, string suffix) =>
        $"{nameof(MarshalingAliasingDifferentialKernels)}.{kernelMemberName}.{suffix}";

    /// <summary>
    /// Element comparer for <see cref="DiffVec2Padded"/> with bitwise field matching.
    /// </summary>
    internal static readonly IElementComparer<DiffVec2Padded> Vec2PaddedComparer =
        new StructElementComparer<DiffVec2Padded>(static (ctx, o, a) =>
        {
            ctx.Field(nameof(DiffVec2Padded.A), o.A, a.A, OutputPolicy.Bitwise);
            ctx.Field("V.X", o.V.X, a.V.X, OutputPolicy.Bitwise);
            ctx.Field("V.Y", o.V.Y, a.V.Y, OutputPolicy.Bitwise);
        });

    /// <summary>
    /// Deep clone delegate for <see cref="DiffDeepBag"/>.
    /// </summary>
    internal static DiffDeepBag CloneDeepBag(DiffDeepBag src, DiffArrayCloneScope scope)
        => new()
        {
            Data = scope.CloneArray(src.Data) ?? Array.Empty<int>(),
            Tag = src.Tag,
        };

    /// <summary>
    /// Structural comparison delegate for <see cref="DiffDeepBag"/>.
    /// </summary>
    internal static void CompareDeepBag(StructFieldCheckContext ctx, DiffDeepBag o, DiffDeepBag a)
    {
        ctx.Field("Tag", o.Tag, a.Tag);
        ctx.Field("Data.Length", o.Data.Length, a.Data.Length);
        int len = Math.Min(o.Data.Length, a.Data.Length);
        for (int i = 0; i < len; i++)
        {
            ctx.Field($"Data[{i}]", o.Data[i], a.Data[i]);
        }
    }

    /// <summary>
    /// Deep clone delegate for <see cref="DiffNestedDeep"/>.
    /// </summary>
    internal static DiffNestedDeep CloneNestedDeep(DiffNestedDeep src, DiffArrayCloneScope scope)
        => new()
        {
            Indices = scope.CloneArray(src.Indices) ?? Array.Empty<int>(),
            Inner = new DiffInnerBag
            {
                Weights = scope.CloneArray(src.Inner.Weights) ?? Array.Empty<float>(),
                Bias = src.Inner.Bias,
            },
            Scale = src.Scale,
        };

    /// <summary>
    /// Structural comparison delegate for <see cref="DiffNestedDeep"/>.
    /// </summary>
    internal static void CompareNestedDeep(StructFieldCheckContext ctx, DiffNestedDeep o, DiffNestedDeep a)
    {
        ctx.Field("Scale", o.Scale, a.Scale);
        ctx.Field("Inner.Bias", o.Inner.Bias, a.Inner.Bias);
        ctx.Field("Indices.Length", o.Indices.Length, a.Indices.Length);
        int idxLen = Math.Min(o.Indices.Length, a.Indices.Length);
        for (int i = 0; i < idxLen; i++)
        {
            ctx.Field($"Indices[{i}]", o.Indices[i], a.Indices[i]);
        }

        ctx.Field("Inner.Weights.Length", o.Inner.Weights.Length, a.Inner.Weights.Length);
        int wLen = Math.Min(o.Inner.Weights.Length, a.Inner.Weights.Length);
        for (int i = 0; i < wLen; i++)
        {
            ctx.Field($"Inner.Weights[{i}]", o.Inner.Weights[i], a.Inner.Weights[i], OutputPolicy.Bitwise);
        }
    }

    /// <summary>
    /// Deep clone delegate for <see cref="DiffPrimaryCtorBag"/>.
    /// </summary>
    internal static DiffPrimaryCtorBag ClonePrimary(DiffPrimaryCtorBag src, DiffArrayCloneScope scope)
        => new(src.Scale, src.Bias, scope.CloneArray(src.Data) ?? Array.Empty<int>());

    /// <summary>
    /// Structural comparison delegate for <see cref="DiffPrimaryCtorBag"/>.
    /// </summary>
    internal static void ComparePrimary(StructFieldCheckContext ctx, DiffPrimaryCtorBag o, DiffPrimaryCtorBag a)
    {
        ctx.Field("Scale", o.Scale, a.Scale);
        ctx.Field("Bias.X", o.Bias.X, a.Bias.X, OutputPolicy.Bitwise);
        ctx.Field("Bias.Y", o.Bias.Y, a.Bias.Y, OutputPolicy.Bitwise);
        ctx.Field("Data.Length", o.Data.Length, a.Data.Length);
        int len = Math.Min(o.Data.Length, a.Data.Length);
        for (int i = 0; i < len; i++)
        {
            ctx.Field($"Data[{i}]", o.Data[i], a.Data[i]);
        }
    }
}
