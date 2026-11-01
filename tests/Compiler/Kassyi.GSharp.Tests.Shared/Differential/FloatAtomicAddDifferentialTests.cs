#nullable enable

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for <c>Gpu.AtomicAdd(ref float, float)</c> and <c>Gpu.ReductionAdd(ref float, float)</c> across
/// global memory, static shared memory, and dynamic shared memory (#868).
/// </summary>
public class FloatAtomicAddDifferentialTests
{
    private const uint CanonicalNaNBits = 0x7FFFFFFFu;

    // Boundary values covering positive/negative zeros, smallest/largest subnormals, subnormal pairs whose sum stays
    // subnormal or crosses into normal, normal values whose sum underflows into positive/negative subnormal, normal
    // numbers, infinities, and quiet/negative NaNs with payloads (#868, property-test-design §4).
    private static readonly float[] s_boundaries =
    [
        0f,
        -0f,
        BitConverter.UInt32BitsToSingle(0x00000001u), // +float.Epsilon (smallest positive subnormal, ~1.4e-45f)
        BitConverter.UInt32BitsToSingle(0x80000001u), // -float.Epsilon (smallest negative subnormal)
        BitConverter.UInt32BitsToSingle(0x006CE3EEu), // 1e-38f (subnormal; 1e-38f + 1e-38f is normal 2e-38f)
        BitConverter.UInt32BitsToSingle(0x806CE3EEu), // -1e-38f (negative subnormal; -1e-38f + -1e-38f is normal -2e-38f)
        BitConverter.UInt32BitsToSingle(0x007FFFFFu), // largest positive subnormal
        BitConverter.UInt32BitsToSingle(0x807FFFFFu), // largest negative subnormal
        BitConverter.UInt32BitsToSingle(0x00800000u), // smallest positive normal
        BitConverter.UInt32BitsToSingle(0x80800000u), // smallest negative normal
        BitConverter.UInt32BitsToSingle(0x00C00000u), // 1.5 * min_normal (adding -min_normal gives subnormal +0.5 * min_normal)
        BitConverter.UInt32BitsToSingle(0x80C00000u), // -1.5 * min_normal (adding +min_normal gives subnormal -0.5 * min_normal)
        1f,
        -1f,
        float.MaxValue,
        float.MinValue,
        float.PositiveInfinity,
        float.NegativeInfinity,
        BitConverter.UInt32BitsToSingle(0x7FC01234u), // positive quiet NaN with payload
        BitConverter.UInt32BitsToSingle(0xFFC05678u), // negative quiet NaN with payload
    ];

    private static readonly DifferentialCase s_floatAtomicAdd = new("FloatAtomicAddMatrixKernels.FloatAtomicAddMatrixKernel")
    {
        Shape = LaunchShape.ForRows(s_boundaries.Length * s_boundaries.Length, blockX: FloatAtomicAddMatrixKernels.StaticSharedCount),
        Inputs = static (rng, rows) =>
        {
            var (left, right) = Pairs(rng, rows);
            return new DiffBuffers()
                .In("left", left, OutputPolicy.Exact)
                .In("right", right, OutputPolicy.Exact)
                .Out<float>("r", rows, FloatAtomicAddMatrixKernels.Columns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => FloatAtomicAddMatrixKernels.LaunchFloatAtomicAddMatrixKernel(
            b.Get<float>("left"),
            b.Get<float>("right"),
            b.Get<float>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX,
            sharedMemBytes: s.BlockX * sizeof(float) * 2u
        ),
        Reference = static b =>
        {
            var left = b.Get<float>("left");
            var right = b.Get<float>("right");
            var r = b.Get<float>("r");
            for (int row = 0; row < left.Length; row++)
            {
                float a = left[row];
                float rhs = right[row];
                int o = row * 9;

                float globalSum = ReferenceGlobalAdd(a, rhs);
                r[o] = globalSum;
                r[o + 1] = a;
                r[o + 2] = globalSum;

                float sharedSum = ReferenceSharedAdd(a, rhs);
                r[o + 3] = sharedSum;
                r[o + 4] = a;
                r[o + 5] = sharedSum;

                r[o + 6] = sharedSum;
                r[o + 7] = a;
                r[o + 8] = sharedSum;
            }
        },
    };

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_FloatAtomicAdd_GlobalAndShared_MatchPtxOnEveryPath()
    {
        // Global float atomic add flushes subnormal inputs and results to sign-preserving zero and stores canonical NaN
        // (0x7FFFFFFF), while shared float atomic add (static and dynamic) preserves subnormal inputs and results and
        // also stores canonical NaN (0x7FFFFFFF). Both return the original un-flushed bits as the old value (#868).
        // mutant: Atomics.hpp / ManagedAtomics.cs flush subnormal in shared memory or skip flushing in global memory
        // mutant: Atomics.hpp / ManagedAtomics.cs return the IEEE NaN payload instead of 0x7FFFFFFF
        // mutant: Atomics.hpp / ManagedAtomics.cs return the flushed old value instead of the original bits
        DifferentialRunner.Run(s_floatAtomicAdd);
    }

    private static (float[] Left, float[] Right) Pairs(DiffRandom rng, int rows)
    {
        var left = new float[rows];
        var right = new float[rows];
        int n = s_boundaries.Length;
        for (int r = 0; r < rows; r++)
        {
            if (rng.IsBoundarySeed)
            {
                left[r] = s_boundaries[r / n % n];
                right[r] = s_boundaries[r % n];
            }
            else
            {
                left[r] = NextValue(rng);
                right[r] = NextValue(rng);
            }
        }

        return (left, right);
    }

    private static float NextValue(DiffRandom rng) =>
        rng.NextBool() ? s_boundaries[rng.NextBelow(s_boundaries.Length)] : DiffValues.NextSingle(rng);

    private static float ReferenceGlobalAdd(float old, float val)
    {
        if (float.IsNaN(old) || float.IsNaN(val))
        {
            return BitConverter.UInt32BitsToSingle(CanonicalNaNBits);
        }

        float sum = FlushSubnormal(old) + FlushSubnormal(val);
        return float.IsNaN(sum)
            ? BitConverter.UInt32BitsToSingle(CanonicalNaNBits)
            : FlushSubnormal(sum);
    }

    private static float ReferenceSharedAdd(float old, float val)
    {
        float sum = old + val;
        return float.IsNaN(sum)
            ? BitConverter.UInt32BitsToSingle(CanonicalNaNBits)
            : sum;
    }

    private static float FlushSubnormal(float value)
    {
        uint bits = BitConverter.SingleToUInt32Bits(value);
        uint abs = bits & 0x7FFFFFFFu;
        return abs != 0u && abs < 0x00800000u
            ? BitConverter.UInt32BitsToSingle(bits & 0x80000000u)
            : value;
    }
}
