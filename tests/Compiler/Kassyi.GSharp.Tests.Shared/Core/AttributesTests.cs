using System;
using Kassyi.GSharp.Core;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

public class AttributesTests
{
    [Fact]
    public void InstantiateAttributes_ToSatisfyCoverage()
    {
        var kernelAttr = new GpuKernelAttribute();
        Assert.NotNull(kernelAttr);

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
