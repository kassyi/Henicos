#nullable enable

using Kassyi.GSharp.Compiler.Instantiation;
using static Kassyi.GSharp.Tests.Shared.Instantiation.GenericCopyValueSupport;

namespace Kassyi.GSharp.Tests.Shared.Instantiation;

/// <summary>
/// Runs the closed copies of generic declarations: an assembly compiled from the derived compilation computes the same
/// values with the copy as with the generic method closed over the type argument, exceptions included.
/// </summary>
[Category(TestCategory.Compiler)]
public class GenericCopyValueTests
{
    // A call through the constraint that runs an explicit or a default implementation goes through a helper that is
    // generic over the receiver: the implementation that runs is the one the generic method runs, and the receiver is
    // passed by reference, so a member that changes it changes it (the second Next of a counter gives 2, not 1)
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("CallFoo", "App.Both", "2")]
    [InlineData("CallFooOfValue", "App.Both", "2")]
    [InlineData("CallFoo", "App.Plain", "5")]
    [InlineData("CallDefault", "App.Defaulted", "3")]
    [InlineData("NextTwice", "App.Counter", "2")]
    public void Emit_CallThroughTheConstraint_RunsTheImplementationOfTheGenericMethod(
        string method,
        string typeArgument,
        string expected)
    {
        var loaded = Load(method, typeArgument);
        var value = CreateDefault(loaded.Type);

        Assert.Equal(expected, Run(loaded.Generic, [value]));
        Assert.Equal(expected, Run(loaded.Closed, [value]));
    }

    // ref and out-like parameters of the interface member pass through the helper, and an array element is a variable
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Emit_CallWithRefArgumentThroughTheConstraint_ChangesTheReceiverAndTheArgument()
    {
        var loaded = Load("AddAndPlus", "App.Counter");
        var arguments = new[] { CreateDefault(loaded.Type), 3 };

        Assert.Equal("913", Run(loaded.Generic, arguments));
        Assert.Equal("913", Run(loaded.Closed, [CreateDefault(loaded.Type), 3]));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Emit_CallOnArrayElementsThroughTheConstraint_ChangesTheElements()
    {
        var loaded = Load("NextInArray", "App.Counter");

        Assert.Equal("3", Run(loaded.Generic, [CreateArray(loaded.Type)]));
        Assert.Equal("3", Run(loaded.Closed, [CreateArray(loaded.Type)]));
    }

    // The emitted copy computes what the generic method computes, for every input of the small types: the narrowing
    // forms, the shift masks and the checked forms (exceptions included)
    // The generic methods that are run with a type of 8 or 16 bits: every value of the 8-bit types, a spread of the
    // 16-bit ones with the corners of each
    public static TheoryData<string, string> SmallTypeCases { get; } = new()
    {
        { "Sum", "System.Byte" },
        { "Sum", "System.SByte" },
        { "CheckedSum", "System.SByte" },
        { "CheckedSum", "System.Byte" },
        { "CheckedMultiply", "System.Int16" },
        { "CheckedMultiply", "System.UInt16" },
        { "CheckedDivide", "System.SByte" },
        { "CheckedDivide", "System.Int16" },
        { "Negate", "System.SByte" },
        { "Negate", "System.Byte" },
        { "CheckedNegate", "System.SByte" },
        { "Complement", "System.SByte" },
        { "Complement", "System.Char" },
        { "Shift", "System.Byte" },
        { "Shift", "System.Int16" },
        { "ShiftRight", "System.SByte" },
        { "ShiftRightUnsigned", "System.SByte" },
        { "ShiftRightUnsigned", "System.Int16" },
        { "ShiftRightUnsigned", "System.Char" },
        { "CompoundDivide", "System.SByte" },
        { "CompoundDivide", "System.Int16" },
        { "CheckedCompoundDivide", "System.SByte" },
        { "CheckedCompoundDivide", "System.Int16" },
        { "CheckedCompoundDivide", "System.Byte" },
        { "CheckedCompoundAdd", "System.SByte" },
        { "CheckedCompoundAdd", "System.UInt16" },
        { "CheckedCompoundAdd", "System.Char" },
        { "CheckedCompoundAdd", "System.Int16" },
        { "Less", "System.Byte" },
        { "Less", "System.Char" },
        { "ShiftAssign", "System.Byte" },
        { "ShiftAssign", "System.SByte" },
        { "ShiftAssign", "System.Int16" },
        { "ShiftAssign", "System.Char" },
        { "ShiftAssignRightUnsigned", "System.SByte" },
        { "ShiftAssignRightUnsigned", "System.Int16" },
        { "Increment", "System.Byte" },
        { "CheckedIncrement", "System.SByte" },
        { "CheckedIncrement", "System.UInt16" },
        { "MinValue", "System.SByte" },
        { "ShiftedMaxValue", "System.Byte" },
        { "ShiftedMaxValue", "System.UInt16" },
        { "NegatedMaxValue", "System.Byte" },
        { "NegatedMaxValue", "System.UInt16" },
        { "MaxValue", "System.Int16" },
        { "Abs", "System.Byte" },
        { "Abs", "System.Char" },
        { "Abs", "System.Int16" },
        { "AbsPlusB", "System.Byte" },
        { "AbsPlusB", "System.Char" },
        { "AbsLessB", "System.Byte" },
        { "AbsLessB", "System.Char" },
        { "CompoundAddAbs", "System.Byte" },
        { "CompoundAddAbs", "System.Char" },
        { "SignOfAbs", "System.Char" },
        { "MaxOfAbs", "System.Char" },
        { "MaxMagnitude", "System.UInt16" },
        { "MaxMagnitude", "System.Char" },
        { "MaxMagnitude", "System.SByte" },
        { "MinMagnitude", "System.Char" },
        { "IsNaN", "System.Byte" },
        { "IsNaN", "System.Char" },
        { "Sign", "System.Char" },
        { "Max", "System.Char" },
        { "Min", "System.Char" },
        { "OneOverZero", "System.Byte" },
        { "CheckedMaxValuePlusOne", "System.Byte" },
        { "CompareTo", "System.Char" },
    };

    // The same with the integer types of 32 bits or more: the corners of each width
    public static TheoryData<string, string> WideTypeCases { get; } = new()
    {
        { "Negate", "System.UInt32" },
        { "CheckedNegate", "System.UInt64" },
        { "Identities", "System.Int32" },
        { "MaxValue", "System.UInt64" },
        { "Abs", "System.UInt32" },
        { "MaxMagnitude", "System.UInt32" },
        { "IsNaN", "System.Int32" },
        { "Sign", "System.Int64" },
        { "OneOverZero", "System.Int32" },
        { "CheckedMaxValuePlusOne", "System.Int32" },
        { "CheckedMaxValuePlusOne", "System.Int64" },
        { "CompareTo", "System.Int32" },
        { "Sum", "System.Int32" },
        { "Sum", "System.Int64" },
        { "Sum", "System.IntPtr" },
        { "Sum", "System.UIntPtr" },
        { "CheckedSum", "System.Int64" },
        { "CheckedSum", "System.UIntPtr" },
        { "Negate", "System.Int64" },
        { "Negate", "System.UInt64" },
        { "Negate", "System.UIntPtr" },
        { "CheckedNegate", "System.Int32" },
        { "CheckedDivide", "System.Int64" },
        { "CheckedMultiply", "System.Int32" },
        { "Shift", "System.UInt32" },
        { "ShiftRightUnsigned", "System.Int64" },
    };

    // The same with the floating point types and decimal: NaN, infinities, signed zero, the corners
    public static TheoryData<string, string> RealTypeCases { get; } = new()
    {
        { "Identities", "System.Double" },
        { "Identities", "System.Decimal" },
        { "MinValue", "System.Single" },
        { "MaxValue", "System.Decimal" },
        { "Pi", "System.Single" },
        { "Pi", "System.Double" },
        { "IsNaN", "System.Single" },
        { "IsNaN", "System.Decimal" },
        { "Max", "System.Double" },
        { "Min", "System.Single" },
        { "OneOverZero", "System.Decimal" },
        { "Sum", "System.Single" },
        { "Sum", "System.Double" },
        { "Sum", "System.Decimal" },
    };

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(SmallTypeCases))]
    [MemberData(nameof(WideTypeCases))]
    [MemberData(nameof(RealTypeCases))]
    public void Emit_Copy_ComputesWhatGenericMathComputes(string method, string typeArgument) =>
        GenericCopyValueSupport.AssertCopyComputesWhatTheDeclarationComputes(method, typeArgument);
}
