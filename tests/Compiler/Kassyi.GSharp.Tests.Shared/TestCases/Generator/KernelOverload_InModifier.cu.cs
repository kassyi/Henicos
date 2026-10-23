using Kassyi.GSharp.Core;
namespace OverloadTest
{
    public static partial class InModifierKernels
    {
        // C# tells the overloads apart by in, but Launch{Name} drops in, so both take one int
        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void Run(int[] data, int value) { data[ThreadIndex.X] = value; }

        [GpuKernel]
        public static void Run(int[] data, in int value) { data[ThreadIndex.X] = value + 1; }
    }
}
