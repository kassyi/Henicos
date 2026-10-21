
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

        // 1. Check native struct declaration and layout for ChildStruct where float[] is replaced by GSharpArrayProxy
        Assert.Contains(
            "[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit, Size = 24)]",
            allCode.WithCSharpNames());
        Assert.Contains("struct Kassyi.GSharp.Tests.GeneratorTestCode.ChildStruct", allCode.WithCSharpNames());
        Assert.Contains("global::Kassyi.GSharp.Runtime.Interop.GSharpArrayProxy Values;", allCode.WithCSharpNames());
        Assert.Contains("int Scale;", allCode.WithCSharpNames());

        // 2. Check native struct declaration for ParentStruct with nested native struct
        Assert.Contains("struct Kassyi.GSharp.Tests.GeneratorTestCode.ParentStruct", allCode.WithCSharpNames());
        Assert.Contains("Kassyi.GSharp.Tests.GeneratorTestCode.ChildStruct Child;", allCode.WithCSharpNames());
        Assert.Contains("global::Kassyi.GSharp.Runtime.Interop.GSharpArrayProxy ExtraBuffer;", allCode.WithCSharpNames());

        // 3. Check native struct declaration for StructWithArrayOfStructs where ChildStruct[] is replaced by GSharpArrayProxy
        Assert.Contains("struct Kassyi.GSharp.Tests.GeneratorTestCode.StructWithArrayOfStructs", allCode.WithCSharpNames());
        Assert.Contains("global::Kassyi.GSharp.Runtime.Interop.GSharpArrayProxy Children;", allCode.WithCSharpNames());

        // 4. Verify Marshal and Unmarshal helper methods exist for the structs
        Assert.Contains("Marshal_ChildStruct", allCode.WithCSharpNames());
        Assert.Contains("Unmarshal_ChildStruct", allCode.WithCSharpNames());
        Assert.Contains("Marshal_ParentStruct", allCode.WithCSharpNames());
        Assert.Contains("Unmarshal_ParentStruct", allCode.WithCSharpNames());

        // 5. Verify packed struct (now emitted explicitly following CUDA ABI rules)
        Assert.Contains("[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit, Size = 18)]", allCode.WithCSharpNames());
        Assert.Contains("struct Kassyi.GSharp.Tests.GeneratorTestCode.PackedShadowStruct", allCode.WithCSharpNames());
        Assert.Contains("global::Kassyi.GSharp.Runtime.Interop.GSharpArrayProxy Data;", allCode.WithCSharpNames());
        Assert.Contains("global::System.Byte Flag;", allCode.WithCSharpNames());
    }
}
