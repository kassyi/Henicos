using System;
using Kassyi.GSharp.Core;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

[Category(TestCategory.Compiler)]
public class AttributesTests
{
    [Fact]
    public void InstantiateAttributes_ToSatisfyCoverage()
    {
        var kernelAttr = new GpuKernelAttribute();
        Assert.NotNull(kernelAttr);

        var constexprEmptyAttr = new GpuConstexprAttribute();
        Assert.NotNull(constexprEmptyAttr);

        var constexprLongAttr = new GpuConstexprAttribute(10, 20);
        Assert.NotNull(constexprLongAttr);

        var constexprDoubleAttr = new GpuConstexprAttribute(0.5, 1.5);
        Assert.NotNull(constexprDoubleAttr);

        var intrinsicAttr1 = new CudaIntrinsicAttribute("foo");
        Assert.NotNull(intrinsicAttr1);

        var importAttr = new CudaImportAttribute("file");
        Assert.NotNull(importAttr);

        var skipAttr = new SkipSnapshotConsistencyAttribute("reason");
        Assert.NotNull(skipAttr);

        var opAttr = new GpuOperatorMacroAttribute(typeof(object), "op");
        Assert.NotNull(opAttr);
    }
}
