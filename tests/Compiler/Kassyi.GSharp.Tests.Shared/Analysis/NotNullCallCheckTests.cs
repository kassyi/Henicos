#nullable enable
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Compiler.Transpiler.Helpers;

namespace Kassyi.GSharp.Tests.Shared.Analysis;

/// <summary>
/// The plan of a <c>Gpu.NotNull</c> call (<see cref="NotNullCallCheck.Plan(InvocationExpressionSyntax, ExpressionSyntax,
/// SemanticModel, ErrorSiteTable?, string?)"/>): no check for a proven array, otherwise a site of the kernel's error site
/// table, or in a device function (no table) a message that names the place of the call.
/// </summary>
[Category(TestCategory.Compiler)]
public class NotNullCallCheckTests
{
    private static readonly Compilation s_compilation = RoslynTestUtility.CreateCompilation(
        RoslynTestUtility.ReadTestCase("Analysis/NonNullProofCases.cs")
    );

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Plan_ProvenKernelParameter_HasNoSite()
    {
        var table = new ErrorSiteTable();

        var check = Plan("KernelNotNullOfParameter", table, "KernelNotNullOfParameter");

        Assert.Null(check.Site);
        Assert.Equal(0, table.Count);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Plan_NullableFieldInKernel_RegistersTheCallSite()
    {
        var table = new ErrorSiteTable();

        var check = Plan("KernelNotNullCall", table, "KernelNotNullCall");

        var site = Assert.NotNull(check.Site);
        Assert.NotEqual(0u, site.SiteId);
        Assert.Equal("", site.Message);
        var entry = Assert.Single(table.Entries);
        Assert.Equal(site.SiteId, entry.SiteId);
        Assert.Equal("KernelNotNullCall", entry.MemberName);
        Assert.Equal(NotNullCallCheck.SiteMessage, entry.DefaultMessage);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Plan_NullableParameterInDeviceFunction_NamesThePlaceInTheMessage()
    {
        var check = Plan("DeviceNotNullLocal", table: null, "DeviceNotNullLocal");

        var site = Assert.NotNull(check.Site);
        Assert.Equal(0u, site.SiteId);
        Assert.StartsWith(NotNullCallCheck.SiteMessage + " at ", site.Message, StringComparison.Ordinal);
    }

    // The plan of the one Gpu.NotNull call in the method
    private static NotNullCheck Plan(string methodName, ErrorSiteTable? table, string memberName)
    {
        var type = s_compilation.GetTypeByMetadataName("NonNullProofCases.Reads") ??
                   throw new InvalidOperationException("Type 'NonNullProofCases.Reads' not found.");
        var declaration = type.GetMembers(methodName).OfType<IMethodSymbol>().Single().DeclaringSyntaxReferences[0]
            .GetSyntax();
        var call = declaration.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(static i => i.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "NotNull" });
        var model = s_compilation.GetSemanticModel(declaration.SyntaxTree);
        return NotNullCallCheck.Plan(call, call.ArgumentList.Arguments[0].Expression, model, table, memberName);
    }
}
