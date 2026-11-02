#nullable enable
using System;
using System.Threading.Tasks;
using Kassyi.GSharp.Core;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for Lane D5 Stage 2: integer division/remainder by zero (<c>/</c>, <c>%</c>, <c>/=</c>, <c>%=</c>),
/// <c>int.MinValue / -1</c> and <c>long.MinValue / -1L</c> overflow vs narrow <c>short.MinValue / (short)-1</c> promotion,
/// <c>Math.Abs(int.MinValue)</c> (#947), shift-count masking (#722), <c>unchecked</c> wrap-around + IEEE 754 float
/// division by zero + float-to-integer saturation (#724, #890), and CPU 2-path <c>checked</c> arithmetic/narrowing
/// overflow (#723 / #718).
/// </summary>
public class IntegerTrapAndCheckedDifferentialTests
{
    private const int DefaultRows = 64;

    private static readonly long[] s_int64Boundaries =
    [
        0L, 1L, -1L, 2L, -2L, long.MaxValue, long.MinValue, long.MaxValue - 1L, long.MinValue + 1L,
        0x7FFF_FFFFL, -0x8000_0000L, 0x1_0000_0000L, -0x1_0000_0000L,
    ];

    private static readonly ulong[] s_uint64Boundaries =
    [
        0UL, 1UL, 2UL, 0x7FFF_FFFFUL, 0x8000_0000UL, 0xFFFF_FFFFUL, 0x1_0000_0000UL,
        ulong.MaxValue - 1UL, ulong.MaxValue,
    ];

    private static readonly double[] s_doubleBoundaries =
    [
        0.0, -0.0, 1.0, -1.0, 0.5, -0.5, double.MaxValue, double.MinValue,
        double.PositiveInfinity, double.NegativeInfinity, double.NaN,
        2147483647.0, 2147483648.0, -2147483648.0, -2147483649.0,
        4294967295.0, 4294967296.0, 9.223372036854776e18, -9.223372036854776e18,
    ];

