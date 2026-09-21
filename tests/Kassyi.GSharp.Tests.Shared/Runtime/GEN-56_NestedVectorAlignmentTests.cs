using System.Runtime.InteropServices;
using System.Linq;
using Kassyi.GSharp.Core;
using Xunit;
using Xunit.Abstractions;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

[Trait("Category", "GpuExecution")]
[Trait("FeatureId", "GEN-56")]
public class GEN56_NestedVectorAlignmentTests
{
    private readonly ITestOutputHelper _output;

    public GEN56_NestedVectorAlignmentTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Test_GEN56_CUDA_ABI_Offsets()
    {
        // ジェネレーターが生成したワイヤ表現である `__Native_GEN56_AlignmentTestStruct` の
        // メモリオフセットが、CUDA ABI互換のサイズ/パディングになっているか検証する。
        // C#側の `LayoutKind.Explicit` と `[FieldOffset]` の計算が正しいかのテスト。
        
        var assembly = typeof(GEN56_NestedVectorAlignmentKernelsWrapper).Assembly;
        Type type = null;
        try
        {
            type = assembly.GetTypes().FirstOrDefault(t => t.Name.Contains("__Native_") && t.Name.Contains("GEN56_AlignmentTestStruct"));
        }
        catch (System.Reflection.ReflectionTypeLoadException ex)
        {
            type = ex.Types.FirstOrDefault(t => t != null && t.Name.Contains("__Native_") && t.Name.Contains("GEN56_AlignmentTestStruct"));
        }
        Assert.NotNull(type);

        var offset1 = Marshal.OffsetOf(type, "Field1_Byte").ToInt32();
        var offset2 = Marshal.OffsetOf(type, "Field2_Float2").ToInt32();
        var offset3 = Marshal.OffsetOf(type, "Field3_Float4").ToInt32();
        var offset4 = Marshal.OffsetOf(type, "Field4_Short").ToInt32();
        var offset5 = Marshal.OffsetOf(type, "Field5_Double2").ToInt32();
        var totalSize = Marshal.SizeOf(type);

        _output.WriteLine($"Field1_Byte: {offset1}");
        _output.WriteLine($"Field2_Float2: {offset2}");
        _output.WriteLine($"Field3_Float4: {offset3}");
        _output.WriteLine($"Field4_Short: {offset4}");
        _output.WriteLine($"Field5_Double2: {offset5}");
        _output.WriteLine($"Struct Size: {totalSize}");

        var debugField2 = type.GetField("DebugName_Field2_Float2", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        if (debugField2 != null)
        {
            _output.WriteLine($"DebugName_Field2_Float2: {debugField2.GetValue(null)}");
        }

        Assert.Equal(0, offset1);
        Assert.Equal(8, offset2);
        Assert.Equal(16, offset3);
        Assert.Equal(32, offset4);
        Assert.Equal(48, offset5);
        Assert.Equal(64, totalSize);
    }

    [Fact]
    public void Test_GEN56_Roundtrip_Marshaling()
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
        GEN56_NestedVectorAlignmentKernelsWrapper.Launch_WriteExpectedOffsetsKernel(offsets);
        _output.WriteLine($"C++ Field1_Byte: {offsets[0]}");
        _output.WriteLine($"C++ Field2_Float2: {offsets[1]}");
        _output.WriteLine($"C++ Field3_Float4: {offsets[2]}");
        _output.WriteLine($"C++ Field4_Short: {offsets[3]}");
        _output.WriteLine($"C++ Field5_Double2: {offsets[4]}");
        _output.WriteLine($"C++ Struct Size: {offsets[5]}");

        GEN56_NestedVectorAlignmentKernelsWrapper.Launch_RoundtripKernel(ref data);

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
