using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Compiler.Extensions;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>Verifies the C# type names that the generators write into host code.</summary>
[Category(TestCategory.Compiler)]
public class CSharpTypeNameWriterTests
{
    private static readonly Compilation s_compilation = RoslynTestUtility.CreateCompilation(
        RoslynTestUtility.ReadTestCase("Transpiler/CSharpTypeNameWriterSource.cs")
    );
    private static readonly INamedTypeSymbol s_holder = GetType("Names.Holder");

    // A nested type of a generic type names the type arguments of its containing type: Outer<int>.Inner
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("NestedInGeneric", "global::Names.Outer<int>.Inner")]
    [InlineData("GenericNestedInGeneric", "global::Names.Outer<int>.GenericInner<float>")]
    [InlineData("ArrayOfNestedInGeneric", "global::Names.Outer<int>.Inner[]")]
    [InlineData("NestedInPlain", "global::Names.Plain.Nested")]
    [InlineData("NullableInt", "int?")]
    public void AppendCsharpTypeName_ContainingTypeArgs_WritesContainingTypeArguments(string field, string expected)
    {
        using var writer = new SourceWriter();
        writer.AppendCsharpTypeName(GetFieldType(field));

        Assert.Equal(expected, writer.ToString());
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("NestedInGeneric", "global::Names.Outer<global::System.Int32>.Inner")]
    [InlineData("GenericNestedInGeneric", "global::Names.Outer<global::System.Int32>.GenericInner<global::System.Single>")]
    [InlineData("ArrayOfNestedInGeneric", "global::Names.Outer<global::System.Int32>.Inner[]")]
    [InlineData("NestedInPlain", "global::Names.Plain.Nested")]
    [InlineData("NullableInt", "global::System.Nullable<global::System.Int32>")]
    public void AppendFullyQualifiedCsharpName_ContainingTypeArgs_WritesContainingTypeArguments(
        string field,
        string expected)
    {
        using var writer = new SourceWriter();
        writer.AppendFullyQualifiedCsharpName(GetFieldType(field));

        Assert.Equal(expected, writer.ToString());
    }

    // A keyword name is spelled as C# source spells it, so the generated code that parses the name compiles
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("KeywordGeneric", "global::Names.@class<int>")]
    [InlineData("KeywordArgument", "global::Names.@class<global::Names.@struct>")]
    [InlineData("KeywordNamespace", "global::@event.Plain")]
    public void AppendCsharpTypeName_KeywordName_EscapesTheKeyword(string field, string expected)
    {
        using var writer = new SourceWriter();
        writer.AppendCsharpTypeName(GetFieldType(field));

        Assert.Equal(expected, writer.ToString());
    }

    // A namespace name is written as it is: callers use it as a metadata name, not as C# source
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GetFullNamespaceName_KeywordNamespace_KeepsTheName() =>
        Assert.Equal("event", GetFieldType("KeywordNamespace").ContainingNamespace.GetFullNamespaceName());

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void GetFullNamespaceName_NestedNamespaces_JoinsNamespaceSegments()
    {
        Assert.Equal("Names", s_holder.ContainingNamespace.GetFullNamespaceName());
        Assert.Equal(
            "System.Collections.Generic",
            GetType("System.Collections.Generic.List`1").ContainingNamespace.GetFullNamespaceName()
        );
    }

    private static ITypeSymbol GetFieldType(string name) =>
        s_holder.GetMembers(name) is [IFieldSymbol field] ? field.Type : throw new InvalidOperationException(name);

    private static INamedTypeSymbol GetType(string metadataName) =>
        s_compilation.GetTypeByMetadataName(metadataName) ??
        throw new InvalidOperationException($"{metadataName} is not in the test compilation.");
}
