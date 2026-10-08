
namespace Kassyi.GSharp.Tests.Shared.Generator;

public class NativeStructGenerationTests
{
    [Fact]
    [Category(TestCategory.Syntax)]
    public async Task Test_NativeStructGeneration_ArrayFieldsAndNestedStructs()
    {
        var testConfig = RoslynTestUtility.TestGenerator("Analyzers/NativeStructGeneration.cs");

        // 0. Verify that generator execution and compilation produced no errors
        await testConfig.HasNoErrorsAsync();

        var driver = await testConfig.GetDriverAsync();
        Assert.NotNull(driver);

        var runResult = driver.GetRunResult();
        var generatedSources = runResult.GeneratedTrees.Select(t => t.ToString()).ToList();
        var allCode = string.Join("\n", generatedSources);

        // 1. Check native struct declaration and layout for ChildStruct where float[] is replaced by IntPtr
        Assert.Contains(
            "[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit, Size = 16)]",
            allCode);
        Assert.Contains("struct Kassyi_GSharp_Tests_GeneratorTestCode_ChildStruct", allCode);
        Assert.Contains("IntPtr Values;", allCode);
        Assert.Contains("int Scale;", allCode);

        // 2. Check native struct declaration for ParentStruct with nested native struct
        Assert.Contains("struct Kassyi_GSharp_Tests_GeneratorTestCode_ParentStruct", allCode);
        Assert.Contains("Kassyi_GSharp_Tests_GeneratorTestCode_ChildStruct Child;", allCode);
        Assert.Contains("IntPtr ExtraBuffer;", allCode);

        // 3. Check native struct declaration for StructWithArrayOfStructs where ChildStruct[] is replaced by IntPtr
        Assert.Contains("struct Kassyi_GSharp_Tests_GeneratorTestCode_StructWithArrayOfStructs", allCode);
        Assert.Contains("IntPtr Children;", allCode);

        // 4. Verify Marshal and Unmarshal helper methods exist for the structs
        Assert.Contains("Marshal_ChildStruct", allCode);
        Assert.Contains("Unmarshal_ChildStruct", allCode);
        Assert.Contains("Marshal_ParentStruct", allCode);
        Assert.Contains("Unmarshal_ParentStruct", allCode);

        // 5. Verify packed struct (now emitted explicitly following CUDA ABI rules)
        Assert.Contains("[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit, Size = 16)]", allCode);
        Assert.Contains("struct Kassyi_GSharp_Tests_GeneratorTestCode_PackedShadowStruct", allCode);
        Assert.Contains("IntPtr Data;", allCode);
        Assert.Contains("global::System.Byte Flag;", allCode);
    }
}
