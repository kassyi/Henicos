using System;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

// An in struct parameter that holds arrays calls UnmarshalArrays_ instead of full Unmarshal_ on ref/Unsafe.AsRef
// (TestCases/Transpiler/Emission/InStructMarshalShapeKernels.cu.cs)
public partial class EmissionShapeTests
{
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public void InStructParameter_CallsUnmarshalArraysInsteadOfFullUnmarshal(string dialect)
    {
        var launcher = PointerLauncher(Generate("InStructMarshalShapeKernels", dialect), "LaunchSeparateInAndRefBagKernel")
            .ToString();

        Assert.Matches(@"UnmarshalArrays_[^(]+\(in readOnlyBag, ref __gsharp_native_0, hStream\);", launcher);
        Assert.Matches(@"Unmarshal_[^(]+\(ref mutableBag, ref __gsharp_native_1, hStream\);", launcher);
        Assert.DoesNotMatch(@"Unmarshal_[^(]+\([^\n;]*Unsafe\.AsRef\(in readOnlyBag\)", launcher);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    public void AllInAliasGroup_CallsUnmarshalArraysWithoutFullUnmarshal(string dialect)
    {
        var launcher = PointerLauncher(Generate("InStructMarshalShapeKernels", dialect), "LaunchTwoAliasedInBagsKernel")
            .ToString();

        Assert.Matches(@"UnmarshalArrays_[^(]+\(in first, ref __gsharp_native_0, hStream\);", launcher);
        Assert.Matches(@"UnmarshalArrays_[^(]+\(in second, ref __gsharp_native_1, hStream\);", launcher);
        Assert.Matches(@"UnmarshalArrays_[^(]+\(in __gsharp_alias_host\[__gsharp_alias_index\], ref __gsharp_alias_native\[__gsharp_alias_index\], hStream\);", launcher);
        Assert.DoesNotMatch(@"Unmarshal_[^(]+\([^\n;]*Unsafe\.AsRef\(in first\)", launcher);
        Assert.DoesNotMatch(@"Unmarshal_[^(]+\([^\n;]*Unsafe\.AsRef\(in second\)", launcher);
    }
}