    // I-01: Integer division and remainder by zero (Seed 0 across all 4 paths; Seeds 1..4 DivideByZeroException on CPU 2-path).
    private static readonly DifferentialCase s_integerDivideByZero = new(
        $"{nameof(IntegerTrapAndCheckedDifferentialKernels)}.{nameof(IntegerTrapAndCheckedDifferentialKernels.IntegerDivideByZeroKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1, 2, 3, 4],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        ExceptionPaths = DiffPath.Managed,
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            int[] nonZeroIntDenoms = [1, -1, 2, -2, 3, -7, 16, 31, 127, -128, int.MaxValue, int.MinValue];
            uint[] nonZeroUIntDenoms = [1u, 2u, 3u, 7u, 16u, 255u, 65535u, uint.MaxValue];
            short[] nonZeroShortDenoms = [1, -1, 2, -2, 7, -13, 127, -127];

            var iNums = DiffValues.Int32(rng, rows);
            var iDenoms = new int[rows];
            var iRemDenoms = new int[rows];
            var uNums = new uint[rows];
            var uDenoms = new uint[rows];
            var lNums = new long[rows];
            var lDenoms = new long[rows];
            var sNums = DiffValues.Int16(rng, rows);
            var sDenoms = new short[rows];

            for (int k = 0; k < rows; k++)
            {
                int d = nonZeroIntDenoms[k % nonZeroIntDenoms.Length];
                if (iNums[k] == int.MinValue && d == -1)
                {
                    d = 1;
                }

                iDenoms[k] = d;
                iRemDenoms[k] = d;
                uNums[k] = (uint)iNums[k];
                uDenoms[k] = nonZeroUIntDenoms[k % nonZeroUIntDenoms.Length];
                lNums[k] = s_int64Boundaries[k % s_int64Boundaries.Length];
                long ld = nonZeroIntDenoms[(k + 3) % nonZeroIntDenoms.Length];
                if (lNums[k] == long.MinValue && ld == -1L)
                {
                    ld = 1L;
                }

                lDenoms[k] = ld;
                sDenoms[k] = nonZeroShortDenoms[k % nonZeroShortDenoms.Length];
            }

            switch (rng.Seed)
            {
                case 1:
                    iDenoms[5] = 0;
                    break;
                case 2:
                    iRemDenoms[11] = 0;
                    break;
                case 3:
                    uDenoms[19] = 0u;
                    break;
                case 4:
                    lDenoms[27] = 0L;
                    sDenoms[27] = 0;
                    break;
            }

            return new DiffBuffers()
                .In("iNums", iNums, readOnly: true)
                .In("iDenoms", iDenoms, readOnly: true)
                .In("iRemDenoms", iRemDenoms, readOnly: true)
                .In("uNums", uNums, readOnly: true)
                .In("uDenoms", uDenoms, readOnly: true)
                .In("lNums", lNums, readOnly: true)
                .In("lDenoms", lDenoms, readOnly: true)
                .In("sNums", sNums, readOnly: true)
                .In("sDenoms", sDenoms, readOnly: true)
                .Out<int>("dst", rows, IntegerTrapAndCheckedDifferentialKernels.DivideByZeroColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.LaunchIntegerDivideByZeroKernel(
            b.Get<int>("iNums"),
            b.Get<int>("iDenoms"),
            b.Get<int>("iRemDenoms"),
            b.Get<uint>("uNums"),
            b.Get<uint>("uDenoms"),
            b.Get<long>("lNums"),
            b.Get<long>("lDenoms"),
            b.Get<short>("sNums"),
            b.Get<short>("sDenoms"),
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.IntegerDivideByZeroKernel(
            b.Get<int>("iNums"),
            b.Get<int>("iDenoms"),
            b.Get<int>("iRemDenoms"),
            b.Get<uint>("uNums"),
            b.Get<uint>("uDenoms"),
            b.Get<long>("lNums"),
            b.Get<long>("lDenoms"),
            b.Get<short>("sNums"),
            b.Get<short>("sDenoms"),
            b.Get<int>("dst"),
            s.Rows),
    };

    // I-02: Signed MinValue / -1 and MinValue % -1 (Seed 0 verifies non-trapping boundaries & short/sbyte promotion on all 4 paths;
    // Seeds 1..4 verify OverflowException on CPU 2-path).
    private static readonly DifferentialCase s_signedMinValueDivideByMinusOne = new(
        $"{nameof(IntegerTrapAndCheckedDifferentialKernels)}.{nameof(IntegerTrapAndCheckedDifferentialKernels.SignedMinValueDivideByMinusOneKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1, 2, 3, 4],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        ExceptionPaths = DiffPath.Managed,
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            (int Num, int Den)[] safeIntPairs =
            [
                (int.MinValue, 1),
                (int.MinValue, 2),
                (int.MinValue + 1, -1),
                (int.MaxValue, -1),
                (-1, -1),
                (0, -1),
            ];
            (long Num, long Den)[] safeLongPairs =
            [
                (long.MinValue, 1L),
                (long.MinValue, 2L),
                (long.MinValue + 1L, -1L),
                (long.MaxValue, -1L),
                (-1L, -1L),
                (0L, -1L),
            ];

            var iNums = new int[rows];
            var iDivDenoms = new int[rows];
            var iRemDenoms = new int[rows];
            var lNums = new long[rows];
            var lDivDenoms = new long[rows];
            var lRemDenoms = new long[rows];
            var sVals = new short[rows];
            var sbVals = new sbyte[rows];

            for (int k = 0; k < rows; k++)
            {
                var ip = safeIntPairs[k % safeIntPairs.Length];
                iNums[k] = ip.Num;
                iDivDenoms[k] = ip.Den;
                iRemDenoms[k] = ip.Den;

                var lp = safeLongPairs[k % safeLongPairs.Length];
                lNums[k] = lp.Num;
                lDivDenoms[k] = lp.Den;
                lRemDenoms[k] = lp.Den;

                sVals[k] = (k & 1) == 0 ? short.MinValue : short.MaxValue;
                sbVals[k] = (k & 1) == 0 ? sbyte.MinValue : sbyte.MaxValue;
            }

            switch (rng.Seed)
            {
                case 1:
                    // int.MinValue / -1 throws OverflowException even in unchecked context.
                    iNums[3] = int.MinValue;
                    iDivDenoms[3] = -1;
                    break;
                case 2:
                    // int.MinValue % -1 also throws OverflowException in C# (ECMA-334 §12.10.3).
                    iNums[9] = int.MinValue;
                    iRemDenoms[9] = -1;
                    break;
                case 3:
                    // long.MinValue / -1L throws OverflowException.
                    lNums[17] = long.MinValue;
                    lDivDenoms[17] = -1L;
                    break;
                case 4:
                    // long.MinValue % -1L throws OverflowException.
                    lNums[29] = long.MinValue;
                    lRemDenoms[29] = -1L;
                    break;
            }

            return new DiffBuffers()
                .In("iNums", iNums, readOnly: true)
                .In("iDivDenoms", iDivDenoms, readOnly: true)
                .In("iRemDenoms", iRemDenoms, readOnly: true)
                .In("lNums", lNums, readOnly: true)
                .In("lDivDenoms", lDivDenoms, readOnly: true)
                .In("lRemDenoms", lRemDenoms, readOnly: true)
                .In("sVals", sVals, readOnly: true)
                .In("sbVals", sbVals, readOnly: true)
                .Out<int>("dst", rows, IntegerTrapAndCheckedDifferentialKernels.SignedMinDivColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.LaunchSignedMinValueDivideByMinusOneKernel(
            b.Get<int>("iNums"),
            b.Get<int>("iDivDenoms"),
            b.Get<int>("iRemDenoms"),
            b.Get<long>("lNums"),
            b.Get<long>("lDivDenoms"),
            b.Get<long>("lRemDenoms"),
            b.Get<short>("sVals"),
            b.Get<sbyte>("sbVals"),
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.SignedMinValueDivideByMinusOneKernel(
            b.Get<int>("iNums"),
            b.Get<int>("iDivDenoms"),
            b.Get<int>("iRemDenoms"),
            b.Get<long>("lNums"),
            b.Get<long>("lDivDenoms"),
            b.Get<long>("lRemDenoms"),
            b.Get<short>("sVals"),
            b.Get<sbyte>("sbVals"),
            b.Get<int>("dst"),
            s.Rows),
    };

    // I-03: Math.Abs(int.MinValue) throws OverflowException in C# (#947).
    private static readonly DifferentialCase s_mathAbsMinValue = new(
        $"{nameof(IntegerTrapAndCheckedDifferentialKernels)}.{nameof(IntegerTrapAndCheckedDifferentialKernels.MathAbsMinValueKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        ExceptionPaths = DiffPath.Managed | DiffPath.Native,
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            int[] safeIntVals = [0, 1, -1, 2, -2, int.MaxValue, int.MinValue + 1, -32768, -128];
            var iVals = new int[rows];
            var fVals = DiffValues.Single(rng, rows);
            var dVals = new double[rows];

            for (int k = 0; k < rows; k++)
            {
                iVals[k] = safeIntVals[k % safeIntVals.Length];
                dVals[k] = s_doubleBoundaries[k % s_doubleBoundaries.Length];
            }

            if (rng.Seed == 1)
            {
                iVals[7] = int.MinValue;
            }

            return new DiffBuffers()
                .In("iVals", iVals, readOnly: true)
                .In("fVals", fVals, readOnly: true)
                .In("dVals", dVals, readOnly: true)
                .Out<int>("iDst", rows, IntegerTrapAndCheckedDifferentialKernels.MathAbsIntColumns, OutputPolicy.Exact)
                .Out<float>("fDst", rows, IntegerTrapAndCheckedDifferentialKernels.MathAbsFloatColumns, OutputPolicy.Ulp(0))
                .Out<double>("dDst", rows, IntegerTrapAndCheckedDifferentialKernels.MathAbsDoubleColumns, OutputPolicy.Ulp(0));
        },
        Launch = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.LaunchMathAbsMinValueKernel(
            b.Get<int>("iVals"),
            b.Get<float>("fVals"),
            b.Get<double>("dVals"),
            b.Get<int>("iDst"),
            b.Get<float>("fDst"),
            b.Get<double>("dDst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.MathAbsMinValueKernel(
            b.Get<int>("iVals"),
            b.Get<float>("fVals"),
            b.Get<double>("dVals"),
            b.Get<int>("iDst"),
            b.Get<float>("fDst"),
            b.Get<double>("dDst"),
            s.Rows),
    };

    // I-04: Shift count masking across negative counts and counts >= width on all 4 paths (#722).
    private static readonly DifferentialCase s_shiftCountMaskBoundaryMatrix = new(
        $"{nameof(IntegerTrapAndCheckedDifferentialKernels)}.{nameof(IntegerTrapAndCheckedDifferentialKernels.ShiftCountMaskBoundaryMatrixKernel)}")
    {
        Shape = LaunchShapes.PartialWarp(74, 37),
        ExtraShapesSeed0Only =
        [
            LaunchShapes.Linear(64, 32),
        ],
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            int[] shiftCounts = [-1, -31, -32, -63, -64, 0, 1, 7, 15, 16, 31, 32, 33, 63, 64, 65, int.MinValue, int.MaxValue];
            var iVals = DiffValues.Int32(rng, rows);
            var uVals = new uint[rows];
            var lVals = new long[rows];
            var ulVals = new ulong[rows];
            var sVals = DiffValues.Int16(rng, rows);
            var sbVals = DiffValues.SByte(rng, rows);
            var counts = new int[rows];

            for (int k = 0; k < rows; k++)
            {
                uVals[k] = (uint)iVals[k];
                lVals[k] = rng.IsBoundarySeed
                    ? s_int64Boundaries[k % s_int64Boundaries.Length]
                    : ((long)rng.NextInt32() << 32) | rng.NextUInt32();
                ulVals[k] = rng.IsBoundarySeed
                    ? s_uint64Boundaries[k % s_uint64Boundaries.Length]
                    : (ulong)lVals[k];
                counts[k] = rng.IsBoundarySeed
                    ? shiftCounts[k % shiftCounts.Length]
                    : (rng.NextBool() ? shiftCounts[rng.NextBelow(shiftCounts.Length)] : rng.NextInt32());
            }

            return new DiffBuffers()
                .In("iVals", iVals, readOnly: true)
                .In("uVals", uVals, readOnly: true)
                .In("lVals", lVals, readOnly: true)
                .In("ulVals", ulVals, readOnly: true)
                .In("sVals", sVals, readOnly: true)
                .In("sbVals", sbVals, readOnly: true)
                .In("counts", counts, readOnly: true)
                .Out<int>("dst", rows, IntegerTrapAndCheckedDifferentialKernels.ShiftMaskBoundaryColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.LaunchShiftCountMaskBoundaryMatrixKernel(
            b.Get<int>("iVals"),
            b.Get<uint>("uVals"),
            b.Get<long>("lVals"),
            b.Get<ulong>("ulVals"),
            b.Get<short>("sVals"),
            b.Get<sbyte>("sbVals"),
            b.Get<int>("counts"),
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.ShiftCountMaskBoundaryMatrixKernel(
            b.Get<int>("iVals"),
            b.Get<uint>("uVals"),
            b.Get<long>("lVals"),
            b.Get<ulong>("ulVals"),
            b.Get<short>("sVals"),
            b.Get<sbyte>("sbVals"),
            b.Get<int>("counts"),
            b.Get<int>("dst"),
            s.Rows),
    };

    // I-05: Unchecked arithmetic wrap-around, float/double division by zero, and float-to-integer saturation on all 4 paths.
    private static readonly DifferentialCase s_uncheckedOverflowAndFloatSaturationMatrix = new(
        $"{nameof(IntegerTrapAndCheckedDifferentialKernels)}.{nameof(IntegerTrapAndCheckedDifferentialKernels.UncheckedOverflowAndFloatSaturationMatrixKernel)}")
    {
        Shape = LaunchShapes.PartialWarp(74, 37),
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var (iLeft, iRight) = DiffValues.Int32Pairs(rng, rows);
            var fVals = DiffValues.Single(rng, rows);
            var dVals = new double[rows];
            for (int k = 0; k < rows; k++)
            {
                dVals[k] = rng.IsBoundarySeed
                    ? s_doubleBoundaries[k % s_doubleBoundaries.Length]
                    : (rng.NextBool() ? s_doubleBoundaries[rng.NextBelow(s_doubleBoundaries.Length)] : fVals[k]);
            }

            return new DiffBuffers()
                .In("iLeft", iLeft, readOnly: true)
                .In("iRight", iRight, readOnly: true)
                .In("fVals", fVals, readOnly: true)
                .In("dVals", dVals, readOnly: true)
                .Out<int>("dst", rows, IntegerTrapAndCheckedDifferentialKernels.UncheckedAndFloatSaturationColumns, OutputPolicy.Exact)
                .Out<float>("fDst", rows, IntegerTrapAndCheckedDifferentialKernels.FloatDivZeroColumns, OutputPolicy.Ulp(0))
                .Out<double>("dDst", rows, IntegerTrapAndCheckedDifferentialKernels.DoubleDivZeroColumns, OutputPolicy.Ulp(0));
        },
        Launch = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.LaunchUncheckedOverflowAndFloatSaturationMatrixKernel(
            b.Get<int>("iLeft"),
            b.Get<int>("iRight"),
            b.Get<float>("fVals"),
            b.Get<double>("dVals"),
            b.Get<int>("dst"),
            b.Get<float>("fDst"),
            b.Get<double>("dDst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => IntegerTrapAndCheckedDifferentialKernels.UncheckedOverflowAndFloatSaturationMatrixKernel(
            b.Get<int>("iLeft"),
            b.Get<int>("iRight"),
            b.Get<float>("fVals"),
            b.Get<double>("dVals"),
            b.Get<int>("dst"),
            b.Get<float>("fDst"),
            b.Get<double>("dDst"),
            s.Rows),
    };

    // I-06: C# checked arithmetic overflow (+, -, *, -x, ++, --, +=, -=, *=) evaluated across SequentialOracle (3 orders)
    // and parallel CPU Managed execution (transpiler emission pending #723 / #718).
    private static void CheckedArithmeticStep(
        int t,
        int[] modes,
        int[] iLeft,
        int[] iRight,
        uint[] uLeft,
        uint[] uRight,
        long[] lLeft,
        long[] lRight,
        short[] sVals,
        byte[] bVals,
        int[] dst)
    {
        int mode = modes[t];
        int a = iLeft[t];
        int b = iRight[t];
        uint ua = uLeft[t];
        uint ub = uRight[t];
        long la = lLeft[t];
        long lb = lRight[t];
        short sv = sVals[t];
        byte bv = bVals[t];

        int res = mode switch
        {
            0 => checked(a + b),
            1 => checked(a - b),
            2 => checked(a * b),
            3 => checked(-a),
            4 => checked((int)(ua - ub)),
            5 => checked((int)(la + lb)),
            6 => EvalCheckedShortIncrement(sv),
            7 => EvalCheckedByteIncrement(bv),
            _ => EvalCheckedCompoundMul(a, b),
        };

        dst[t] = res;

        static int EvalCheckedShortIncrement(short v)
        {
            checked
            {
                v++;
                return v;
            }
        }

        static int EvalCheckedByteIncrement(byte v)
        {
            checked
            {
                v++;
                return v;
            }
        }

        static int EvalCheckedCompoundMul(int x, int y)
        {
            checked
            {
                x *= y;
                return x;
            }
        }
    }

    private static readonly DifferentialCase s_checkedArithmeticOverflowCpuManaged = new(
        "IntegerTrapAndCheckedDifferentialTests.CheckedArithmeticOverflowCpuManaged")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1, 2, 3, 4, 5, 6, 7, 8],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        ExceptionPaths = DiffPath.Managed,
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var modes = new int[rows];
            var iLeft = new int[rows];
            var iRight = new int[rows];
            var uLeft = new uint[rows];
            var uRight = new uint[rows];
            var lLeft = new long[rows];
            var lRight = new long[rows];
            var sVals = new short[rows];
            var bVals = new byte[rows];

            for (int k = 0; k < rows; k++)
            {
                modes[k] = k % 9;
                iLeft[k] = (k * 17) - 100;
                iRight[k] = (k * 3) - 20;
                uLeft[k] = (uint)(k + 10);
                uRight[k] = (uint)k;
                lLeft[k] = k * 100L;
                lRight[k] = k * 10L;
                sVals[k] = (short)(k - 32);
                bVals[k] = (byte)(k & 0x7F);
            }

            switch (rng.Seed)
            {
                case 1:
                    modes[0] = 0;
                    iLeft[0] = int.MaxValue;
                    iRight[0] = 1;
                    break;
                case 2:
                    modes[1] = 1;
                    iLeft[1] = int.MinValue;
                    iRight[1] = 1;
                    break;
                case 3:
                    modes[2] = 2;
                    iLeft[2] = 65536;
                    iRight[2] = 65536;
                    break;
                case 4:
                    modes[3] = 3;
                    iLeft[3] = int.MinValue;
                    break;
                case 5:
                    modes[4] = 4;
                    uLeft[4] = 0u;
                    uRight[4] = 1u;
                    break;
                case 6:
                    modes[5] = 5;
                    lLeft[5] = long.MaxValue;
                    lRight[5] = 1L;
                    break;
                case 7:
                    modes[6] = 6;
                    sVals[6] = short.MaxValue;
                    break;
                case 8:
                    modes[7] = 7;
                    bVals[7] = byte.MaxValue;
                    break;
            }

            return new DiffBuffers()
                .In("modes", modes, readOnly: true)
                .In("iLeft", iLeft, readOnly: true)
                .In("iRight", iRight, readOnly: true)
                .In("uLeft", uLeft, readOnly: true)
                .In("uRight", uRight, readOnly: true)
                .In("lLeft", lLeft, readOnly: true)
                .In("lRight", lRight, readOnly: true)
                .In("sVals", sVals, readOnly: true)
                .In("bVals", bVals, readOnly: true)
                .Out<int>("dst", rows, ["checked_arith"], OutputPolicy.Exact);
        },
        Launch = static (b, s) =>
        {
            var modes = b.Get<int>("modes");
            var iLeft = b.Get<int>("iLeft");
            var iRight = b.Get<int>("iRight");
            var uLeft = b.Get<uint>("uLeft");
            var uRight = b.Get<uint>("uRight");
            var lLeft = b.Get<long>("lLeft");
            var lRight = b.Get<long>("lRight");
            var sVals = b.Get<short>("sVals");
            var bVals = b.Get<byte>("bVals");
            var dst = b.Get<int>("dst");
            for (int t = 0; t < s.Rows; t++)
            {
                CheckedArithmeticStep(t, modes, iLeft, iRight, uLeft, uRight, lLeft, lRight, sVals, bVals, dst);
            }
        },
        Oracle = static (b, s) =>
        {
            int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
            if (t >= s.Rows)
            {
                return;
            }

            CheckedArithmeticStep(
                t,
                b.Get<int>("modes"),
                b.Get<int>("iLeft"),
                b.Get<int>("iRight"),
                b.Get<uint>("uLeft"),
                b.Get<uint>("uRight"),
                b.Get<long>("lLeft"),
                b.Get<long>("lRight"),
                b.Get<short>("sVals"),
                b.Get<byte>("bVals"),
                b.Get<int>("dst"));
        },
    };

    // I-07: C# checked narrowing conversions (integral and float/double -> integer including NaN / ±Inf) evaluated across
    // SequentialOracle and parallel CPU Managed execution (transpiler emission pending #723 / #718).
    private static void CheckedNarrowingStep(
        int t,
        int[] modes,
        int[] iVals,
        long[] lVals,
        float[] fVals,
        double[] dVals,
        int[] dst)
    {
        int mode = modes[t];
        int iv = iVals[t];
        long lv = lVals[t];
        float fv = fVals[t];
        double dv = dVals[t];

        int res = mode switch
        {
            0 => checked((byte)iv),
            1 => checked((sbyte)iv),
            2 => checked((short)iv),
            3 => checked((ushort)iv),
            4 => unchecked((int)checked((uint)iv)),
            5 => checked((int)lv),
            6 => checked((int)fv),
            _ => checked((int)dv),
        };

        dst[t] = res;
    }

    private static readonly DifferentialCase s_checkedNarrowingCastCpuManaged = new(
        "IntegerTrapAndCheckedDifferentialTests.CheckedNarrowingCastCpuManaged")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1, 2, 3, 4, 5, 6, 7],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        ExceptionPaths = DiffPath.Managed,
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var modes = new int[rows];
            var iVals = new int[rows];
            var lVals = new long[rows];
            var fVals = new float[rows];
            var dVals = new double[rows];

            for (int k = 0; k < rows; k++)
            {
                modes[k] = k % 8;
                iVals[k] = k % 120;
                lVals[k] = (k * 1000L) - 5000L;
                fVals[k] = (k * 10.5f) - 100.25f;
                dVals[k] = (k * 100.75) - 1000.5;
            }

            switch (rng.Seed)
            {
                case 1:
                    modes[0] = 0;
                    iVals[0] = 256;
                    break;
                case 2:
                    modes[1] = 1;
                    iVals[1] = -129;
                    break;
                case 3:
                    modes[4] = 4;
                    iVals[4] = -1;
                    break;
                case 4:
                    modes[5] = 5;
                    lVals[5] = (long)int.MaxValue + 1L;
                    break;
                case 5:
                    modes[6] = 6;
                    fVals[6] = float.NaN;
                    break;
                case 6:
                    modes[6] = 6;
                    fVals[6] = 2147483648.0f;
                    break;
                case 7:
                    modes[7] = 7;
                    dVals[7] = double.NegativeInfinity;
                    break;
            }

            return new DiffBuffers()
                .In("modes", modes, readOnly: true)
                .In("iVals", iVals, readOnly: true)
                .In("lVals", lVals, readOnly: true)
                .In("fVals", fVals, readOnly: true)
                .In("dVals", dVals, readOnly: true)
                .Out<int>("dst", rows, ["checked_narrow"], OutputPolicy.Exact);
        },
        Launch = static (b, s) =>
        {
            var modes = b.Get<int>("modes");
            var iVals = b.Get<int>("iVals");
            var lVals = b.Get<long>("lVals");
            var fVals = b.Get<float>("fVals");
            var dVals = b.Get<double>("dVals");
            var dst = b.Get<int>("dst");
            for (int t = 0; t < s.Rows; t++)
            {
                CheckedNarrowingStep(t, modes, iVals, lVals, fVals, dVals, dst);
            }
        },
        Oracle = static (b, s) =>
        {
            int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
            if (t >= s.Rows)
            {
                return;
            }

            CheckedNarrowingStep(
                t,
                b.Get<int>("modes"),
                b.Get<int>("iVals"),
                b.Get<long>("lVals"),
                b.Get<float>("fVals"),
                b.Get<double>("dVals"),
                b.Get<int>("dst"));
        },
    };

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_IntegerDivideByZero_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_integerDivideByZero);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_SignedMinValueDivideByMinusOne_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_signedMinValueDivideByMinusOne);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_MathAbsMinValue_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_mathAbsMinValue);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_ShiftCountMaskBoundaryMatrix_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_shiftCountMaskBoundaryMatrix);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_UncheckedOverflowAndFloatSaturationMatrix_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_uncheckedOverflowAndFloatSaturationMatrix);

    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_CheckedArithmeticOverflow_MatchesTheOracleOnCpuManaged()
    {
        var report = DifferentialRunner.Evaluate(
            s_checkedArithmeticOverflowCpuManaged,
            s_checkedArithmeticOverflowCpuManaged.Seeds!,
            [new DiffPathRunner(DiffPath.Managed, DiffPaths.DirectRunner)],
            KnownDivergences.All);
        Assert.False(report.HasFailures, report.ToString());
    }

    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_CheckedNarrowingCastAndFloatOverflow_MatchesTheOracleOnCpuManaged()
    {
        var report = DifferentialRunner.Evaluate(
            s_checkedNarrowingCastCpuManaged,
            s_checkedNarrowingCastCpuManaged.Seeds!,
            [new DiffPathRunner(DiffPath.Managed, DiffPaths.DirectRunner)],
            KnownDivergences.All);
        Assert.False(report.HasFailures, report.ToString());
    }
}
