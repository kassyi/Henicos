using Kassyi.GSharp.Tests.Generators;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

[Category(TestCategory.Compiler)]
public class TestStrengthAnalyzerTests : AnalyzerTestBase
{
    // HNT0004: reported on tests that reach no Assert / Verifier / FluentAssertions call. Followed: helpers, local
    // functions, method groups, base class members, overrides of abstract members and interface implementations.
    // Not reported on non-test methods; a user type named Assert is not an assertion.
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_NoAssertion_ReportsWarning()
    {
        await VerifyAnalyzerFromFileAsync(
            "TestStrength/NoAssertion.cs",
            new TestStrengthAnalyzer(),
            nameof(Test_NoAssertion_ReportsWarning)
        );
    }

    // HNT0005: reported on System.Diagnostics.Debug.Assert in a test method (lambdas included); not in helpers or on
    // a user type named Debug.
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_DebugAssert_ReportsWarning()
    {
        await VerifyAnalyzerFromFileAsync(
            "TestStrength/DebugAssert.cs",
            new TestStrengthAnalyzer(),
            nameof(Test_DebugAssert_ReportsWarning)
        );
    }

    // HNT0006: reported on new Random() without a seed (field initializers and target-typed new included),
    // Random.Shared and RandomNumberGenerator anywhere in a test assembly; not on a seeded Random or user types.
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_UnseededRandom_ReportsWarning()
    {
        await VerifyAnalyzerFromFileAsync(
            "TestStrength/UnseededRandom.cs",
            new TestStrengthAnalyzer(),
            nameof(Test_UnseededRandom_ReportsWarning)
        );
    }

    // HNT0006 belongs to test assemblies only.
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public async Task Test_UnseededRandomOutsideTestAssembly_NoDiagnostics()
    {
        await VerifyAnalyzerFromFileAsync(
            "TestStrength/UnseededRandom.cs",
            new TestStrengthAnalyzer(),
            nameof(Test_UnseededRandomOutsideTestAssembly_NoDiagnostics),
            assemblyName: "ProductAssembly"
        );
    }

    // HNT0007: reported on 'catch' and 'catch (Exception)' without a filter in a test method whose handler drops the
    // exception (also when it only writes it to Console, the test output, Debug or Trace, returns before 'throw;' or
    // turns it into a skip). Not reported on a filter, a narrow type, a handler that records the exception (assigns it
    // or passes it on), asserts, or always rethrows, or on a helper.
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_BroadCatch_ReportsWarning()
    {
        await VerifyAnalyzerFromFileAsync(
            "TestStrength/BroadCatch.cs",
            new TestStrengthAnalyzer(),
            nameof(Test_BroadCatch_ReportsWarning)
        );
    }
}
