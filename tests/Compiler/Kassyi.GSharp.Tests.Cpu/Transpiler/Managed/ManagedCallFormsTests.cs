#nullable enable
using System.Linq;
using Microsoft.CodeAnalysis;
using Kassyi.GSharp.Compiler.Semantics;
using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Tests.Shared;

namespace Kassyi.GSharp.Tests.Cpu.Transpiler.Managed;

[Category(TestCategory.Compiler)]
public class ManagedCallFormsTests
{
    private static readonly Compilation s_compilation = RoslynTestUtility.CreateCompilation("");

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GetArgumentModifier_VariableToRefReadOnly_IsIn()
    {
        Assert.Equal(
            ManagedArgumentModifier.In,
            ManagedCallForms.GetArgumentModifier(RefKind.RefReadOnlyParameter, isVariable: true)
        );
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void GetArgumentModifier_ValueToRefReadOnly_IsNone()
    {
        Assert.Equal(
            ManagedArgumentModifier.None,
            ManagedCallForms.GetArgumentModifier(RefKind.RefReadOnlyParameter, isVariable: false)
        );
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GetArgumentModifier_VariableToIn_IsNone()
    {
        Assert.Equal(
            ManagedArgumentModifier.None,
            ManagedCallForms.GetArgumentModifier(RefKind.In, isVariable: true)
        );
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(RefKind.None, false)]
    [InlineData(RefKind.Ref, false)]
    [InlineData(RefKind.Out, false)]
    [InlineData(RefKind.In, false)]
    [InlineData(RefKind.RefReadOnlyParameter, true)]
    public void ArgumentNeedsVariableTest_EachRefKind_OnlyRefReadOnly(RefKind refKind, bool expected)
    {
        Assert.Equal(expected, ManagedCallForms.ArgumentNeedsVariableTest(refKind));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GetReceiverModifier_RefReceiver_IsRef()
    {
        Assert.Equal(
            ManagedArgumentModifier.Ref,
            ManagedCallForms.GetReceiverModifier(RefKind.Ref, isVariableOfParameterType: false)
        );
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GetReceiverModifier_InReceiverOfAVariable_IsIn()
    {
        Assert.Equal(
            ManagedArgumentModifier.In,
            ManagedCallForms.GetReceiverModifier(RefKind.In, isVariableOfParameterType: true)
        );
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GetReceiverModifier_RefReadOnlyReceiverOfAVariable_IsIn()
    {
        Assert.Equal(
            ManagedArgumentModifier.In,
            ManagedCallForms.GetReceiverModifier(RefKind.RefReadOnlyParameter, isVariableOfParameterType: true)
        );
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void GetReceiverModifier_InReceiverOfAValue_IsNone()
    {
        Assert.Equal(
            ManagedArgumentModifier.None,
            ManagedCallForms.GetReceiverModifier(RefKind.In, isVariableOfParameterType: false)
        );
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(RefKind.None, false)]
    [InlineData(RefKind.Ref, false)]
    [InlineData(RefKind.Out, false)]
    [InlineData(RefKind.In, true)]
    [InlineData(RefKind.RefReadOnlyParameter, true)]
    public void ReceiverNeedsVariableTest_EachRefKind_OnlyInAndRefReadOnly(RefKind refKind, bool expected)
    {
        Assert.Equal(expected, ManagedCallForms.ReceiverNeedsVariableTest(refKind));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GetNamedSymbol_StaticMethod_IsTheMethod()
    {
        var math = s_compilation.GetTypeByMetadataName("System.Math") ??
            throw new InvalidOperationException("System.Math not found.");
        var method = math.GetMembers("Max").OfType<IMethodSymbol>()
            .First(m => m.Parameters.Length == 2 && m.Parameters[0].Type.SpecialType == SpecialType.System_Int32);
        var named = ManagedNameQualifier.GetNamedSymbol(method, isAttributeName: false);

        Assert.NotNull(named);
        Assert.True(SymbolEqualityComparer.Default.Equals(method, named));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void GetNamedSymbol_InstanceMethod_IsNull()
    {
        var str = s_compilation.GetSpecialType(SpecialType.System_String);
        var method = str.GetMembers("ToString").OfType<IMethodSymbol>().First(m => m.Parameters.Length == 0);

        Assert.Null(ManagedNameQualifier.GetNamedSymbol(method, isAttributeName: false));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void GetNamedSymbol_GlobalNamespace_IsNull()
    {
        Assert.Null(ManagedNameQualifier.GetNamedSymbol(s_compilation.GlobalNamespace, isAttributeName: false));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GetNamedSymbol_AttributeConstructorAsAttributeName_IsTheAttributeType()
    {
        var attr = s_compilation.GetTypeByMetadataName("System.ObsoleteAttribute") ??
            throw new InvalidOperationException("System.ObsoleteAttribute not found.");
        var ctor = attr.Constructors.First();

        var namedTrue = ManagedNameQualifier.GetNamedSymbol(ctor, isAttributeName: true);
        Assert.NotNull(namedTrue);
        Assert.True(SymbolEqualityComparer.Default.Equals(attr, namedTrue));

        var namedFalse = ManagedNameQualifier.GetNamedSymbol(ctor, isAttributeName: false);
        Assert.Null(namedFalse);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GetContainerName_TypeInANamespace_IsTheNamespaceFromGlobal()
    {
        var math = s_compilation.GetTypeByMetadataName("System.Math") ??
            throw new InvalidOperationException("System.Math not found.");
        Assert.Equal("global::System", ManagedNameQualifier.GetContainerName(math));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GetContainerName_StaticMember_IsTheContainingType()
    {
        var math = s_compilation.GetTypeByMetadataName("System.Math") ??
            throw new InvalidOperationException("System.Math not found.");
        var method = math.GetMembers("Max").OfType<IMethodSymbol>().First();
        Assert.Equal("global::System.Math", ManagedNameQualifier.GetContainerName(method));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GetContainerName_TopLevelNamespace_IsEmpty()
    {
        var sysNs = s_compilation.GlobalNamespace.GetNamespaceMembers().First(n => n.Name == "System");
        Assert.Equal("", ManagedNameQualifier.GetContainerName(sysNs));
    }
}
