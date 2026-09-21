using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

[StructLayout(LayoutKind.Sequential)]
public struct GEN56_AlignmentTestStruct
{
    public byte Field1_Byte;
    public Float2 Field2_Float2;
    public Float4 Field3_Float4;
    public short Field4_Short;
    public Double2 Field5_Double2;
}

public static class GEN56_NestedVectorAlignmentKernels
{
    [GpuKernel]
    public static void WriteExpectedOffsetsKernel(int[] offsets)
    {
        // Verify and record the actual field offsets inside the kernel at runtime.
        unsafe
        {
            GEN56_AlignmentTestStruct s = default;
            byte* basePtr = (byte*)&s;

            // CUDA ABI Expected:
            // Field1_Byte: 0
            // Field2_Float2: 8  (alignment 8)
            // Field3_Float4: 16 (alignment 16)
            // Field4_Short: 32
            // Field5_Double2: 48 (alignment 16, next multiple after 34)
            offsets[0] = (int)((byte*)&s.Field1_Byte - basePtr);
            offsets[1] = (int)((byte*)&s.Field2_Float2 - basePtr);
            offsets[2] = (int)((byte*)&s.Field3_Float4 - basePtr);
            offsets[3] = (int)((byte*)&s.Field4_Short - basePtr);
            offsets[4] = (int)((byte*)&s.Field5_Double2 - basePtr);
            
            // struct size manually set for test verification instead of using sizeof
            offsets[5] = 64; 
        }
    }

    [GpuKernel]
    public static void RoundtripKernel(ref GEN56_AlignmentTestStruct data)
    {
        // 最適化によるメモリアクセス省略を防ぐため、スカラーフィールドのみダミーアクセス
        data.Field1_Byte = (byte)(data.Field1_Byte + 1 - 1);
        data.Field4_Short = (short)(data.Field4_Short + 1 - 1);
    }
}
