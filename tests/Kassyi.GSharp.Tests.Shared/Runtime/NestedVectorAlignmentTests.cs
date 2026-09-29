using System.Runtime.InteropServices;
using System.Linq;
using Kassyi.GSharp.Core;
using Xunit;
using Xunit.Abstractions;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

[Trait("Category", "GpuExecution")]
public class NestedVectorAlignmentTests
{
    private readonly ITestOutputHelper _output;

    public NestedVectorAlignmentTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Test_CUDA_ABI_Offsets()
    {
        // ジェネレーターが生成したワイヤ表現である `__Native_GEN56_AlignmentTestStruct` の
        // メモリオフセットが、CUDA ABI互換のサイズ/パディングになっているか検証する。
        // C#側の `LayoutKind.Explicit` と `[FieldOffset]` の計算が正しいかのテスト。
        
        var assembly = typeof(NestedVectorAlignmentKernels).Assembly;
        Type type = null;
        try
        {
            type = assembly.GetTypes().FirstOrDefault(t => t.Name == "GEN56_AlignmentTestStruct" && t.DeclaringType != null && t.DeclaringType.Name == "AotContexts");
        }
        catch (System.Reflection.ReflectionTypeLoadException ex)
        {
            type = ex.Types.FirstOrDefault(t => t != null && t.Name == "GEN56_AlignmentTestStruct" && t.DeclaringType != null && t.DeclaringType.Name == "AotContexts");
        }
        Assert.NotNull(type);

        var offset1 = Marshal.OffsetOf(type, "Field1_Byte").ToInt32();
        var offset2 = Marshal.OffsetOf(type, "Field2_Float2").ToInt32();
        var offset3 = Marshal.OffsetOf(type, "Field3_Float4").ToInt32();
        var offset4 = Marshal.OffsetOf(type, "Field4_Short").ToInt32();
        var offset5 = Marshal.OffsetOf(type, "Field5_Double2").ToInt32();
        var totalSize = Marshal.SizeOf(type);

        var debugField2 = type.GetField("DebugName_Field2_Float2", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        Assert.Equal(0, offset1);
        Assert.Equal(8, offset2);
        Assert.Equal(16, offset3);
        Assert.Equal(32, offset4);
        Assert.Equal(48, offset5);
        Assert.Equal(64, totalSize);
    }

    [Fact]
    public void Test_Roundtrip_Marshaling()
    {
        // ホスト側で値をセットし、デバイス側で書き換え、再度ホスト側で読むことで
        // 異なるメモリレイアウト間でのパディングのズレが吸収されているかを検証する
        var data = new GEN56_AlignmentTestStruct
        {
            Field1_Byte = 10,
            Field2_Float2 = new Float2(1.0f, 2.0f),
            Field3_Float4 = new Float4(3.0f, 4.0f, 5.0f, 6.0f),
            Field4_Short = 20,
            Field5_Double2 = new Double2(7.0, 8.0)
        };

        var offsets = new int[6];
        NestedVectorAlignmentKernels.LaunchWriteExpectedOffsetsKernel(offsets);

        NestedVectorAlignmentKernels.LaunchRoundtripKernel(ref data);

        Assert.Equal((byte)10, data.Field1_Byte);
        
        Assert.Equal(1.0f, data.Field2_Float2.X);
        Assert.Equal(2.0f, data.Field2_Float2.Y);

        Assert.Equal(3.0f, data.Field3_Float4.X);
        Assert.Equal(4.0f, data.Field3_Float4.Y);
        Assert.Equal(5.0f, data.Field3_Float4.Z);
        Assert.Equal(6.0f, data.Field3_Float4.W);

        Assert.Equal((short)20, data.Field4_Short);

        Assert.Equal(7.0, data.Field5_Double2.X);
        Assert.Equal(8.0, data.Field5_Double2.Y);
    }
}
