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

    // A namespace that generated C# source reopens or qualifies with is spelled as C# source spells it
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("event.Plain", "@event")]
    [InlineData("event.base.Deep", "@event.@base")]
    [InlineData("Names.Holder", "Names")]
    [InlineData("System.Collections.Generic.List`1", "System.Collections.Generic")]
    public void GetCsharpNamespaceName_Namespace_EscapesEachKeywordSegment(string typeName, string expected)
    {
        var ns = GetType(typeName).ContainingNamespace;
        using var writer = new SourceWriter();
        writer.AppendCsharpNamespace(ns);

        Assert.Equal(expected, ns.GetCsharpNamespaceName());
        Assert.Equal(expected, writer.ToString());
    }

    // A metadata name is written as it is: [UnsafeAccessorType] and the other metadata names take no @
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("event.Plain", "event")]
    [InlineData("event.base.Deep", "event.base")]
    [InlineData("System.Collections.Generic.List`1", "System.Collections.Generic")]
    public void GetMetadataNamespaceName_Namespace_KeepsTheNames(string typeName, string expected) =>
        Assert.Equal(expected, GetType(typeName).ContainingNamespace.GetMetadataNamespaceName());

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GetCsharpNamespaceName_GlobalNamespace_IsEmpty()
    {
        Assert.Equal("", s_compilation.GlobalNamespace.GetCsharpNamespaceName());
        Assert.Equal("", s_compilation.GlobalNamespace.GetMetadataNamespaceName());
    }

    // The name that declares a type in a partial declaration: its identifier and its own type parameters, escaped
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Names.class`1", "@class<T>")]
    [InlineData("Names.KeywordParameters`2", "KeywordParameters<@in, U>")]
    [InlineData("Names.Outer`1+Inner", "Inner")]
    [InlineData("event.Plain", "Plain")]
    public void GetDeclaredTypeName_KeywordNames_EscapesTheKeywords(string typeName, string expected) =>
        Assert.Equal(expected, GetType(typeName).GetDeclaredTypeName());

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("class", "@class")]
    [InlineData("value", "value")]
    [InlineData("Class", "Class")]
    public void AppendCsharpIdentifier_ReservedKeywordOnly_GetsTheVerbatimPrefix(string name, string expected)
    {
        using var writer = new SourceWriter();
        writer.AppendCsharpIdentifier(name);

        Assert.Equal(expected, writer.ToString());
        Assert.Equal(expected, name.ToCSharpIdentifier());
    }

    private static ITypeSymbol GetFieldType(string name) =>
        s_holder.GetMembers(name) is [IFieldSymbol field] ? field.Type : throw new InvalidOperationException(name);

    private static INamedTypeSymbol GetType(string metadataName) =>
        s_compilation.GetTypeByMetadataName(metadataName) ??
        throw new InvalidOperationException($"{metadataName} is not in the test compilation.");
}
