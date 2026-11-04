#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.DeviceLibrary;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

[Category(TestCategory.Differential)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public sealed class MarshalingBoundaryDifferentialTests
{
    private const int DefaultRows = 64;
    private const int DefaultBlockX = 32;

    private sealed class NullableValueElementComparer<T> : IElementComparer<T?>
        where T : struct
    {
        public static readonly NullableValueElementComparer<T> Instance = new();

        public bool Equals(in T? expected, in T? actual, OutputPolicy policy) =>
            EqualityComparer<T?>.Default.Equals(expected, actual);

        public string Format(in T? value) =>
            value.HasValue ? value.Value.ToString() ?? string.Empty : "null";
    }

    private static readonly IElementComparer<DiffBoolPack> BoolPackComparer =
        new StructElementComparer<DiffBoolPack>(static (ctx, e, a) =>
        {
            ctx.Field(nameof(DiffBoolPack.Flag0), e.Flag0, a.Flag0, OutputPolicy.Exact);
            ctx.Field(nameof(DiffBoolPack.Mid), e.Mid, a.Mid, OutputPolicy.Exact);
            ctx.Field(nameof(DiffBoolPack.Flag1), e.Flag1, a.Flag1, OutputPolicy.Exact);
        });

    private static readonly IElementComparer<DiffCharPack> CharPackComparer =
        new StructElementComparer<DiffCharPack>(static (ctx, e, a) =>
        {
            ctx.Field(nameof(DiffCharPack.C0), e.C0, a.C0, OutputPolicy.Exact);
            ctx.Field(nameof(DiffCharPack.Mid), e.Mid, a.Mid, OutputPolicy.Exact);
            ctx.Field(nameof(DiffCharPack.C1), e.C1, a.C1, OutputPolicy.Exact);
        });

    private static readonly IElementComparer<DiffNullableBlitHolder> NullableBlitHolderComparer =
        new StructElementComparer<DiffNullableBlitHolder>(static (ctx, e, a) =>
        {
            ctx.Equal(nameof(DiffNullableBlitHolder.IntOpt), e.IntOpt, a.IntOpt);
            ctx.Equal(nameof(DiffNullableBlitHolder.ByteOpt), e.ByteOpt, a.ByteOpt);
            ctx.Equal(nameof(DiffNullableBlitHolder.CharOpt), e.CharOpt, a.CharOpt);
            ctx.Equal(nameof(DiffNullableBlitHolder.BoolOpt), e.BoolOpt, a.BoolOpt);
        });

    private static readonly IElementComparer<DiffNullableVec2Holder> NullableVec2HolderComparer =
        new StructElementComparer<DiffNullableVec2Holder>(static (ctx, e, a) =>
        {
            ctx.Field("V.HasValue", e.V.HasValue, a.V.HasValue, OutputPolicy.Exact);
            if (e.V.HasValue && a.V.HasValue)
            {
                ctx.Field("V.X", e.V.Value.X, a.V.Value.X, OutputPolicy.Bitwise);
                ctx.Field("V.Y", e.V.Value.Y, a.V.Value.Y, OutputPolicy.Bitwise);
            }

            ctx.Field("Rv.HasValue", e.Rv.HasValue, a.Rv.HasValue, OutputPolicy.Exact);
            if (e.Rv.HasValue && a.Rv.HasValue)
            {
                ctx.Field("Rv.X", e.Rv.Value.X, a.Rv.Value.X, OutputPolicy.Bitwise);
                ctx.Field("Rv.Y", e.Rv.Value.Y, a.Rv.Value.Y, OutputPolicy.Bitwise);
            }

            ctx.Field(nameof(DiffNullableVec2Holder.Tag), e.Tag, a.Tag, OutputPolicy.Exact);
        });

    private static readonly IElementComparer<DiffExplicitGapped> ExplicitGappedComparer =
        new StructElementComparer<DiffExplicitGapped>(static (ctx, e, a) =>
        {
            ctx.Field(nameof(DiffExplicitGapped.Head), e.Head, a.Head, OutputPolicy.Exact);
            ctx.Field(nameof(DiffExplicitGapped.Mid), e.Mid, a.Mid, OutputPolicy.Exact);
            ctx.Field(nameof(DiffExplicitGapped.Tail), e.Tail, a.Tail, OutputPolicy.Exact);
        });

    private static readonly IElementComparer<DiffFloatUintUnion> FloatUintUnionComparer =
        new StructElementComparer<DiffFloatUintUnion>(static (ctx, e, a) =>
        {
            ctx.Field(nameof(DiffFloatUintUnion.U), e.U, a.U, OutputPolicy.Exact);
            ctx.Field(nameof(DiffFloatUintUnion.F), e.F, a.F, OutputPolicy.Bitwise);
            ctx.Field(nameof(DiffFloatUintUnion.Tag), e.Tag, a.Tag, OutputPolicy.Exact);
        });

    private static readonly IElementComparer<DiffConvertExplicitUnion> ConvertExplicitUnionComparer =
        new StructElementComparer<DiffConvertExplicitUnion>(static (ctx, e, a) =>
        {
            ctx.Field("VecArm.A", e.VecArm.A, a.VecArm.A, OutputPolicy.Bitwise);
            ctx.Field("VecArm.V.X", e.VecArm.V.X, a.VecArm.V.X, OutputPolicy.Bitwise);
            ctx.Field("VecArm.V.Y", e.VecArm.V.Y, a.VecArm.V.Y, OutputPolicy.Bitwise);
        });

    private static readonly IElementComparer<DiffAutoMixed> AutoMixedComparer =
        new StructElementComparer<DiffAutoMixed>(static (ctx, e, a) =>
        {
            ctx.Field(nameof(DiffAutoMixed.B1), e.B1, a.B1, OutputPolicy.Exact);
            ctx.Field(nameof(DiffAutoMixed.L), e.L, a.L, OutputPolicy.Exact);
            ctx.Field(nameof(DiffAutoMixed.Flag), e.Flag, a.Flag, OutputPolicy.Exact);
            ctx.Field(nameof(DiffAutoMixed.S), e.S, a.S, OutputPolicy.Exact);
            ctx.Field(nameof(DiffAutoMixed.C), e.C, a.C, OutputPolicy.Exact);
        });

    private static readonly IElementComparer<DiffAutoNested> AutoNestedComparer =
        new StructElementComparer<DiffAutoNested>((ctx, e, a) =>
        {
            ctx.Field(nameof(DiffAutoNested.Header), e.Header, a.Header, OutputPolicy.Exact);
            ctx.Field(nameof(DiffAutoNested.Inner), e.Inner, a.Inner, OutputPolicy.ForStruct(AutoMixedComparer));
        });

    private static readonly IElementComparer<DiffInlineVec2Padded3> InlineVec2Padded3Comparer =
        new StructElementComparer<DiffInlineVec2Padded3>(static (ctx, e, a) =>
        {
            for (int k = 0; k < 3; k++)
            {
                ctx.Field($"[{k}].A", e[k].A, a[k].A, OutputPolicy.Bitwise);
                ctx.Field($"[{k}].V.X", e[k].V.X, a[k].V.X, OutputPolicy.Bitwise);
                ctx.Field($"[{k}].V.Y", e[k].V.Y, a[k].V.Y, OutputPolicy.Bitwise);
            }
        });

    private static readonly IElementComparer<DiffInlineOuterPacket> InlineOuterPacketComparer =
        new StructElementComparer<DiffInlineOuterPacket>((ctx, e, a) =>
        {
            ctx.Field(nameof(DiffInlineOuterPacket.Tag), e.Tag, a.Tag, OutputPolicy.Exact);
            for (int k = 0; k < 3; k++)
            {
                ctx.Field($"Bools[{k}]", e.Bools[k], a.Bools[k], OutputPolicy.Exact);
                ctx.Field($"Chars[{k}]", e.Chars[k], a.Chars[k], OutputPolicy.Exact);
            }

            ctx.Field(nameof(DiffInlineOuterPacket.Vecs), e.Vecs, a.Vecs, OutputPolicy.ForStruct(InlineVec2Padded3Comparer));
        });

    private static readonly IElementComparer<FixedBufferPacket> FixedBufferPacketComparer =
        new StructElementComparer<FixedBufferPacket>(CompareFixedBufferPacket);

    private static unsafe void CompareFixedBufferPacket(
        StructFieldCheckContext ctx,
        FixedBufferPacket e,
        FixedBufferPacket a)
    {
        ctx.Field(nameof(FixedBufferPacket.Header), e.Header, a.Header, OutputPolicy.Exact);
        for (int k = 0; k < 4; k++)
        {
            ctx.Field($"Items[{k}]", e.Items[k], a.Items[k], OutputPolicy.Exact);
            ctx.Field($"Bytes[{k}]", e.Bytes[k], a.Bytes[k], OutputPolicy.Exact);
        }
    }

    private static readonly IElementComparer<DiffInlineNullableVec2Holder> InlineNullableVec2HolderComparer =
        new StructElementComparer<DiffInlineNullableVec2Holder>(static (ctx, e, a) =>
        {
            ctx.Field(nameof(DiffInlineNullableVec2Holder.Tag), e.Tag, a.Tag, OutputPolicy.Exact);
            for (int k = 0; k < 2; k++)
            {
                ctx.Field($"Items[{k}].HasValue", e.Items[k].HasValue, a.Items[k].HasValue, OutputPolicy.Exact);
                if (e.Items[k] is { } ev && a.Items[k] is { } av)
                {
                    ctx.Field($"Items[{k}].X", ev.X, av.X, OutputPolicy.Bitwise);
                    ctx.Field($"Items[{k}].Y", ev.Y, av.Y, OutputPolicy.Bitwise);
                }
            }
        });

    private static readonly IElementComparer<OuterPacket> OuterPacketComparer =
        new StructElementComparer<OuterPacket>(static (ctx, e, a) =>
        {
            ctx.Field(nameof(OuterPacket.Tag), e.Tag, a.Tag, OutputPolicy.Exact);
            ctx.Field("First.Left", e.First.Left, a.First.Left, OutputPolicy.Exact);
            ctx.Field("First.Right", e.First.Right, a.First.Right, OutputPolicy.Bitwise);
            ctx.Field("Vec2.X", e.Vec2.X, a.Vec2.X, OutputPolicy.Bitwise);
            ctx.Field("Vec2.Y", e.Vec2.Y, a.Vec2.Y, OutputPolicy.Bitwise);
            ctx.Field("Second.Left", e.Second.Left, a.Second.Left, OutputPolicy.Exact);
            ctx.Field("Second.Right", e.Second.Right, a.Second.Right, OutputPolicy.Bitwise);
        });

    private static readonly IElementComparer<DiffVecAlignTorture> VecAlignTortureComparer =
        new StructElementComparer<DiffVecAlignTorture>(static (ctx, e, a) =>
        {
            ctx.Field(nameof(DiffVecAlignTorture.B0), e.B0, a.B0, OutputPolicy.Exact);
            ctx.Field("V3.X", e.V3.X, a.V3.X, OutputPolicy.Bitwise);
            ctx.Field("V3.Y", e.V3.Y, a.V3.Y, OutputPolicy.Bitwise);
            ctx.Field("V3.Z", e.V3.Z, a.V3.Z, OutputPolicy.Bitwise);
            ctx.Field(nameof(DiffVecAlignTorture.B1), e.B1, a.B1, OutputPolicy.Exact);
            ctx.Field("V2.X", e.V2.X, a.V2.X, OutputPolicy.Bitwise);
            ctx.Field("V2.Y", e.V2.Y, a.V2.Y, OutputPolicy.Bitwise);
            ctx.Field(nameof(DiffVecAlignTorture.B2), e.B2, a.B2, OutputPolicy.Exact);
            ctx.Field("V4.X", e.V4.X, a.V4.X, OutputPolicy.Bitwise);
            ctx.Field("V4.Y", e.V4.Y, a.V4.Y, OutputPolicy.Bitwise);
            ctx.Field("V4.Z", e.V4.Z, a.V4.Z, OutputPolicy.Bitwise);
            ctx.Field("V4.W", e.V4.W, a.V4.W, OutputPolicy.Bitwise);
        });

    private static readonly IElementComparer<PackedPair> PackedPairComparer =
        new StructElementComparer<PackedPair>(static (ctx, e, a) =>
        {
            ctx.Field(nameof(PackedPair.Tag), e.Tag, a.Tag, OutputPolicy.Exact);
            ctx.Field(nameof(PackedPair.Value), e.Value, a.Value, OutputPolicy.Exact);
        });

    private static readonly IElementComparer<SwapPair> SwapPairComparer =
        new StructElementComparer<SwapPair>(static (ctx, e, a) =>
        {
            ctx.Field(nameof(SwapPair.A), e.A, a.A, OutputPolicy.Exact);
            ctx.Field(nameof(SwapPair.B), e.B, a.B, OutputPolicy.Exact);
        });

    private static readonly IElementComparer<DiffExternalNestedHolder> ExternalNestedHolderComparer =
        new StructElementComparer<DiffExternalNestedHolder>(static (ctx, e, a) =>
        {
            ctx.Field(nameof(DiffExternalNestedHolder.Prefix), e.Prefix, a.Prefix, OutputPolicy.Exact);
            ctx.Field("Packed.Tag", e.Packed.Tag, a.Packed.Tag, OutputPolicy.Exact);
            ctx.Field("Packed.Value", e.Packed.Value, a.Packed.Value, OutputPolicy.Exact);
            ctx.Field("SizedItem.A", e.SizedItem.A, a.SizedItem.A, OutputPolicy.Exact);
            ctx.Field("Swapped.A", e.Swapped.A, a.Swapped.A, OutputPolicy.Exact);
            ctx.Field("Swapped.B", e.Swapped.B, a.Swapped.B, OutputPolicy.Exact);
            ctx.Field("UnionItem.Bits", e.UnionItem.Bits, a.UnionItem.Bits, OutputPolicy.Exact);
            ctx.Field("UnionItem.Tag", e.UnionItem.Tag, a.UnionItem.Tag, OutputPolicy.Exact);
        });

    private static readonly DifferentialCase s_boolAllPositions = new(
        $"{nameof(MarshalingBoundaryDifferentialKernels)}.{nameof(MarshalingBoundaryDifferentialKernels.BoolAllPositionsKernel)}")
    {
        Shape = LaunchShape.ForRows(DefaultRows, blockX: DefaultBlockX),
        Inputs = static (rng, s) =>
        {
            bool v = (rng.NextInt32() & 1) == 0;
            bool iv = (rng.NextInt32() & 2) != 0;
            bool rv = !v;
            var arr = new bool[s.Rows];
            for (int i = 0; i < s.Rows; i++)
            {
                arr[i] = i < 4 ? (i & 1) != 0 : (rng.NextInt32() & 1) != 0;
            }

            var pack = new DiffBoolPack
            {
                Flag0 = v,
                Mid = (rng.NextInt32() & 0xFF) - 64,
                Flag1 = !iv,
            };

            return new DiffBuffers()
                .InScalar("v", v)
                .InScalar("iv", iv)
                .RefScalar("rv", rv)
                .OutScalar<bool>("ov")
                .In("arr", arr, OutputPolicy.Exact)
                .RefScalar("pack", pack, OutputPolicy.ForStruct(BoolPackComparer), BoolPackComparer)
                .Out<int>("probe", s.Rows, MarshalingBoundaryDifferentialKernels.BoolProbeColumns, OutputPolicy.Exact)
                .OutAutoResult<bool>(s.Rows, OutputPolicy.Exact);
        },
        Oracle = static (b, _) =>
        {
            bool iv = b.GetScalar<bool>("iv");
            bool ret = MarshalingBoundaryDifferentialKernels.BoolAllPositionsKernel(
                b.GetScalar<bool>("v"),
                in iv,
                ref b.GetScalarRef<bool>("rv"),
                out bool ov,
                b.Get<bool>("arr"),
                ref b.GetScalarRef<DiffBoolPack>("pack"),
                b.Get<int>("probe")
            );
            b.SetScalar("ov", ov);
            int tid = (BlockIndex.X * BlockDimension.X) + ThreadIndex.X;
            var autoRes = b.GetAutoResult<bool>();
            if ((uint)tid < (uint)autoRes.Length)
            {
                autoRes[tid] = ret;
            }
        },
        Launch = static (b, s) =>
        {
            bool iv = b.GetScalar<bool>("iv");
            var res = MarshalingBoundaryDifferentialKernels.LaunchBoolAllPositionsKernel(
                b.GetScalar<bool>("v"),
                iv,
                ref b.GetScalarRef<bool>("rv"),
                out bool ov,
                b.Get<bool>("arr"),
                ref b.GetScalarRef<DiffBoolPack>("pack"),
                b.Get<int>("probe"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            );
            b.SetScalar("ov", ov);
            b.SetAutoResult(res);
        },
    };

    private static readonly char[] s_witnessChars =
    [
        '\0',
        'A',
        '\u007F',
        '\u0080',
        '\u1234',
        '\u8000',
        '\uD800',
        '\uFFFE',
        '\uFFFF',
    ];

    private static readonly DifferentialCase s_charAllPositions = new(
        $"{nameof(MarshalingBoundaryDifferentialKernels)}.{nameof(MarshalingBoundaryDifferentialKernels.CharAllPositionsKernel)}")
    {
        Shape = LaunchShape.ForRows(DefaultRows, blockX: DefaultBlockX),
        Inputs = static (rng, s) =>
        {
            // Always pick a non-ASCII UTF-16 code unit for v and iv so #971 consistently reproduces on Native CPU
            char v = (char)(0x1234 ^ (rng.NextInt32() & 0x00FF));
            char iv = (char)(0x8001 | (rng.NextInt32() & 0x0FFF));
            char rv = '\u1234';
            var arr = new char[s.Rows];
            for (int i = 0; i < s.Rows; i++)
            {
                arr[i] = i < s_witnessChars.Length
                    ? s_witnessChars[i]
                    : (char)(rng.NextInt32() & 0xFFFF);
            }

            var pack = new DiffCharPack
            {
                C0 = '\u03A9',
                Mid = 10,
                C1 = '\u2603',
            };

            return new DiffBuffers()
                .InScalar("v", v)
                .InScalar("iv", iv)
                .RefScalar("rv", rv)
                .OutScalar<char>("ov")
                .In("arr", arr, OutputPolicy.Exact)
                .RefScalar("pack", pack, OutputPolicy.ForStruct(CharPackComparer), CharPackComparer)
                .Out<int>("probe", s.Rows, MarshalingBoundaryDifferentialKernels.CharProbeColumns, OutputPolicy.Exact)
                .OutAutoResult<char>(s.Rows, OutputPolicy.Exact);
        },
        Oracle = static (b, _) =>
        {
            char iv = b.GetScalar<char>("iv");
            char ret = MarshalingBoundaryDifferentialKernels.CharAllPositionsKernel(
                b.GetScalar<char>("v"),
                in iv,
                ref b.GetScalarRef<char>("rv"),
                out char ov,
                b.Get<char>("arr"),
                ref b.GetScalarRef<DiffCharPack>("pack"),
                b.Get<int>("probe")
            );
            b.SetScalar("ov", ov);
            int tid = (BlockIndex.X * BlockDimension.X) + ThreadIndex.X;
            var autoRes = b.GetAutoResult<char>();
            if ((uint)tid < (uint)autoRes.Length)
            {
                autoRes[tid] = ret;
            }
        },
        Launch = static (b, s) =>
        {
            char iv = b.GetScalar<char>("iv");
            var res = MarshalingBoundaryDifferentialKernels.LaunchCharAllPositionsKernel(
                b.GetScalar<char>("v"),
                iv,
                ref b.GetScalarRef<char>("rv"),
                out char ov,
                b.Get<char>("arr"),
                ref b.GetScalarRef<DiffCharPack>("pack"),
                b.Get<int>("probe"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            );
            b.SetScalar("ov", ov);
            b.SetAutoResult(res);
        },
    };

    private static readonly DifferentialCase s_nullableBlitPositions = new(
        $"{nameof(MarshalingBoundaryDifferentialKernels)}.{nameof(MarshalingBoundaryDifferentialKernels.NullableBlitPositionsKernel)}")
    {
        Shape = LaunchShape.ForRows(DefaultRows, blockX: DefaultBlockX),
        Inputs = static (rng, s) =>
        {
            var arr = new int?[s.Rows];
            arr[0] = null;
            arr[1] = 0;
            arr[2] = int.MinValue + 10;
            arr[3] = int.MaxValue - 100;
            for (int i = 4; i < s.Rows; i++)
            {
                arr[i] = (i % 3 == 0) ? null : (rng.NextInt32() & 0xFFFF) - 32768;
            }

            bool hasValues = (rng.NextInt32() & 1) == 0;
            var holder = new DiffNullableBlitHolder
            {
                IntOpt = hasValues ? 123 : null,
                ByteOpt = hasValues ? (byte)0xAB : null,
                CharOpt = hasValues ? '\u1234' : null,
                BoolOpt = hasValues ? true : null,
            };

            var nullableIntComparer = NullableValueElementComparer<int>.Instance;
            return new DiffBuffers()
                .In("arr", arr, OutputPolicy.ForStruct(nullableIntComparer), nullableIntComparer)
                .RefScalar("h", holder, OutputPolicy.ForStruct(NullableBlitHolderComparer), NullableBlitHolderComparer)
                .Out<int>("probe", s.Rows, MarshalingBoundaryDifferentialKernels.NullableBlitProbeColumns, OutputPolicy.Exact)
                .OutAutoResult<int?>(s.Rows, OutputPolicy.ForStruct(nullableIntComparer), nullableIntComparer);
        },
        Oracle = static (b, _) =>
        {
            int? ret = MarshalingBoundaryDifferentialKernels.NullableBlitPositionsKernel(
                b.Get<int?>("arr"),
                ref b.GetScalarRef<DiffNullableBlitHolder>("h"),
                b.Get<int>("probe")
            );
            int tid = (BlockIndex.X * BlockDimension.X) + ThreadIndex.X;
            var autoRes = b.GetAutoResult<int?>();
            if ((uint)tid < (uint)autoRes.Length)
            {
                autoRes[tid] = ret;
            }
        },
        Launch = static (b, s) =>
        {
            var res = MarshalingBoundaryDifferentialKernels.LaunchNullableBlitPositionsKernel(
                b.Get<int?>("arr"),
                ref b.GetScalarRef<DiffNullableBlitHolder>("h"),
                b.Get<int>("probe"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            );
            b.SetAutoResult(res);
        },
    };

    private static readonly DifferentialCase s_nullableBlitScalarArg = new(
        $"{nameof(MarshalingBoundaryDifferentialKernels)}.{nameof(MarshalingBoundaryDifferentialKernels.NullableBlitScalarArgKernel)}")
    {
        Shape = LaunchShape.ForRows(DefaultRows, blockX: DefaultBlockX),
        Seeds = [0],
        Inputs = static (_, s) =>
        {
            var nullableByteComparer = NullableValueElementComparer<byte>.Instance;
            var nullableCharComparer = NullableValueElementComparer<char>.Instance;
            return new DiffBuffers()
                .InScalar<int?>("v", 42, NullableValueElementComparer<int>.Instance)
                .RefScalar<byte?>("rb", (byte)10, OutputPolicy.ForStruct(nullableByteComparer), nullableByteComparer)
                .OutScalar<char?>("oc", OutputPolicy.ForStruct(nullableCharComparer), nullableCharComparer)
                .Out<int>("probe", s.Rows, MarshalingBoundaryDifferentialKernels.NullableBlitScalarProbeColumns, OutputPolicy.Exact);
        },
        Oracle = static (b, _) =>
        {
            MarshalingBoundaryDifferentialKernels.NullableBlitScalarArgKernel(
                b.GetScalar<int?>("v"),
                ref b.GetScalarRef<byte?>("rb"),
                out char? oc,
                b.Get<int>("probe")
            );
            b.SetScalar("oc", oc);
        },
        Launch = static (b, s) =>
        {
#if GSHARP_CPU
            try
            {
                MarshalingBoundaryDifferentialKernels.LaunchNullableBlitScalarArgKernel(
                    b.GetScalar<int?>("v"),
                    ref b.GetScalarRef<byte?>("rb"),
                    out char? oc,
                    b.Get<int>("probe"),
                    gridDimX: s.GridX,
                    blockDimX: s.BlockX
                );
                b.SetScalar("oc", oc);
            }
            catch (MarshalDirectiveException) when (!CpuKernelLoader.ForceManagedExecution)
            {
                // #961: Native CPU P/Invoke delegate embeds generic System.Nullable<T> directly,
                // throwing MarshalDirectiveException. Leaving probe/rb/oc at initial values
                // surfaces the mismatch for KnownDivergences (#961).
            }
#else
            MarshalingBoundaryDifferentialKernels.LaunchNullableBlitScalarArgKernel(
                b.GetScalar<int?>("v"),
                ref b.GetScalarRef<byte?>("rb"),
                out char? oc,
                b.Get<int>("probe"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            );
            b.SetScalar("oc", oc);
#endif
        },
    };

    private static readonly DifferentialCase s_nullableConvertPositions = new(
        $"{nameof(MarshalingBoundaryDifferentialKernels)}.{nameof(MarshalingBoundaryDifferentialKernels.NullableConvertPositionsKernel)}")
    {
        Shape = LaunchShape.ForRows(DefaultRows, blockX: DefaultBlockX),
        Seeds = [0],
        Inputs = static (_, s) =>
        {
            var holder = new DiffNullableVec2Holder
            {
                V = new Vector2(1.5f, -2.25f),
                Rv = new Vector2(3.5f, 4.75f),
                Tag = 10,
            };

            return new DiffBuffers()
                .RefScalar("h", holder, OutputPolicy.ForStruct(NullableVec2HolderComparer), NullableVec2HolderComparer)
                .Out<float>("probe", s.Rows, MarshalingBoundaryDifferentialKernels.NullableConvertProbeColumns, OutputPolicy.Bitwise);
        },
        Oracle = static (b, _) =>
            MarshalingBoundaryDifferentialKernels.NullableConvertPositionsKernel(
                ref b.GetScalarRef<DiffNullableVec2Holder>("h"),
                b.Get<float>("probe")
            ),
        Launch = static (b, s) =>
            MarshalingBoundaryDifferentialKernels.LaunchNullableConvertPositionsKernel(
                ref b.GetScalarRef<DiffNullableVec2Holder>("h"),
                b.Get<float>("probe"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
    };

    private static readonly uint[] s_floatUnionWitnesses =
    [
        0x0000_0000u, // +0.0f
        0x8000_0000u, // -0.0f
        0x3F80_0000u, // 1.0f
        0x0000_0001u, // smallest subnormal
        0x7F80_0000u, // +Infinity
        0x7FC0_1234u, // quiet NaN payload
    ];

    private static readonly DifferentialCase s_explicitLayoutAndUnionPositions = new(
        $"{nameof(MarshalingBoundaryDifferentialKernels)}.{nameof(MarshalingBoundaryDifferentialKernels.ExplicitLayoutAndUnionPositionsKernel)}")
    {
        Shape = LaunchShape.ForRows(DefaultRows, blockX: DefaultBlockX),
        Inputs = static (rng, s) =>
        {
            var gVal = new DiffExplicitGapped
            {
                Head = 7,
                Mid = 1000L,
                Tail = 0x1234,
            };
            var uRef = new DiffFloatUintUnion
            {
                U = 0x3F80_0000u,
                Tag = 50,
            };

            var gArr = new DiffExplicitGapped[s.Rows];
            var uArr = new DiffFloatUintUnion[s.Rows];
            for (int i = 0; i < s.Rows; i++)
            {
                gArr[i] = new DiffExplicitGapped
                {
                    Head = (byte)(i + 1),
                    Mid = (i * 100L) - 500L,
                    Tail = (rng.NextInt32() & 0xFFFF),
                };
                uArr[i] = new DiffFloatUintUnion
                {
                    U = i < s_floatUnionWitnesses.Length ? s_floatUnionWitnesses[i] : (uint)rng.NextInt32(),
                    Tag = i + 1,
                };
            }

            return new DiffBuffers()
                .InScalar("gVal", gVal, ExplicitGappedComparer)
                .RefScalar("uRef", uRef, OutputPolicy.ForStruct(FloatUintUnionComparer), FloatUintUnionComparer)
                .In("gArr", gArr, OutputPolicy.ForStruct(ExplicitGappedComparer), ExplicitGappedComparer)
                .In("uArr", uArr, OutputPolicy.ForStruct(FloatUintUnionComparer), FloatUintUnionComparer)
                .Out<int>("probe", s.Rows, MarshalingBoundaryDifferentialKernels.ExplicitUnionProbeColumns, OutputPolicy.Exact)
                .OutAutoResult<DiffFloatUintUnion>(s.Rows, OutputPolicy.ForStruct(FloatUintUnionComparer), FloatUintUnionComparer);
        },
        Oracle = static (b, _) =>
        {
            var ret = MarshalingBoundaryDifferentialKernels.ExplicitLayoutAndUnionPositionsKernel(
                b.GetScalar<DiffExplicitGapped>("gVal"),
                ref b.GetScalarRef<DiffFloatUintUnion>("uRef"),
                b.Get<DiffExplicitGapped>("gArr"),
                b.Get<DiffFloatUintUnion>("uArr"),
                b.Get<int>("probe")
            );
            int tid = (BlockIndex.X * BlockDimension.X) + ThreadIndex.X;
            var autoRes = b.GetAutoResult<DiffFloatUintUnion>();
            if ((uint)tid < (uint)autoRes.Length)
            {
                autoRes[tid] = ret;
            }
        },
        Launch = static (b, s) =>
        {
            var res = MarshalingBoundaryDifferentialKernels.LaunchExplicitLayoutAndUnionPositionsKernel(
                b.GetScalar<DiffExplicitGapped>("gVal"),
                ref b.GetScalarRef<DiffFloatUintUnion>("uRef"),
                b.Get<DiffExplicitGapped>("gArr"),
                b.Get<DiffFloatUintUnion>("uArr"),
                b.Get<int>("probe"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            );
            b.SetAutoResult(res);
        },
    };

    private static readonly DifferentialCase s_explicitUnionWithConvertArm = new(
        $"{nameof(MarshalingBoundaryDifferentialKernels)}.{nameof(MarshalingBoundaryDifferentialKernels.ExplicitUnionWithConvertArmKernel)}")
    {
        Shape = LaunchShape.ForRows(DefaultRows, blockX: DefaultBlockX),
        Seeds = [0],
        Inputs = static (_, s) =>
        {
            var u = new DiffConvertExplicitUnion
            {
                VecArm = new DiffVec2Padded
                {
                    A = 2.0f,
                    V = new Vector2(5.0f, 10.0f),
                },
            };

            return new DiffBuffers()
                .RefScalar("u", u, OutputPolicy.ForStruct(ConvertExplicitUnionComparer), ConvertExplicitUnionComparer)
                .Out<float>("probe", s.Rows, MarshalingBoundaryDifferentialKernels.ConvertUnionProbeColumns, OutputPolicy.Bitwise);
        },
        Oracle = static (b, _) =>
            MarshalingBoundaryDifferentialKernels.ExplicitUnionWithConvertArmKernel(
                ref b.GetScalarRef<DiffConvertExplicitUnion>("u"),
                b.Get<float>("probe")
            ),
        Launch = static (b, s) =>
            MarshalingBoundaryDifferentialKernels.LaunchExplicitUnionWithConvertArmKernel(
                ref b.GetScalarRef<DiffConvertExplicitUnion>("u"),
                b.Get<float>("probe"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
    };

    private static readonly DifferentialCase s_autoLayoutAllPositions = new(
        $"{nameof(MarshalingBoundaryDifferentialKernels)}.{nameof(MarshalingBoundaryDifferentialKernels.AutoLayoutAllPositionsKernel)}")
    {
        Shape = LaunchShape.ForRows(DefaultRows, blockX: DefaultBlockX),
        Inputs = static (rng, s) =>
        {
            var val = new DiffAutoMixed { B1 = 3, L = 1000L, Flag = true, S = -12, C = 'X' };
            var inVal = new DiffAutoMixed { B1 = 5, L = 2000L, Flag = false, S = 34, C = '\u03B1' };
            var refVal = new DiffAutoMixed { B1 = 9, L = -500L, Flag = true, S = 100, C = 'Z' };
            var nested = new DiffAutoNested
            {
                Header = 42,
                Inner = new DiffAutoMixed { B1 = 11, L = 777L, Flag = false, S = -7, C = '\u1234' },
            };

            var arr = new DiffAutoMixed[s.Rows];
            for (int i = 0; i < s.Rows; i++)
            {
                arr[i] = new DiffAutoMixed
                {
                    B1 = (byte)(i + 1),
                    L = (rng.NextInt32() & 0xFFFF) - 32768L,
                    Flag = (i & 1) == 0,
                    S = (short)((i * 7) - 50),
                    C = (char)('A' + (i % 26)),
                };
            }

            return new DiffBuffers()
                .InScalar("val", val, AutoMixedComparer)
                .InScalar("inVal", inVal, AutoMixedComparer)
                .RefScalar("refVal", refVal, OutputPolicy.ForStruct(AutoMixedComparer), AutoMixedComparer)
                .OutScalar("outVal", OutputPolicy.ForStruct(AutoMixedComparer), AutoMixedComparer)
                .In("arr", arr, OutputPolicy.ForStruct(AutoMixedComparer), AutoMixedComparer)
                .RefScalar("nested", nested, OutputPolicy.ForStruct(AutoNestedComparer), AutoNestedComparer)
                .Out<long>("probe", s.Rows, MarshalingBoundaryDifferentialKernels.AutoLayoutProbeColumns, OutputPolicy.Exact)
                .OutAutoResult<DiffAutoMixed>(s.Rows, OutputPolicy.ForStruct(AutoMixedComparer), AutoMixedComparer);
        },
        Oracle = static (b, _) =>
        {
            var inVal = b.GetScalar<DiffAutoMixed>("inVal");
            var ret = MarshalingBoundaryDifferentialKernels.AutoLayoutAllPositionsKernel(
                b.GetScalar<DiffAutoMixed>("val"),
                in inVal,
                ref b.GetScalarRef<DiffAutoMixed>("refVal"),
                out DiffAutoMixed outVal,
                b.Get<DiffAutoMixed>("arr"),
                ref b.GetScalarRef<DiffAutoNested>("nested"),
                b.Get<long>("probe")
            );
            b.SetScalar("outVal", outVal);
            int tid = (BlockIndex.X * BlockDimension.X) + ThreadIndex.X;
            var autoRes = b.GetAutoResult<DiffAutoMixed>();
            if ((uint)tid < (uint)autoRes.Length)
            {
                autoRes[tid] = ret;
            }
        },
        Launch = static (b, s) =>
        {
            var inVal = b.GetScalar<DiffAutoMixed>("inVal");
            var res = MarshalingBoundaryDifferentialKernels.LaunchAutoLayoutAllPositionsKernel(
                b.GetScalar<DiffAutoMixed>("val"),
                inVal,
                ref b.GetScalarRef<DiffAutoMixed>("refVal"),
                out DiffAutoMixed outVal,
                b.Get<DiffAutoMixed>("arr"),
                ref b.GetScalarRef<DiffAutoNested>("nested"),
                b.Get<long>("probe"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            );
            b.SetScalar("outVal", outVal);
            b.SetAutoResult(res);
        },
    };

    private static unsafe DifferentialCase CreateInlineArrayAndFixedBufferCase() =>
        new(
            $"{nameof(MarshalingBoundaryDifferentialKernels)}.{nameof(MarshalingBoundaryDifferentialKernels.InlineArrayAndFixedBufferPositionsKernel)}")
        {
            Shape = LaunchShape.ForRows(DefaultRows, blockX: DefaultBlockX),
            Inputs = static (_, s) =>
            {
                var valVecs = default(DiffInlineVec2Padded3);
                valVecs[0] = new DiffVec2Padded { A = 1.0f, V = new Vector2(2.0f, 3.0f) };
                valVecs[1] = new DiffVec2Padded { A = 4.0f, V = new Vector2(5.0f, 6.0f) };
                valVecs[2] = new DiffVec2Padded { A = 7.0f, V = new Vector2(8.0f, 9.0f) };

                var outer = new DiffInlineOuterPacket { Tag = 10 };
                outer.Bools[0] = true;
                outer.Bools[1] = false;
                outer.Bools[2] = true;
                outer.Chars[0] = 'G';
                outer.Chars[1] = '#';
                outer.Chars[2] = '\u03BB';
                outer.Vecs[0] = new DiffVec2Padded { A = 10.0f, V = new Vector2(11.0f, 12.0f) };
                outer.Vecs[1] = new DiffVec2Padded { A = 20.0f, V = new Vector2(21.0f, 22.0f) };
                outer.Vecs[2] = new DiffVec2Padded { A = 30.0f, V = new Vector2(31.0f, 32.0f) };

                var fixedPkt = new FixedBufferPacket { Header = 5 };
                fixedPkt.Items[0] = 10;
                fixedPkt.Items[1] = 20;
                fixedPkt.Items[2] = 30;
                fixedPkt.Items[3] = 40;
                fixedPkt.Bytes[0] = 1;
                fixedPkt.Bytes[1] = 2;
                fixedPkt.Bytes[2] = 3;
                fixedPkt.Bytes[3] = 4;

                var vecArr = new DiffInlineVec2Padded3[s.Rows];
                var fixedArr = new FixedBufferPacket[s.Rows];
                for (int i = 0; i < s.Rows; i++)
                {
                    var vElem = default(DiffInlineVec2Padded3);
                    vElem[0] = new DiffVec2Padded { A = i + 1.0f, V = new Vector2(i + 2.0f, i + 3.0f) };
                    vElem[1] = new DiffVec2Padded { A = i + 4.0f, V = new Vector2(i + 5.0f, i + 6.0f) };
                    vElem[2] = new DiffVec2Padded { A = i + 7.0f, V = new Vector2(i + 8.0f, i + 9.0f) };
                    vecArr[i] = vElem;

                    var fElem = new FixedBufferPacket { Header = i };
                    fElem.Items[0] = i * 2;
                    fElem.Items[1] = (i * 2) + 1;
                    fElem.Items[2] = (i * 2) + 2;
                    fElem.Items[3] = (i * 2) + 3;
                    fElem.Bytes[0] = (byte)(i & 0xFF);
                    fElem.Bytes[1] = (byte)((i + 1) & 0xFF);
                    fElem.Bytes[2] = (byte)((i + 2) & 0xFF);
                    fElem.Bytes[3] = (byte)((i + 3) & 0xFF);
                    fixedArr[i] = fElem;
                }

                return new DiffBuffers()
                    .InScalar("valVecs", valVecs, InlineVec2Padded3Comparer)
                    .RefScalar("outer", outer, OutputPolicy.ForStruct(InlineOuterPacketComparer), InlineOuterPacketComparer)
                    .In("vecArr", vecArr, OutputPolicy.ForStruct(InlineVec2Padded3Comparer), InlineVec2Padded3Comparer)
                    .RefScalar("fixedPkt", fixedPkt, OutputPolicy.ForStruct(FixedBufferPacketComparer), FixedBufferPacketComparer)
                    .In("fixedArr", fixedArr, OutputPolicy.ForStruct(FixedBufferPacketComparer), FixedBufferPacketComparer)
                    .Out<int>("probe", s.Rows, MarshalingBoundaryDifferentialKernels.InlineAndFixedProbeColumns, OutputPolicy.Exact)
                    .OutAutoResult<DiffInlineVec2Padded3>(s.Rows, OutputPolicy.ForStruct(InlineVec2Padded3Comparer), InlineVec2Padded3Comparer);
            },
            Oracle = static (b, _) =>
            {
                var ret = MarshalingBoundaryDifferentialKernels.InlineArrayAndFixedBufferPositionsKernel(
                    b.GetScalar<DiffInlineVec2Padded3>("valVecs"),
                    ref b.GetScalarRef<DiffInlineOuterPacket>("outer"),
                    b.Get<DiffInlineVec2Padded3>("vecArr"),
                    ref b.GetScalarRef<FixedBufferPacket>("fixedPkt"),
                    b.Get<FixedBufferPacket>("fixedArr"),
                    b.Get<int>("probe")
                );
                int tid = (BlockIndex.X * BlockDimension.X) + ThreadIndex.X;
                var autoRes = b.GetAutoResult<DiffInlineVec2Padded3>();
                if ((uint)tid < (uint)autoRes.Length)
                {
                    autoRes[tid] = ret;
                }
            },
            Launch = static (b, s) =>
            {
                var res = MarshalingBoundaryDifferentialKernels.LaunchInlineArrayAndFixedBufferPositionsKernel(
                    b.GetScalar<DiffInlineVec2Padded3>("valVecs"),
                    ref b.GetScalarRef<DiffInlineOuterPacket>("outer"),
                    b.Get<DiffInlineVec2Padded3>("vecArr"),
                    ref b.GetScalarRef<FixedBufferPacket>("fixedPkt"),
                    b.Get<FixedBufferPacket>("fixedArr"),
                    b.Get<int>("probe"),
                    gridDimX: s.GridX,
                    blockDimX: s.BlockX
                );
                b.SetAutoResult(res);
            },
        };

    private static readonly DifferentialCase s_inlineArrayAndFixedBufferPositions = CreateInlineArrayAndFixedBufferCase();

    private static readonly DifferentialCase s_inlineArrayNullableVec2 = new(
        $"{nameof(MarshalingBoundaryDifferentialKernels)}.{nameof(MarshalingBoundaryDifferentialKernels.InlineArrayNullableVec2Kernel)}")
    {
        Shape = LaunchShape.ForRows(DefaultRows, blockX: DefaultBlockX),
        Seeds = [0],
        Inputs = static (_, s) =>
        {
            var h = new DiffInlineNullableVec2Holder { Tag = 7 };
            h.Items[0] = new Vector2(3.0f, 4.0f);
            h.Items[1] = new Vector2(5.0f, 6.0f);

            return new DiffBuffers()
                .RefScalar("h", h, OutputPolicy.ForStruct(InlineNullableVec2HolderComparer), InlineNullableVec2HolderComparer)
                .Out<float>("probe", s.Rows, MarshalingBoundaryDifferentialKernels.InlineNullableVec2ProbeColumns, OutputPolicy.Bitwise);
        },
        Oracle = static (b, _) =>
            MarshalingBoundaryDifferentialKernels.InlineArrayNullableVec2Kernel(
                ref b.GetScalarRef<DiffInlineNullableVec2Holder>("h"),
                b.Get<float>("probe")
            ),
        Launch = static (b, s) =>
            MarshalingBoundaryDifferentialKernels.LaunchInlineArrayNullableVec2Kernel(
                ref b.GetScalarRef<DiffInlineNullableVec2Holder>("h"),
                b.Get<float>("probe"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
    };

    private static readonly DifferentialCase s_vectorAlignmentAndExternalStructs = new(
        $"{nameof(MarshalingBoundaryDifferentialKernels)}.{nameof(MarshalingBoundaryDifferentialKernels.VectorAlignmentAndExternalStructsKernel)}")
    {
        Shape = LaunchShape.ForRows(DefaultRows, blockX: DefaultBlockX),
        Inputs = static (_, s) =>
        {
            var tortVal = new DiffVecAlignTorture
            {
                B0 = 1,
                V3 = new Vector3(2.0f, 3.0f, 4.0f),
                B1 = 5,
                V2 = new Vector2(6.0f, 7.0f),
                B2 = 8,
                V4 = new Vector4(9.0f, 10.0f, 11.0f, 12.0f),
            };

            var outerRef = new OuterPacket
            {
                Tag = 9,
                First = new InnerPair { Left = 10, Right = 2.5f },
                Vec2 = new Vector2(4.0f, -4.0f),
                Second = new InnerPair { Left = 20, Right = 7.5f },
            };

            var extHolder = new DiffExternalNestedHolder
            {
                Prefix = 3,
                Packed = new PackedPair { Tag = 7, Value = 100 },
                SizedItem = new Sized { A = 250 },
                Swapped = new SwapPair { A = 30, B = 12 },
                UnionItem = new IntFloatUnion { Value = 1.5f, Tag = 99 },
            };

            var tortArr = new DiffVecAlignTorture[s.Rows];
            var extPackedArr = new PackedPair[s.Rows];
            var extSwapArr = new SwapPair[s.Rows];
            for (int i = 0; i < s.Rows; i++)
            {
                tortArr[i] = new DiffVecAlignTorture
                {
                    B0 = (byte)((i & 0x3F) + 1),
                    V3 = new Vector3(i + 0.5f, i + 1.5f, i + 2.5f),
                    B1 = (byte)((i & 0x1F) + 2),
                    V2 = new Vector2(i * 2.0f, (i * -2.0f) - 1.0f),
                    B2 = (byte)((i & 0x0F) + 3),
                    V4 = new Vector4(i + 1.0f, i + 2.0f, i + 3.0f, i + 4.0f),
                };
                extPackedArr[i] = new PackedPair
                {
                    Tag = (byte)((i % 120) + 1),
                    Value = (i * 10) + 3,
                };
                extSwapArr[i] = new SwapPair
                {
                    A = (i * 5) + 100,
                    B = (i * 3) + 10,
                };
            }

            return new DiffBuffers()
                .InScalar("tortVal", tortVal, VecAlignTortureComparer)
                .RefScalar("outerRef", outerRef, OutputPolicy.ForStruct(OuterPacketComparer), OuterPacketComparer)
                .In("tortArr", tortArr, OutputPolicy.ForStruct(VecAlignTortureComparer), VecAlignTortureComparer)
                .In("extPackedArr", extPackedArr, OutputPolicy.ForStruct(PackedPairComparer), PackedPairComparer)
                .In("extSwapArr", extSwapArr, OutputPolicy.ForStruct(SwapPairComparer), SwapPairComparer)
                .RefScalar("extHolder", extHolder, OutputPolicy.ForStruct(ExternalNestedHolderComparer), ExternalNestedHolderComparer)
                .Out<float>("probe", s.Rows, MarshalingBoundaryDifferentialKernels.VecAlignAndExternalProbeColumns, OutputPolicy.Bitwise)
                .OutAutoResult<OuterPacket>(s.Rows, OutputPolicy.ForStruct(OuterPacketComparer), OuterPacketComparer);
        },
        Oracle = static (b, _) =>
        {
            var ret = MarshalingBoundaryDifferentialKernels.VectorAlignmentAndExternalStructsKernel(
                b.GetScalar<DiffVecAlignTorture>("tortVal"),
                ref b.GetScalarRef<OuterPacket>("outerRef"),
                b.Get<DiffVecAlignTorture>("tortArr"),
                b.Get<PackedPair>("extPackedArr"),
                b.Get<SwapPair>("extSwapArr"),
                ref b.GetScalarRef<DiffExternalNestedHolder>("extHolder"),
                b.Get<float>("probe")
            );
            int tid = (BlockIndex.X * BlockDimension.X) + ThreadIndex.X;
            var autoRes = b.GetAutoResult<OuterPacket>();
            if ((uint)tid < (uint)autoRes.Length)
            {
                autoRes[tid] = ret;
            }
        },
        Launch = static (b, s) =>
        {
            var res = MarshalingBoundaryDifferentialKernels.LaunchVectorAlignmentAndExternalStructsKernel(
                b.GetScalar<DiffVecAlignTorture>("tortVal"),
                ref b.GetScalarRef<OuterPacket>("outerRef"),
                b.Get<DiffVecAlignTorture>("tortArr"),
                b.Get<PackedPair>("extPackedArr"),
                b.Get<SwapPair>("extSwapArr"),
                ref b.GetScalarRef<DiffExternalNestedHolder>("extHolder"),
                b.Get<float>("probe"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            );
            b.SetAutoResult(res);
        },
    };

    private static unsafe DifferentialCase CreateMultiRefScalarAndRawPointerCase() =>
        new(
            $"{nameof(MarshalingBoundaryDifferentialKernels)}.{nameof(MarshalingBoundaryDifferentialKernels.MultiRefScalarAndRawPointerKernel)}")
        {
            Shape = LaunchShape.ForRows(DefaultRows, blockX: DefaultBlockX),
            Inputs = static (_, s) =>
            {
                var rawBuf = new int[s.Rows];
                var roTable = new int[s.Rows];
                for (int i = 0; i < s.Rows; i++)
                {
                    rawBuf[i] = (i * 3) + 10;
                    roTable[i] = (i * 5) - 20;
                }

                return new DiffBuffers()
                    .InScalar("inScalar", 7)
                    .RefScalar("refA", 12)
                    .RefScalar("refB", 30)
                    .RefScalar("refF", 2.5f, OutputPolicy.Bitwise)
                    .OutScalar<int>("outSum")
                    .In("rawBuf", rawBuf, OutputPolicy.Exact)
                    .InScalar("constFactor", 4)
                    .In("roTable", roTable, OutputPolicy.Exact, readOnly: true)
                    .Out<int>("probe", s.Rows, MarshalingBoundaryDifferentialKernels.MultiRefAndPointerProbeColumns, OutputPolicy.Exact);
            },
            Oracle = static (b, _) =>
            {
                int inScalar = b.GetScalar<int>("inScalar");
                var rawBuf = b.Get<int>("rawBuf");
                fixed (int* pValid = rawBuf)
                {
                    MarshalingBoundaryDifferentialKernels.MultiRefScalarAndRawPointerKernel(
                        in inScalar,
                        ref b.GetScalarRef<int>("refA"),
                        ref b.GetScalarRef<int>("refB"),
                        ref b.GetScalarRef<float>("refF"),
                        out int outSum,
                        pValid,
                        null,
                        b.GetScalar<int>("constFactor"),
                        b.Get<int>("roTable"),
                        b.Get<int>("probe")
                    );
                    b.SetScalar("outSum", outSum);
                }
            },
            Launch = static (b, s) =>
            {
                int inScalar = b.GetScalar<int>("inScalar");
                var rawOnly = new DiffBuffers().In("rawBuf", b.Get<int>("rawBuf"), OutputPolicy.Exact);
                using var scope = rawOnly.PinOrUpload();
                var pValid = (int*)scope.PinOrUpload<int>("rawBuf");
                MarshalingBoundaryDifferentialKernels.LaunchMultiRefScalarAndRawPointerKernel(
                    inScalar,
                    ref b.GetScalarRef<int>("refA"),
                    ref b.GetScalarRef<int>("refB"),
                    ref b.GetScalarRef<float>("refF"),
                    out int outSum,
                    pValid,
                    null,
                    b.GetScalar<int>("constFactor"),
                    b.Get<int>("roTable"),
                    b.Get<int>("probe"),
                    gridDimX: s.GridX,
                    blockDimX: s.BlockX
                );
                b.SetScalar("outSum", outSum);
            },
        };

    private static readonly DifferentialCase s_multiRefScalarAndRawPointer = CreateMultiRefScalarAndRawPointerCase();

    // bool across all 4 positions (value, in, ref, out, bool[], return bool, struct field)
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_BoolAllPositions_MatchesOracle() =>
        DifferentialRunner.Run(s_boolAllPositions);

    // char across all 4 positions (value, in, ref, out, char[], return char, struct field; #971 on Native)
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_CharAllPositions_MatchesOracle() =>
        DifferentialRunner.Run(s_charAllPositions);

    // Nullable<T> (H == D: int?, byte?, char?, bool?) in array, return value, and struct fields
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_NullableBlitPositions_MatchesOracle() =>
        DifferentialRunner.Run(s_nullableBlitPositions);

    // Nullable<T> scalar/byref parameter (#961 throws MarshalDirectiveException on Native CPU)
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_NullableBlitScalarArg_MatchesOracleExceptKnownNativeIssue() =>
        DifferentialRunner.Run(s_nullableBlitScalarArg);

    // Nullable<Vector2> (H != D, #961 12B host vs 16B CUDA layout divergence)
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_NullableConvertPositions_MatchesOracleExceptKnownCudaIssue() =>
        DifferentialRunner.Run(s_nullableConvertPositions);

    // LayoutKind.Explicit (non-overlapping with gaps & Size=24, plus identical bit union)
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_ExplicitLayoutAndUnionPositions_MatchesOracle() =>
        DifferentialRunner.Run(s_explicitLayoutAndUnionPositions);

    // LayoutKind.Explicit union containing a Convert arm (#1006 on CUDA)
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_ExplicitUnionWithConvertArm_MatchesOracleExceptKnownCudaIssue() =>
        DifferentialRunner.Run(s_explicitUnionWithConvertArm);

    // LayoutKind.Auto across value, in, ref, out, array, return value, and nested struct
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_AutoLayoutAllPositions_MatchesOracle() =>
        DifferentialRunner.Run(s_autoLayoutAllPositions);

    // [InlineArray(3)] (bool, char, DiffVec2Padded) + fixed buffer FixedBufferPacket across 4 positions
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_InlineArrayAndFixedBufferPositions_MatchesOracle() =>
        DifferentialRunner.Run(s_inlineArrayAndFixedBufferPositions);

    // [InlineArray(2)] of Vector2? (#961 block-copy corruption on CUDA)
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_InlineArrayNullableVec2_MatchesOracleExceptKnownCudaIssue() =>
        DifferentialRunner.Run(s_inlineArrayNullableVec2);

    // Vector2/3/4 alignment torture + InnerPair/OuterPacket + External assembly structs (DeviceLibrary)
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_VectorAlignmentAndExternalStructs_MatchesOracle() =>
        DifferentialRunner.Run(s_vectorAlignmentAndExternalStructs);

    // Multiple ref/in/out scalars + raw pointer (valid & null) + empty Span + [GpuConstexpr] + [GpuReadOnly]
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_MultiRefScalarAndRawPointer_MatchesOracle() =>
        DifferentialRunner.Run(s_multiRefScalarAndRawPointer);
}
