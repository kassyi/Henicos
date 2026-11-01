using System;
using Xunit;
using Kassyi.GSharp.Tests.Infrastructure;

// HNT0008: class-level [Trait] is reported even when a valid 3-axis attribute is present
[Category(TestCategory.Compiler)]
[Trait("IssueId", "177")]
public class ClassWithRawTraitTests
{
    // HNT0008: method-level [Trait] is reported even when [Scenario] is present
    [Fact]
    [Trait("DiagnosticId", "GSH0067")]
    [Scenario(TestScenario.SadPath)]
    public void MethodWithDiagnosticTrait()
    {
        Assert.Equal(1, 1);
    }

    // A user attribute named TraitAttribute is not Xunit.TraitAttribute, so it is not reported
    [Fact]
    [CustomAttributes.Trait("Key", "Value")]
    [Scenario(TestScenario.HappyPath)]
    public void MethodWithUserTraitAttribute()
    {
        Assert.Equal(1, 1);
    }
}

// HNT0008 and HNT0002: [Trait("Category", ...)] is rejected by HNT0008 and no longer satisfies HNT0002
[Trait("Category", "GpuExecution")]
public class LegacyCategoryTraitOnlyTests
{
    [Fact]
    public void MethodRelyingOnLegacyCategoryTrait()
    {
        Assert.Equal(1, 1);
    }
}

namespace CustomAttributes
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public sealed class TraitAttribute(string name, string value) : Attribute
    {
        public string Name { get; } = name;
        public string Value { get; } = value;
    }
}
