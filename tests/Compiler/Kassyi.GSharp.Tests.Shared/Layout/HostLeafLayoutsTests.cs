#nullable enable
using System;
using System.IO;
using System.Numerics;
using Kassyi.GSharp.Compiler.Dialects;
using Kassyi.GSharp.Compiler.Layout;
using Kassyi.GSharp.Tests.Infrastructure;
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Layout.TransferPlannerTestHelper;

namespace Kassyi.GSharp.Tests.Shared.Layout;

public class HostLeafLayoutsTests
{
    // The host leaf table is the CLR's: the size is Unsafe.SizeOf, the alignment the offset of the value after one byte
    [Theory]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(typeof(bool))]
    [InlineData(typeof(byte))]
    [InlineData(typeof(sbyte))]
    [InlineData(typeof(char))]
    [InlineData(typeof(short))]
    [InlineData(typeof(ushort))]
    [InlineData(typeof(int))]
    [InlineData(typeof(uint))]
    [InlineData(typeof(long))]
    [InlineData(typeof(ulong))]
    [InlineData(typeof(float))]
    [InlineData(typeof(double))]
    [InlineData(typeof(Vector2))]
    [InlineData(typeof(Vector3))]
    [InlineData(typeof(Vector4))]
    public void HostLeafLayouts_MatchTheRuntime(Type type)
    {
        var (compilation, _, _) = GetContext(Path.Combine("Layout", "HostLayoutCases.cs"));
        var symbol = compilation.GetTypeByMetadataName(type.FullName ?? type.Name) ??
                     throw new InvalidOperationException(type.FullName);

        Assert.True(HostLeafLayouts.TryGet(symbol, out var layout));
        Assert.Equal(SizeOf(type), layout.Size);
        Assert.Equal(OffsetOf(typeof(AlignmentProbe<>).MakeGenericType(type), nameof(AlignmentProbe<int>.Value)),
            layout.Alignment);
    }

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.SadPath)]
    public void HostLeafLayouts_StructsAndDeviceVectors_AreNotLeaves()
    {
        var (compilation, _, _) = GetContext(Path.Combine("Layout", "HostLayoutCases.cs"));
        foreach (var name in new[] { "Kassyi.GSharp.Core.Float2", "System.Decimal", "System.IntPtr" })
        {
            var symbol = compilation.GetTypeByMetadataName(name) ?? throw new InvalidOperationException(name);
            Assert.False(HostLeafLayouts.TryGet(symbol, out _), name);
        }

        Assert.Equal(new TypeLayout(IntPtr.Size, IntPtr.Size), HostLeafLayouts.Pointer);
    }
}
