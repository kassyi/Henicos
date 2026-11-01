using System.Text.RegularExpressions;

namespace Kassyi.GSharp.Tests.Shared.Generator;

/// <summary>
/// The device definition of an explicit layout struct puts the largest member of each union group first: C++ aggregate
/// initialization (<c>S{}</c>) initializes only the first member of an anonymous union.
/// </summary>
[Category(TestCategory.Compiler)]
public class ExplicitUnionEmissionTests
{
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public void Test_UnionGroup_EmitsTheLargestMemberFirst(string dialect)
    {
        var source = RoslynTestUtility.ReadTestCase("Layout/ExplicitUnionOrder.cs");

        var (_, output, _) = RoslynTestUtility.RunGenerator(source, dialect);

        var generated = string.Concat(output.SyntaxTrees.Select(static tree => tree.ToString()));
        Assert.Matches(
            new Regex(@"union\{\r?\nlong long __gsharp_mb_L;\r?\nint __gsharp_mb_I;\r?\n\}"),
            generated);
    }

    // #1006: When an explicit-layout struct has an identical union group at one offset and a converted field at another
    // non-overlapping offset, Marshal_ and Unmarshal_ must copy only the widest member of the union offset group once,
    // regardless of field declaration order.
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public void Test_ConvertedExplicitStructWithIdenticalUnion_MarshalsOnlyTheWidestUnionMember(string dialect)
    {
        const string Source = """
            using System.Runtime.InteropServices;
            using Kassyi.GSharp.Core;

            [StructLayout(LayoutKind.Sequential, Size = 6)]
            public struct OddHead { public int A; }

            public struct ShiftedFields { public OddHead Head; public byte Tail; }

            [StructLayout(LayoutKind.Explicit)]
            public struct WideFirstConvertedExplicit
            {
                [FieldOffset(0)] public long Raw;
                [FieldOffset(0)] public int Low;
                [FieldOffset(8)] public ShiftedFields Converted;
            }

            [StructLayout(LayoutKind.Explicit)]
            public struct NarrowFirstConvertedExplicit
            {
                [FieldOffset(0)] public int Low;
                [FieldOffset(0)] public long Raw;
                [FieldOffset(8)] public ShiftedFields Converted;
            }

            public static partial class Kernels
            {
                [GpuKernel]
                public static WideFirstConvertedExplicit K1() => default;

                [GpuKernel]
                public static NarrowFirstConvertedExplicit K2() => default;
            }
            """;

        var (_, output, _) = RoslynTestUtility.RunGenerator(Source, dialect);

        var generated = string.Concat(output.SyntaxTrees.Select(static tree => tree.ToString()));
        Assert.Contains("native.Raw = host.Raw;", generated);
        Assert.Contains("host.Raw = native.Raw;", generated);
        Assert.DoesNotContain("native.Low = host.Low;", generated);
        Assert.DoesNotContain("host.Low = native.Low;", generated);
    }
}
