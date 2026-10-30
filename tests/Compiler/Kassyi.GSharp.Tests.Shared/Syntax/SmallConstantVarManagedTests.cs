#if GSHARP_CPU
using System;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>The managed C# fallback keeps the type of a small <c>const</c> field read through <c>var</c> (#859).</summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Syntax)]
public class SmallConstantVarManagedTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runManaged = ManagedKernelRun.Run;

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_VarOfSmallConstField_WrapsAtFieldType() =>
        SmallConstantVarScenarios.Increment(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_VarOfSmallEnumMember_WrapsAtBaseType() =>
        SmallConstantVarScenarios.EnumIncrement(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_VarOfSmallConstLocal_WrapsAtLocalType() =>
        SmallConstantVarScenarios.LocalConst(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_NarrowConstantInSwitchMathConditionalAndArguments() =>
        SmallConstantVarScenarios.NarrowUses(s_runManaged);
}
#endif
