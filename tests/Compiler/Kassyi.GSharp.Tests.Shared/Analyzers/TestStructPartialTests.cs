#nullable enable
using System.IO;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

/// <summary>
/// A struct that holds a device function or a kernel must be <c>partial</c>, or the managed path runs the user's operators
/// where the GPU gives another result (#1897 (A device function of a non-partial struct gets no managed version, so the
/// managed path differs from the GPU without GSH0036)). GSH0036 reports it, but the analyzer does not look at the test
/// projects (generated code to it), so this test reads their sources.
/// </summary>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class TestStructPartialTests
{
    // Test cases that hold the non-partial form on purpose, for the analyzer tests of GSH0036 and GSH0111
    private static readonly string[] s_deliberateFiles =
    [
        "EnclosingTypeMustBePartial_SadPath.cs",
        "EnclosingTypeMustBePartial_Members_SadPath.cs",
        "KernelDeclaration_NonPartial.cs",
    ];

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void StructsOfTestSources_HoldingGpuMethods_ArePartial()
    {
        var options = new CSharpParseOptions(LanguageVersion.Preview);
        var offenders = new List<string>();
        var holders = new List<string>();
        var testsRoot = RepositoryPaths.Combine("tests");
        var separator = Path.DirectorySeparatorChar;
        foreach (var path in Directory.EnumerateFiles(testsRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains($"{separator}obj{separator}") || path.Contains($"{separator}bin{separator}") ||
                s_deliberateFiles.Contains(Path.GetFileName(path)))
            {
                continue;
            }

            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path), options).GetRoot();
            foreach (var type in root.DescendantNodes().OfType<StructDeclarationSyntax>()
                         .Concat<TypeDeclarationSyntax>(root.DescendantNodes().OfType<RecordDeclarationSyntax>()
                             .Where(static record => record.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword))))
            {
                if (!HoldsGpuMethod(type))
                {
                    continue;
                }

                holders.Add(type.Identifier.Text);
                if (!type.Modifiers.Any(SyntaxKind.PartialKeyword))
                {
                    offenders.Add($"{Path.GetRelativePath(testsRoot, path)}: {type.Identifier.Text}");
                }
            }
        }

        // The scan sees the structs that it is meant to check (a partial one of IntegerDivisionCheckKernels.cu.cs)
        Assert.Contains("DivisionHolder", holders);
        Assert.True(offenders.Count == 0, "Make these structs partial: " + string.Join("; ", offenders));
    }

    // A method, operator or accessor of the type itself (not of a type inside it) with [GpuDeviceFunction] or [GpuKernel]
    private static bool HoldsGpuMethod(TypeDeclarationSyntax type) =>
        type.Members
            .Where(static member => member is not BaseTypeDeclarationSyntax)
            .SelectMany(static member => member.DescendantNodesAndSelf(static node => node is not BaseTypeDeclarationSyntax))
            .OfType<AttributeSyntax>()
            .Any(static attribute => IsGpuAttribute(attribute.Name));

    private static bool IsGpuAttribute(NameSyntax name)
    {
        var simple = name switch
        {
            QualifiedNameSyntax qualified => qualified.Right,
            AliasQualifiedNameSyntax alias => alias.Name,
            _ => name,
        };
        return simple is SimpleNameSyntax { Identifier.ValueText: var text } &&
               text is "GpuDeviceFunction" or "GpuDeviceFunctionAttribute" or "GpuKernel" or "GpuKernelAttribute";
    }
}
