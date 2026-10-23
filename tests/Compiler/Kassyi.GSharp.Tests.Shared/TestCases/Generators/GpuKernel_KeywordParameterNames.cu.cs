using Kassyi.GSharp.Core;

namespace TestNamespace.KeywordNames;

// Parameters named after C# keywords: the generated host wrapper has to escape them consistently
public static partial class KeywordNameKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Generator case")]
    public static void RefKeywordKernel(ref int @object)
    {
        @object = @object + 1;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Generator case")]
    public static void OutKeywordKernel(out int @event)
    {
        @event = 1;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Generator case")]
    public static void ArrayAndValueKeywordKernel(float[] @float, int @int)
    {
        int i = ThreadIndex.X;
        @float[i] = @float[i] * @int;
    }
}
