using System.Collections.Immutable;
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Compiler.Helpers;

namespace Kassyi.GSharp.Tests.Transpiler;

public class CompilerInfrastructureUnitTests
{
    [Fact]
    [Trait("Category", "HappyPath")]
    public void Test_EquatableArray_BasicOperations_WorkCorrectly()
    {
        // Happy Path: Standard population, indexing, iteration, and equality
        var items = ImmutableArray.Create(1, 2, 3);
        var eqArray1 = items.AsEquatableArray();
        var eqArray2 = ImmutableArray.Create(1, 2, 3).AsEquatableArray();

        Assert.False(eqArray1.IsDefaultOrEmpty);
        Assert.Equal(3, eqArray1.Length);
        Assert.Equal(1, eqArray1[0]);
        Assert.Equal(2, eqArray1[1]);
        Assert.Equal(3, eqArray1[2]);

        Assert.True(eqArray1.Equals(eqArray2));
        Assert.True(eqArray1 == eqArray2);
        Assert.False(eqArray1 != eqArray2);
        Assert.Equal(eqArray1.GetHashCode(), eqArray2.GetHashCode());

        var list = new List<int>();
        foreach (var item in eqArray1)
        {
            list.Add(item);
        }
        Assert.Equal([1, 2, 3], list);
        Assert.Equal("[1, 2, 3]", eqArray1.ToString());
    }

    [Theory]
    [Trait("Category", "Boundary")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Test_EquatableArray_BoundarySizes_BehaveExpectedly(int size)
    {
        // Boundary Testing: 0, 1, and small element counts
        var builder = ImmutableArray.CreateBuilder<int>(size);
        for (int i = 0; i < size; i++)
        {
            builder.Add(i * 10);
        }
        var arr = builder.ToImmutable().AsEquatableArray();

        Assert.Equal(size, arr.Length);
        if (size == 0)
        {
            Assert.True(arr.IsDefaultOrEmpty);
            Assert.Equal("[]", arr.ToString());
        }
        else if (size == 1)
        {
            Assert.False(arr.IsDefaultOrEmpty);
            Assert.Equal("[0]", arr.ToString());
        }
        else
        {
            Assert.False(arr.IsDefaultOrEmpty);
            Assert.Equal("[0, 10]", arr.ToString());
        }
    }

    [Fact]
    [Trait("Category", "Boundary")]
    public void Test_EquatableArray_DefaultValue_HandledGracefully()
    {
        // Boundary Testing: Default (uninitialized) struct instance
        EquatableArray<int> defArr = default;

        Assert.True(defArr.IsDefaultOrEmpty);
        Assert.Equal(0, defArr.Length);
        Assert.Equal("[]", defArr.ToString());
        Assert.Equal(0, defArr.GetHashCode());

        // Default compared with Default
        EquatableArray<int> anotherDef = default;
        Assert.True(defArr.Equals(anotherDef));
        Assert.True(defArr == anotherDef);

        // Enumerate default array should return empty enumerator without throwing
        var count = 0;
        foreach (var _ in defArr)
        {
            count++;
        }
        Assert.Equal(0, count);
    }

    [Fact]
    [Trait("Category", "SadPath")]
    public void Test_EquatableArray_Mismatches_ReturnFalse()
    {
        // Sad Path: Length mismatches, element value differences, and Default vs Initialized
        var arrA = ImmutableArray.Create(1, 2, 3).AsEquatableArray();
        var arrDifferentLen = ImmutableArray.Create(1, 2).AsEquatableArray();
        var arrDifferentVals = ImmutableArray.Create(1, 2, 4).AsEquatableArray();
        EquatableArray<int> defArr = default;

        Assert.False(arrA.Equals(arrDifferentLen));
        Assert.False(arrA.Equals(arrDifferentVals));
        Assert.False(arrA.Equals(defArr));
        Assert.False(defArr.Equals(arrA));
        Assert.False(arrA.Equals((object)"not an EquatableArray"));
        Assert.False(arrA.Equals(null));
    }

    [Fact]
    [Trait("Category", "SadPath")]
    public void Test_EquatableArray_IndexOutOfBounds_ThrowsArgumentOutOfRangeException()
    {
        // Sad Path: Indexing out of range
        var arr = ImmutableArray.Create(10, 20).AsEquatableArray();
        Assert.Throws<IndexOutOfRangeException>(() => _ = arr[-1]);
        Assert.Throws<IndexOutOfRangeException>(() => _ = arr[2]);
    }

    [Fact]
    [Trait("Category", "HappyPath")]
    public void Test_SourceWriter_InterpolationAndIndentation_BuildsAccurateCode()
    {
        // Happy Path: SourceWriter with indentation scopes and interpolated handler
        var writer = new SourceWriter();
        writer.AppendLine("// Header comment");
        using (writer.Scope())
        {
            writer.AppendLine($"int a = {42};");
            writer.AppendLine($"string b = \"{"hello"}\";");
        }

        var code = writer.ToString();
        Assert.Contains("// Header comment", code);
        Assert.Contains("{", code);
        Assert.Contains("int a = 42;", code);
        Assert.Contains("string b = \"hello\";", code);
        Assert.Contains("}", code);
    }

    [Fact]
    [Trait("Category", "Stress")]
    public void Test_EquatableArray_ConcurrentReadsAndEquality_ThreadSafe()
    {
        // Stress & Concurrency: Multi-threaded equality checks across worker threads
        var arrA = ImmutableArray.Create(1, 2, 3, 4, 5).AsEquatableArray();
        var arrB = ImmutableArray.Create(1, 2, 3, 4, 5).AsEquatableArray();

        Parallel.For(0, 1000, _ =>
        {
            Assert.True(arrA == arrB);
            Assert.Equal(arrA.GetHashCode(), arrB.GetHashCode());
            Assert.Equal(5, arrA.Length);
        });
    }
}
