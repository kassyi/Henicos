namespace Kassyi.GSharp.Tests.Shared.Syntax;

public class MathFloorCeilingTests : TranspilerTestBase
{
    // negative, positive, exact integers, +-0.5 boundaries, values just beside an integer, zero
    private static readonly double[] s_values =
    [
        -2.5, -1.5, -1.0, -0.5, -0.25, 0.0, 0.25, 0.5, 1.0, 1.5, 2.5, 2.7, -2.7, 1e10 + 0.5, -1e10 - 0.5,
    ];

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void FloorAndCeiling_MatchCSharp()
    {
        int n = s_values.Length;
        double[] doubleInputs = s_values;
        float[] floatInputs = new float[n];
        for (int i = 0; i < n; i++)
        {
            floatInputs[i] = (float)s_values[i];
        }

        double[] doubleFloor = new double[n];
        double[] doubleCeiling = new double[n];
        float[] floatFloor = new float[n];
        float[] floatCeiling = new float[n];

        MathFloorCeilingKernels.LaunchFloorCeilingKernel(
            doubleInputs, doubleFloor, doubleCeiling, floatInputs, floatFloor, floatCeiling, blockDimX: (uint)n);

        for (int i = 0; i < n; i++)
        {
            Assert.Equal(Math.Floor(doubleInputs[i]), doubleFloor[i]);
            Assert.Equal(Math.Ceiling(doubleInputs[i]), doubleCeiling[i]);
            Assert.Equal(MathF.Floor(floatInputs[i]), floatFloor[i]);
            Assert.Equal(MathF.Ceiling(floatInputs[i]), floatCeiling[i]);
        }
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Pow_MatchesCSharp()
    {
        double[] bases = [2.0, 3.0, 10.0, 0.5, 9.0, 1.5];
        double[] exponents = [10.0, 3.0, -2.0, 4.0, 0.5, 2.0];
        int n = bases.Length;
        float[] floatBases = new float[n];
        float[] floatExponents = new float[n];
        for (int i = 0; i < n; i++)
        {
            floatBases[i] = (float)bases[i];
            floatExponents[i] = (float)exponents[i];
        }

        double[] doubleResults = new double[n];
        float[] floatResults = new float[n];

        MathFloorCeilingKernels.LaunchPowKernel(
            bases, exponents, doubleResults, floatBases, floatExponents, floatResults, blockDimX: (uint)n);

        for (int i = 0; i < n; i++)
        {
            Assert.Equal(Math.Pow(bases[i], exponents[i]), doubleResults[i], precision: 9);
            Assert.Equal(MathF.Pow(floatBases[i], floatExponents[i]), floatResults[i], precision: 4);
        }
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void Round_RoundsHalfToEvenLikeCSharp()
    {
        // halfway cases of both parities and signs, values off the halfway, zero, and a value near 2^50 plus a half
        double[] values = [0.5, 1.5, 2.5, 3.5, -0.5, -1.5, -2.5, 2.4, 2.6, -2.6, 0.0, 1e15 + 0.5];
        int n = values.Length;
        float[] floatInputs = new float[n];
        for (int i = 0; i < n; i++)
        {
            floatInputs[i] = (float)values[i];
        }

        double[] doubleResults = new double[n];
        float[] floatResults = new float[n];

        MathFloorCeilingKernels.LaunchRoundKernel(values, doubleResults, floatInputs, floatResults, blockDimX: (uint)n);

        for (int i = 0; i < n; i++)
        {
            Assert.Equal(Math.Round(values[i]), doubleResults[i]);
            Assert.Equal(MathF.Round(floatInputs[i]), floatResults[i]);
        }

        // 2.5 must give 2 (half to even), not 3 (half away from zero)
        Assert.Equal(2.0, doubleResults[2]);
        Assert.Equal(2.0f, floatResults[2]);
    }
}
