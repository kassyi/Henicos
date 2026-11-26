using System;
using Xunit;
using Kassyi.GSharp.Tests.Infrastructure;

// Compiled beside WindowsPlatformSkipGeneratorOutput.cs, which the test gives a generator path (*.g.cs)
[Category(TestCategory.Compiler)]
public class GeneratorHelperCallerTests
{
    [SkippableFact]
    public void SkipThroughGeneratedHelper()
    {
        GeneratedPlatformHelpers.RequireWindows();
    }
}
