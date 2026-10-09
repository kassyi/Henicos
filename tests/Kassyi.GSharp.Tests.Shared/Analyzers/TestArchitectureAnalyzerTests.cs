using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Tests.Common;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

[Category(TestCategory.Compiler)]
public class TestArchitectureAnalyzerTests : AnalyzerTestBase
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task Test_ValidTestArchitecture_NoDiagnostics()
    {
        await VerifyAnalyzerFromFileAsync("TestArchitecture/ValidTestArchitecture.cs", new TestArchitectureAnalyzer(), nameof(Test_ValidTestArchitecture_NoDiagnostics));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_InvalidBaseClass_ReportsError()
    {
        await VerifyAnalyzerFromFileAsync("TestArchitecture/InvalidBaseClass.cs", new TestArchitectureAnalyzer(), nameof(Test_InvalidBaseClass_ReportsError));
    }
    
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task Test_MissingCategory_ReportsError()
    {
        await VerifyAnalyzerFromFileAsync("TestArchitecture/MissingCategory.cs", new TestArchitectureAnalyzer(), nameof(Test_MissingCategory_ReportsError));
    }
}
