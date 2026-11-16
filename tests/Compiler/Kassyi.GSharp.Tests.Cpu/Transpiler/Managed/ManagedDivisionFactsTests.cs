#nullable enable
using System.Numerics;
using Kassyi.GSharp.Compiler.Analysis.Checks;
using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Compiler.Transpiler.Helpers.Numeric;
using Microsoft.CodeAnalysis;
using Names = Kassyi.GSharp.Compiler.GSharpInternalNames.IntegerSemantics;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Managed;

/// <summary>
/// Unit tests for <see cref="ManagedDivision"/> decision functions and facts (#1561, #718).
/// </summary>
[Category(TestCategory.Compiler)]
public class ManagedDivisionFactsTests
{
    private static readonly Compilation s_compilation = RoslynTestUtility.CreateCompilation("");
    private static readonly ITypeSymbol s_objectType =
        s_compilation.GetSpecialType(SpecialType.System_Object);
    private static readonly ITypeSymbol s_intType =
        s_compilation.GetSpecialType(SpecialType.System_Int32);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void MayNeedHelper_Int32WithUnknownRanges_IsTrue()
    {
        // Both operands unknown, spanning the full Int32 range including int.MinValue and -1
        Assert.True(ManagedDivision.MayNeedHelper(SpecialType.System_Int32, null, null));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void MayNeedHelper_DivisorWithoutMinusOne_IsFalse()
    {
        // Divisor range [1, 10] cannot be -1 (DivisionCheckRule.cs:46)
        Assert.False(ManagedDivision.MayNeedHelper(SpecialType.System_Int32, null, new IntegerRange(1, 10)));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void MayNeedHelper_DividendWithoutTheMinimum_IsFalse()
    {
        // Dividend range [0, 100] cannot be long.MinValue (DivisionCheckRule.cs:47)
        Assert.False(ManagedDivision.MayNeedHelper(SpecialType.System_Int64, new IntegerRange(0, 100), null));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void MayNeedHelper_UnsignedType_IsFalse()
    {
        // Unsigned types cannot overflow on division
        Assert.False(ManagedDivision.MayNeedHelper(SpecialType.System_UInt32, null, null));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void DecideBinary_Int64Remainder_IsTheRemainderCall()
    {
        var plan = ManagedDivision.DecideBinary(divide: false, SpecialType.System_Int64);
        Assert.Equal(new ManagedDivisionPlan(Names.RemainderInt64, ManagedDivisionForm.Call), plan);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void DecideBinary_UnsignedType_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => ManagedDivision.DecideBinary(true, SpecialType.System_UInt32));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void DecideCompound_LocationNotWrittenByTheValue_AssignsByReference()
    {
        var facts = new ManagedCompoundDivisionFacts(
            Divide: true,
            Type: SpecialType.System_Int32,
            Target: ManagedDivisionTarget.Location,
            ValueWritesTarget: false,
            RepeatableProperty: false,
            Receiver: ManagedPropertyReceiver.None,
            ReceiverType: null,
            PropertyType: null);

        var plan = ManagedDivision.DecideCompound(facts);
        Assert.Equal(new ManagedDivisionPlan(Names.DivideAssignInt32, ManagedDivisionForm.AssignByReference), plan);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void DecideCompound_ByValueVariableWrittenByTheValue_Reassigns()
    {
        var facts = new ManagedCompoundDivisionFacts(
            Divide: false,
            Type: SpecialType.System_Int64,
            Target: ManagedDivisionTarget.ByValueVariable,
            ValueWritesTarget: true,
            RepeatableProperty: false,
            Receiver: ManagedPropertyReceiver.None,
            ReceiverType: null,
            PropertyType: null);

        var plan = ManagedDivision.DecideCompound(facts);
        Assert.Equal(new ManagedDivisionPlan(Names.RemainderInt64, ManagedDivisionForm.Reassign), plan);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void DecideCompound_LocationWrittenByTheValue_ReadsFirst()
    {
        var facts = new ManagedCompoundDivisionFacts(
            Divide: true,
            Type: SpecialType.System_Int32,
            Target: ManagedDivisionTarget.Location,
            ValueWritesTarget: true,
            RepeatableProperty: false,
            Receiver: ManagedPropertyReceiver.None,
            ReceiverType: null,
            PropertyType: null);

        var plan = ManagedDivision.DecideCompound(facts);
        Assert.Equal(
            new ManagedDivisionPlan(
                Names.DivideStoreInt32,
                ManagedDivisionForm.ReadFirst,
                Reader: Names.ReadReferenceInt32),
            plan);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void DecideCompound_RepeatableProperty_Reassigns()
    {
        var facts = new ManagedCompoundDivisionFacts(
            Divide: true,
            Type: SpecialType.System_Int32,
            Target: ManagedDivisionTarget.Property,
            ValueWritesTarget: false,
            RepeatableProperty: true,
            Receiver: ManagedPropertyReceiver.None,
            ReceiverType: null,
            PropertyType: null);

        var plan = ManagedDivision.DecideCompound(facts);
        Assert.Equal(new ManagedDivisionPlan(Names.DivideInt32, ManagedDivisionForm.Reassign), plan);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void DecideCompound_PropertyOfAnObject_BindsTheObject()
    {
        var facts = new ManagedCompoundDivisionFacts(
            Divide: true,
            Type: SpecialType.System_Int32,
            Target: ManagedDivisionTarget.Property,
            ValueWritesTarget: false,
            RepeatableProperty: false,
            Receiver: ManagedPropertyReceiver.Object,
            ReceiverType: s_objectType,
            PropertyType: s_intType);

        var plan = ManagedDivision.DecideCompound(facts);
        Assert.Equal(
            new ManagedDivisionPlan(
                Names.DividePropertyStoreOf,
                ManagedDivisionForm.PropertyOfObject,
                Reader: Names.ReadPropertyOf,
                ReceiverType: s_objectType,
                ValueType: s_intType),
            plan);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void DecideCompound_PropertyOfAVariable_BindsByReference()
    {
        var facts = new ManagedCompoundDivisionFacts(
            Divide: false,
            Type: SpecialType.System_Int32,
            Target: ManagedDivisionTarget.Property,
            ValueWritesTarget: false,
            RepeatableProperty: false,
            Receiver: ManagedPropertyReceiver.Variable,
            ReceiverType: s_intType,
            PropertyType: s_intType);

        var plan = ManagedDivision.DecideCompound(facts);
        Assert.Equal(
            new ManagedDivisionPlan(
                Names.RemainderPropertyStore,
                ManagedDivisionForm.PropertyByReference,
                Reader: Names.ReadProperty,
                ReceiverType: s_intType,
                ValueType: s_intType),
            plan);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void DecideCompound_PropertyThatCannotBeBound_KeepsTheOperator()
    {
        var facts = new ManagedCompoundDivisionFacts(
            Divide: true,
            Type: SpecialType.System_Int32,
            Target: ManagedDivisionTarget.Property,
            ValueWritesTarget: false,
            RepeatableProperty: false,
            Receiver: ManagedPropertyReceiver.None,
            ReceiverType: null,
            PropertyType: null);

        var plan = ManagedDivision.DecideCompound(facts);
        Assert.Null(plan);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void DecideCompound_OtherTarget_KeepsTheOperator()
    {
        var facts = new ManagedCompoundDivisionFacts(
            Divide: true,
            Type: SpecialType.System_Int32,
            Target: ManagedDivisionTarget.Other,
            ValueWritesTarget: false,
            RepeatableProperty: false,
            Receiver: ManagedPropertyReceiver.None,
            ReceiverType: null,
            PropertyType: null);

        var plan = ManagedDivision.DecideCompound(facts);
        Assert.Null(plan);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void DecideCompound_BoundPropertyWithoutItsTypes_Throws()
    {
        var facts = new ManagedCompoundDivisionFacts(
            Divide: true,
            Type: SpecialType.System_Int32,
            Target: ManagedDivisionTarget.Property,
            ValueWritesTarget: false,
            RepeatableProperty: false,
            Receiver: ManagedPropertyReceiver.Object,
            ReceiverType: null,
            PropertyType: null);

        Assert.Throws<InvalidOperationException>(() => ManagedDivision.DecideCompound(facts));
    }
}
