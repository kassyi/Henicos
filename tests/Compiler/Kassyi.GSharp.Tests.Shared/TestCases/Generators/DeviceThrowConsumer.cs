using Kassyi.GSharp.Core;
using DeviceThrowCase;

// A kernel that calls the device function of DeviceThrowLibrary.cs, built as another assembly
namespace DeviceThrowImportCase
{
    public static partial class ThrowImportKernels
    {
        [GpuKernel]
        public static void CheckKernel(int[] data)
        {
            data[ThreadIndex.X] = LibThrow.Check(data[ThreadIndex.X]);
        }
    }
}
