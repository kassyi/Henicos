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
    public void VarDeclaration_SmallConstField_WrapsAtFieldType() =>
        SmallConstantVarScenarios.Increment(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void VarDeclaration_SmallEnumMember_WrapsAtBaseType() =>
        SmallConstantVarScenarios.EnumIncrement(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void VarDeclaration_SmallConstLocal_WrapsAtLocalType() =>
        SmallConstantVarScenarios.LocalConst(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void NarrowConstant_InSwitchMathConditionalAndArguments_WrapsAtNarrowType() =>
        SmallConstantVarScenarios.NarrowUses(s_runNative);
}
