using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Kassyi.GSharp.Tests.Helpers;
using Microsoft.CodeAnalysis;

namespace Kassyi.GSharp.Tests.Generator;

public class GEN_33_NativeStructGenerationTests
{
    private static async Task<string> LoadTestCaseSourceAsync()
    {
        var testCasesDir = RoslynTestUtility.GetTestCasesDirectory();
        var path = Path.Combine(testCasesDir, "Analyzers", "GEN-33_NativeStructGeneration.cs");
        return await File.ReadAllTextAsync(path);
    }

    [Fact]
    [Trait("Category", "Syntax")]
    [Trait("FeatureId", "GEN-33")]
    public async Task Test_GEN33_NativeStructGeneration_ArrayFieldsAndNestedStructs()
    {
        var source = await LoadTestCaseSourceAsync();
        var (driver, compilation, diagnostics) = RoslynTestUtility.RunGenerator(source);

        Assert.NotNull(driver);
        Assert.NotNull(compilation);

        var runResult = driver.GetRunResult();
        var generatedSources = runResult.GeneratedTrees.Select(t => t.ToString()).ToList();
        var allCode = string.Join("\n", generatedSources);

        // 1. Check native struct declaration for ChildStruct where float[] is replaced by IntPtr
        Assert.Contains("struct __Native_Kassyi_GSharp_Tests_GeneratorTestCode_ChildStruct", allCode);
        Assert.Contains("IntPtr Values;", allCode);
        Assert.Contains("int Scale;", allCode);

        // 2. Check native struct declaration for ParentStruct with nested native struct
        Assert.Contains("struct __Native_Kassyi_GSharp_Tests_GeneratorTestCode_ParentStruct", allCode);
        Assert.Contains("__Native_Kassyi_GSharp_Tests_GeneratorTestCode_ChildStruct Child;", allCode);
        Assert.Contains("IntPtr ExtraBuffer;", allCode);

        // 3. Check native struct declaration for StructWithArrayOfStructs where ChildStruct[] is replaced by IntPtr
        Assert.Contains("struct __Native_Kassyi_GSharp_Tests_GeneratorTestCode_StructWithArrayOfStructs", allCode);
        Assert.Contains("IntPtr Children;", allCode);

        // 4. Verify Marshal and Unmarshal helper methods exist for the structs
        Assert.Contains("Marshal_Kassyi_GSharp_Tests_GeneratorTestCode_ChildStruct", allCode);
        Assert.Contains("Unmarshal_Kassyi_GSharp_Tests_GeneratorTestCode_ChildStruct", allCode);
        Assert.Contains("Marshal_Kassyi_GSharp_Tests_GeneratorTestCode_ParentStruct", allCode);
        Assert.Contains("Unmarshal_Kassyi_GSharp_Tests_GeneratorTestCode_ParentStruct", allCode);
    }
}
