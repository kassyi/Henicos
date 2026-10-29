using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// A <c>var</c> that reads a <c>const</c> field narrower than <c>int</c> keeps the field's type in the emitted C++, so
/// that its arithmetic wraps as in C# (#859).
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
public class SmallConstantVarTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runNative = static launch => launch();

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_VarOfSmallConstField_WrapsAtFieldType_Execution() =>
        SmallConstantVarScenarios.Increment(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_VarOfSmallEnumMember_WrapsAtBaseType_Execution() =>
        SmallConstantVarScenarios.EnumIncrement(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_VarOfSmallConstLocal_WrapsAtLocalType_Execution() =>
        SmallConstantVarScenarios.LocalConst(s_runNative);
}
