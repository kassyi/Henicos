namespace Kassyi.GSharp.Tests.Syntax;

public class Tier1_IntegrationTests : TranspilerTestBase
{
    [SkippableFact]
    [Trait("Category", "GpuExecution")]
    [Trait("FeatureId", "Tier1")]
    public void Test_Tier1_AllInOneKernel_Execution()
    {
        const int count = 256;
        int[] intInput = new int[count];
        float[] floatInput = new float[count];
        float[] gpuResults = new float[count];
        ulong[] gpuCounter = new ulong[1];

        Tier1CustomStruct config = new Tier1CustomStruct
        {
            IntVal = 42,
            FloatVal = 10.0f,
            BoolVal = true
        };

        for (int i = 0; i < count; i++)
        {
            intInput[i] = i - 128; // Mix of positive and negative
            floatInput[i] = (float)Math.Sin(i);
        }

        // We can just execute the CPU JIT which sets the thread index properly!
        Tier1IntegrationKernelsWrapper.Launch_AllInOneKernel(
            intInput,
            floatInput,
            gpuResults,
            config,
            gpuCounter,
            count,
            count * sizeof(float), // dynamicSharedSize
            blockDimX: count,
            sharedMemBytes: (uint)(count * sizeof(float))
        );

        ulong expectedCounter = 0;
        for (int tid = 0; tid < count; tid++)
        {
            int a = tid;
            int b = tid * 2;

            int processed = intInput[tid] * 2;
            bool ok = intInput[tid] > 0;

            int bitwise = (a << 1) | (b >> 1) ^ 0xFF;
            bitwise &= ~1;

            float y = (float)processed;
            float mag = (float)Math.Max(floatInput[tid] * floatInput[tid], y * y);
            mag = Math.Abs(mag);
            mag = Math.Max(mag, config.FloatVal);

            mag += (tid % 3) switch
            {
                0 => 1.0f,
                1 => 2.0f,
                _ => 3.0f
            };

            float[] arr = new float[4];
            arr[0] = mag;
            arr[1] = mag * 2.0f;
            arr[2] = mag * 3.0f;
            arr[3] = mag * 4.0f;

            float sum = 0;
            sum += arr[0];
            sum += arr[2];
            sum += arr[3];

            float foreachSum = 0;
            foreach (var val in arr)
            {
                foreachSum += val;
            }

            sum += foreachSum;

            ReadOnlySpan<float> coll = [1.0f, 2.0f, 3.0f, 4.0f];
            float collSum = 0;
            foreach (var val in coll)
            {
                collSum += val;
            }

            sum += collSum + coll.Length;

            Span<float> stackBuf = stackalloc float[] { 5.0f, 10.0f, 15.0f, 20.0f };
            float stackSum = 0;
            foreach (var val in stackBuf)
            {
                stackSum += val;
            }

            sum += stackSum + stackBuf.Length;

            sum = sum * 2.0f;

            if (ok && config.BoolVal)
            {
                expectedCounter += (ulong)bitwise;
            }

            float sharedMemVal = sum;

            float finalVal = (bitwise > 0)
                ? (sharedMemVal > 10.0f ? 10.0f : sharedMemVal)
                : 0.0f;

            finalVal += bitwise;
            finalVal += 5; // actualVal

            int popCount = System.Numerics.BitOperations.PopCount((uint)bitwise);
            int leadingZeroes = System.Numerics.BitOperations.LeadingZeroCount((uint)bitwise);
            finalVal += popCount + leadingZeroes;

            int loopCounter = 0;
            do
            {
                if (loopCounter % 2 == 0)
                {
                    if (finalVal < 1000.0f)
                    {
                        finalVal += 1.0f;
                    }
                }
                else
                {
                    finalVal -= 0.5f;
                }

                loopCounter++;
            } while (loopCounter < 3);

            Assert.Equal(finalVal, gpuResults[tid], 0.0001f);
        }

        Assert.Equal(expectedCounter, gpuCounter[0]);
    }
}
