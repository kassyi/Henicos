#nullable enable
using System;
using System.IO;
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

[Category(TestCategory.Compiler)]
public class ArrayLikeTypeTests
{
    private static IFieldSymbol GetFieldSymbol(Compilation compilation, string fieldName)
    {
        var classType = compilation.GetTypeByMetadataName("Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.ArrayLikeTypeTestCase");
        if (classType == null)
        {
            throw new InvalidOperationException("Could not find type Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.ArrayLikeTypeTestCase");
        }

        foreach (var member in classType.GetMembers())
        {
            if (member is IFieldSymbol field && member.Name == fieldName)
            {
                if (field.Type.TypeKind == TypeKind.Error)
                {
                    throw new InvalidOperationException($"Field '{fieldName}' type resolved as TypeKind.Error.");
                }

                return field;
            }
        }

        throw new InvalidOperationException($"Field '{fieldName}' not found on ArrayLikeTypeTestCase.");
    }

    [Theory]
    [InlineData("ArrayField")]
    [InlineData("SpanField")]
    [InlineData("ReadOnlySpanField")]
    [InlineData("MemoryField")]
    [InlineData("ReadOnlyMemoryField")]
    [InlineData("ArraySegmentField")]
    [InlineData("ImmutableArrayField")]
    [Scenario(TestScenario.HappyPath)]
    public void Test_TryGetArrayLikeElementType_HappyPath_ReturnsTrue(string fieldName)
    {
        var (compilation, _, _) = RoslynTestUtility.GetTestCaseContext(Path.Combine("Analyzers", "ArrayLikeTypeTestCase.cs"));
        var fieldSymbol = GetFieldSymbol(compilation, fieldName);

        var isArrayLike = fieldSymbol.Type.TryGetArrayLikeElementType(out var elementType);

        Assert.True(isArrayLike);
        Assert.NotNull(elementType);
        Assert.Equal(SpecialType.System_Int32, elementType.SpecialType);
        Assert.True(fieldSymbol.Type.IsArrayLikeType());
    }

    [Theory]
    [InlineData("IntField")]
    [InlineData("ListField")]
    [InlineData("FakeSystemSpanField")]
    [InlineData("FakeMemoryField")]
    [Scenario(TestScenario.SadPath)]
    public void Test_TryGetArrayLikeElementType_SadPath_ReturnsFalse(string fieldName)
    {
        var (compilation, _, _) = RoslynTestUtility.GetTestCaseContext(Path.Combine("Analyzers", "ArrayLikeTypeTestCase.cs"));
        var fieldSymbol = GetFieldSymbol(compilation, fieldName);

        var isArrayLike = fieldSymbol.Type.TryGetArrayLikeElementType(out var elementType);

        Assert.False(isArrayLike);
        Assert.Null(elementType);
        Assert.False(fieldSymbol.Type.IsArrayLikeType());
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_TryGetArrayLikeElementType_NullType_ReturnsFalse()
    {
        ITypeSymbol? nullType = null;

        var isArrayLike = nullType.TryGetArrayLikeElementType(out var elementType);

        Assert.False(isArrayLike);
        Assert.Null(elementType);
        Assert.False(nullType.IsArrayLikeType());
    }
}
