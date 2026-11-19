#nullable enable

using Kassyi.GSharp.Compiler.Instantiation;
using Kassyi.GSharp.Compiler.Lowering;
using Microsoft.CodeAnalysis.Text;
using static Kassyi.GSharp.Tests.Shared.Instantiation.GenericCopyTestSupport;

namespace Kassyi.GSharp.Tests.Shared.Instantiation;

/// <summary>
/// Verifies the closed copies of generic declarations: the planned rewrites keep each call and operator bound to what
/// the declaration binds, and the copies compute what the generic methods compute.
/// </summary>
[Category(TestCategory.Compiler)]
public class GenericCopiesTests
{
    // The smallest set of values of a type (nuint) that a method is run with
    private const int MinimumInputs = 9;

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Create_InferredGenericCall_KeepsTheGenericOverload()
    {
        var (copy, set) = CreateCopy("CallsHelper", "System.Int32");

        Assert.True(copy.IsFaithful, Describe(copy));
        var call = Single<InvocationExpressionSyntax>(copy);
        var callee = Assert.IsAssignableFrom<IMethodSymbol>(SymbolOf(set, call));
        Assert.True(callee.IsGenericMethod);
        Assert.Equal(SpecialType.System_Int32, callee.TypeArguments[0].SpecialType);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Create_ExtensionCallOnTypeWithSameInstanceMember_KeepsTheExtension()
    {
        var (copy, set) = CreateCopy("CallsExt", "App.Odd");

        Assert.True(copy.IsFaithful, Describe(copy));
        var callee = Assert.IsAssignableFrom<IMethodSymbol>(SymbolOf(set, Single<InvocationExpressionSyntax>(copy)));
        Assert.Equal("Exts", callee.ContainingType.Name);
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Sum", "System.Int32")]
    [InlineData("Sum", "App.Vec")]
    [InlineData("Sum", "System.Byte")]
    [InlineData("Sum", "System.Double")]
    [InlineData("CheckedSum", "System.Int32")]
    [InlineData("CheckedSum", "App.Vec")]
    [InlineData("CheckedSum", "System.Byte")]
    [InlineData("Identities", "System.Int32")]
    [InlineData("Identities", "System.Single")]
    [InlineData("Max", "System.Int32")]
    [InlineData("Max", "System.Single")]
    [InlineData("Negate", "System.Int32")]
    [InlineData("Negate", "System.UInt32")]
    [InlineData("Negate", "System.UInt64")]
    [InlineData("Negate", "System.Byte")]
    [InlineData("CheckedNegate", "System.SByte")]
    [InlineData("Shift", "System.Int32")]
    [InlineData("Shift", "System.Byte")]
    [InlineData("ShiftRight", "System.SByte")]
    [InlineData("ShiftRightUnsigned", "System.Int16")]
    [InlineData("ShiftRightUnsigned", "System.Char")]
    [InlineData("CheckedDivide", "System.SByte")]
    [InlineData("CheckedMultiply", "System.UInt16")]
    [InlineData("Complement", "System.Byte")]
    [InlineData("ShiftAssign", "System.Int32")]
    [InlineData("ShiftAssign", "System.Byte")]
    [InlineData("ShiftAssignRightUnsigned", "System.SByte")]
    [InlineData("CompoundDivide", "System.SByte")]
    [InlineData("CheckedCompoundDivide", "System.Int16")]
    [InlineData("CompoundDivideAt", "System.SByte")]
    [InlineData("CheckedCompoundAdd", "System.Byte")]
    [InlineData("CheckedCompoundAdd", "System.Char")]
    [InlineData("CheckedCompoundAdd", "System.Int16")]
    [InlineData("Less", "System.Byte")]
    [InlineData("Less", "System.Char")]
    [InlineData("Increment", "System.Byte")]
    [InlineData("CheckedIncrement", "System.SByte")]
    [InlineData("MinValue", "System.Int32")]
    [InlineData("MaxValue", "System.UInt64")]
    [InlineData("MaxValue", "System.Char")]
    [InlineData("MinValue", "System.Decimal")]
    [InlineData("Pi", "System.Single")]
    [InlineData("Pi", "System.Double")]
    [InlineData("Identities", "System.Decimal")]
    [InlineData("Abs", "System.UInt32")]
    [InlineData("Abs", "System.Int16")]
    [InlineData("AbsPlusB", "System.Byte")]
    [InlineData("AbsPlusB", "System.Char")]
    [InlineData("AbsLessB", "System.Byte")]
    [InlineData("AbsLessB", "System.Char")]
    [InlineData("CompoundAddAbs", "System.Byte")]
    [InlineData("CompoundAddAbs", "System.Char")]
    [InlineData("SignOfAbs", "System.Char")]
    [InlineData("MaxOfAbs", "System.Char")]
    [InlineData("MaxMagnitude", "System.UInt64")]
    [InlineData("MaxMagnitude", "System.Char")]
    [InlineData("MinMagnitude", "System.Char")]
    [InlineData("IsNaN", "System.Int32")]
    [InlineData("IsNaN", "System.Double")]
    [InlineData("IsNaN", "System.Decimal")]
    [InlineData("Sign", "System.Char")]
    [InlineData("Max", "System.Char")]
    [InlineData("Min", "System.Char")]
    [InlineData("OneOverZero", "System.Int32")]
    [InlineData("CheckedMaxValuePlusOne", "System.Byte")]
    [InlineData("CompareTo", "System.Int32")]
    [InlineData("CompareTo", "System.Char")]
    [InlineData("CallFoo", "App.Plain")]
    [InlineData("CallFoo", "App.Both")]
    [InlineData("CallFooOfValue", "App.Both")]
    [InlineData("CallDefault", "App.Defaulted")]
    [InlineData("NextTwice", "App.Counter")]
    [InlineData("AddAndPlus", "App.Counter")]
    [InlineData("NextInArray", "App.Counter")]
    [InlineData("Sum", "System.Int64")]
    [InlineData("Sum", "System.IntPtr")]
    [InlineData("Sum", "System.Decimal")]
    [InlineData("CheckedSum", "System.UIntPtr")]
    [InlineData("Locals", "System.Int32")]
    [InlineData("Recurse", "System.Int32")]
    [InlineData("AbsOfAbs", "System.Byte")]
    [InlineData("AbsOfAbs", "System.Char")]
    [InlineData("AbsOfAbs", "System.UInt32")]
    [InlineData("AbsOfAbs", "System.UInt64")]
    [InlineData("AbsOfAbs", "System.UIntPtr")]
    [InlineData("AbsOfAbs", "System.Int32")]
    [InlineData("AbsOfSum", "System.Byte")]
    [InlineData("AbsOfNegation", "System.UInt32")]
    [InlineData("AbsOfMaxValue", "System.UInt32")]
    [InlineData("AbsOfAbsPlusB", "System.Byte")]
    [InlineData("AbsOfMax", "System.Char")]
    public void Create_SupportedForm_GivesACopyThatCompiles(string method, string typeArgument)
    {
        var (copy, set) = CreateCopy(method, typeArgument);

        Assert.True(copy.IsFaithful, Describe(copy));
        var errors = ModelOf(set, copy.Declaration!)
            .GetDiagnostics(copy.Declaration!.Span)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.Id);
        Assert.Empty(errors);
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("Sum", "App.Odd", "implements", "a + b")]
    [InlineData("CompoundDivideWithEffect", "System.SByte", "has side effects", "data[i++] /= b")]
    public void Create_FormWithoutAClosedSpelling_ReportsItsNode(
        string method,
        string typeArgument,
        string reason,
        string expression)
    {
        var (copy, _) = CreateCopy(method, typeArgument);

        var finding = Assert.Single(copy.Findings);
        Assert.Contains(reason, finding.Reason, StringComparison.Ordinal);
        Assert.Equal(expression, SourceText.From(s_source).ToString(finding.Location.SourceSpan));
    }

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

    // A sibling member of a generic type is not pinned to the closed type yet: the check stops the copy
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Create_SiblingOfGenericType_IsRejectedNotEmitted()
    {
        var compilation = CreateCompilation();
        var outer = compilation.GetTypeByMetadataName("App.Outer`1") ??
                    throw new InvalidOperationException("No Outer.");
        var run = outer.Construct(compilation.GetSpecialType(SpecialType.System_Int32)).GetTypeMembers("Kernels")[0]
            .GetMembers("Run").OfType<IMethodSymbol>().Single();

        var copy = GenericCopies.Create(compilation, [GenericInstance.Of(run)]).Copies.Single();

        Assert.False(copy.IsFaithful);
        Assert.Contains(copy.Findings, f => f.Reason.Contains("does not compile", StringComparison.Ordinal));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Create_NameOfTypeParameter_KeepsTheName()
    {
        var (copy, set) = CreateCopy("Name", "System.Int32");

        Assert.True(copy.IsFaithful, Describe(copy));
        var literal = Single<LiteralExpressionSyntax>(copy);
        Assert.Equal("T", ModelOf(set, literal).GetConstantValue(literal).Value);
    }

    // Diagnostics on a copy are reported at the user's source, so every node needs the original span
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("CallsExt", "App.Odd")]
    [InlineData("CheckedNegate", "System.SByte")]
    [InlineData("ShiftRightUnsigned", "System.Int16")]
    [InlineData("Identities", "System.Int32")]
    [InlineData("OneOverZero", "System.Int32")]
    [InlineData("CompoundDivide", "System.SByte")]
    [InlineData("MaxMagnitude", "System.Char")]
    [InlineData("Locals", "System.Int32")]
    public void Create_EveryNodeOfTheCopy_CarriesAnOriginalSpanInsideTheDeclaration(string method, string typeArgument)
    {
        var (copy, _) = CreateCopy(method, typeArgument);
        var declaration = Assert.IsAssignableFrom<MethodDeclarationSyntax>(copy.Declaration);
        var original = DeclarationSpan(method);

        foreach (var node in declaration.DescendantNodesAndSelf())
        {
            var annotation = Assert.Single(node.GetAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind));
            Assert.True(LoweringAnnotations.TryParseOriginalSpanData(
                annotation.Data, out var start, out var length, out _, out _));
            Assert.True(original.Contains(new TextSpan(start, length)),
                $"{node.Kind()} points outside the declaration");
        }
    }

    // The copy keeps the node of the inner call of T.Abs(T.Abs(a)): the outer call is a new parenthesized node around
    // the inner one, which keeps its own number
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("System.Byte")]
    [InlineData("System.UInt32")]
    [InlineData("System.UIntPtr")]
    public void Create_NestedArgumentCall_KeepsTheNodeOfTheInnerCall(string typeArgument)
    {
        var (copy, set) = CreateCopy("AbsOfAbs", typeArgument);
        var type = set.Compilation.GetTypeByMetadataName(typeArgument) ?? throw new InvalidOperationException(typeArgument);
        var declaration = Assert.IsAssignableFrom<MethodDeclarationSyntax>(
            Instance(set.Compilation, "AbsOfAbs", type).Definition.DeclaringSyntaxReferences.Single().GetSyntax());
        var nodes = CopyNodeIndex.List(declaration);
        var calls = declaration.DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();
        Assert.Equal(2, calls.Length);
        var outer = Array.IndexOf(nodes, calls[0]);
        var inner = Array.IndexOf(nodes, calls[1]);

        var innerNode = Assert.Single(
            Assert.IsAssignableFrom<MethodDeclarationSyntax>(copy.Declaration).DescendantNodes(),
            node => CopyNodeIndex.TryGet(node, out var index) && index == inner);
        Assert.IsType<ParenthesizedExpressionSyntax>(innerNode);
        var parent = Assert.IsType<ParenthesizedExpressionSyntax>(innerNode.Parent);
        Assert.True(CopyNodeIndex.TryGet(parent, out var parentIndex) && parentIndex == outer);
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
        { "AbsOfAbs", "System.Byte" },
        { "AbsOfSum", "System.Byte" },
        { "AbsOfAbsPlusB", "System.Byte" },
        { "AbsOfMax", "System.Char" },
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
        { "AbsOfAbs", "System.UInt64" },
        { "AbsOfNegation", "System.UInt32" },
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
    public void Emit_Copy_ComputesWhatGenericMathComputes(string method, string typeArgument)
    {
        var loaded = Load(method, typeArgument);
        var arity = loaded.Generic.GetParameters().Length;

        var compared = 0;
        foreach (var arguments in Inputs(loaded.Type, arity, loaded.TakesCount))
        {
            Assert.Equal(Run(loaded.Generic, arguments), Run(loaded.Closed, arguments));
            compared++;
        }

        Assert.True(compared >= (arity == 0 ? 1 : MinimumInputs), $"only {compared} inputs");
    }

    // The generic method closed over the type argument, and the closed copy, of an assembly compiled from the copies
    private static LoadedMethods Load(string method, string typeArgument)
    {
        var (copy, set) = CreateCopy(method, typeArgument);
        Assert.True(copy.IsFaithful, Describe(copy));
        var copyName = Assert.IsAssignableFrom<IMethodSymbol>(copy.Symbol).Name;
        using var stream = new MemoryStream();
        var emitted = set.Compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));

#pragma warning disable RS0030 // Reflection calls an assembly this test compiled at run time
        var assembly = System.Reflection.Assembly.Load(stream.ToArray());
        var kernels = assembly.GetType("App.Kernels") ??
                      throw new InvalidOperationException("No App.Kernels in the emitted assembly.");
        var type = Type.GetType(typeArgument) ?? assembly.GetType(typeArgument) ??
                   throw new InvalidOperationException(typeArgument);
        var definition = kernels.GetMethod(method) ?? throw new InvalidOperationException(method);
        var closed = kernels.GetMethod(copyName) ?? throw new InvalidOperationException(copyName);
        // A second parameter that is not of the type argument is a shift count
        var takesCount = definition.GetParameters() is [_, { ParameterType.IsGenericParameter: false }];
        return new LoadedMethods(type, definition.MakeGenericMethod(type), closed, takesCount);
#pragma warning restore RS0030
    }

    private static object CreateDefault(Type type)
    {
#pragma warning disable RS0030 // Reflection creates a value of a type this test compiled at run time
        return Activator.CreateInstance(type) ?? throw new InvalidOperationException(type.Name);
#pragma warning restore RS0030
    }

    // Two default values of the type, in an array
    private static Array CreateArray(Type type)
    {
        var items = Array.CreateInstance(type, 2);
        items.SetValue(CreateDefault(type), 0);
        items.SetValue(CreateDefault(type), 1);
        return items;
    }

    private sealed record LoadedMethods(
        Type Type,
        System.Reflection.MethodInfo Generic,
        System.Reflection.MethodInfo Closed,
        bool TakesCount);

    private static string Run(System.Reflection.MethodBase method, object[] arguments)
    {
        try
        {
#pragma warning disable RS0030 // Reflection calls an assembly this test compiled at run time
            return method.Invoke(null, arguments)?.ToString() ?? "null";
#pragma warning restore RS0030
        }
        catch (System.Reflection.TargetInvocationException e) when (e.InnerException is { } inner)
        {
            return inner.GetType().Name;
        }
    }

    // Every value of a byte-sized type, a spread of the others; shift counts from -40 to 40
    private static IEnumerable<object[]> Inputs(Type type, int arity, bool takesCount)
    {
        if (arity == 0)
        {
            return [[]];
        }

        var values = Values(type).ToArray();
        if (arity == 1)
        {
            return values.Select(v => new[] { v });
        }

        var second = takesCount ? Enumerable.Range(-40, 81).Select(n => (object)n).ToArray()
            : values.Length <= 300 ? values
            : values.Where((_, i) => i % 97 == 0).ToArray();
        return values.SelectMany(a => second.Select(b => new[] { a, b }));
    }

    private static IEnumerable<object> Values(Type type)
    {
        if (type == typeof(byte))
        {
            return Enumerable.Range(0, 256).Select(i => (object)(byte)i);
        }

        if (type == typeof(sbyte))
        {
            return Enumerable.Range(-128, 256).Select(i => (object)(sbyte)i);
        }

        if (type == typeof(short))
        {
            return Spread().Select(i => (object)(short)i);
        }

        if (type == typeof(ushort))
        {
            return Spread().Select(i => (object)(ushort)i);
        }

        if (type == typeof(char))
        {
            return Spread().Select(i => (object)(char)i);
        }

        if (type == typeof(uint))
        {
            return new uint[] { 0, 1, 2, 0x7FFFFFFF, 0x80000000, 0xFFFFFFFE, 0xFFFFFFFF }.Select(v => (object)v)
                .Concat(Enumerable.Range(0, 200).Select(i => (object)(uint)(i * 21474836)));
        }

        if (type == typeof(ulong))
        {
            return new ulong[] { 0, 1, 2, long.MaxValue, 1UL << 63, ulong.MaxValue }.Select(v => (object)v)
                .Concat(Enumerable.Range(0, 200).Select(i => (object)(ulong)(i * 92233720368547758L)));
        }

        if (type == typeof(int))
        {
            return new[]
            {
                int.MinValue, -123456789, -32769, -32768, -129, -128, -2, -1, 0, 1, 2, 7, 8, 9, 31, 32, 33, 127, 128,
                255, 256, 32767, 32768, 65535, 65536, 123456789, int.MaxValue - 1, int.MaxValue,
            }.Select(v => (object)v);
        }

        if (type == typeof(long))
        {
            return new[] { long.MinValue, int.MinValue - 1L, -2L, -1L, 0L, 1L, 2L, 63L, 64L, 65L, int.MaxValue + 1L,
                long.MaxValue - 1, long.MaxValue }.Select(v => (object)v);
        }

        if (type == typeof(nint))
        {
            return new nint[] { nint.MinValue, -2, -1, 0, 1, 2, 63, 64, 65, nint.MaxValue - 1, nint.MaxValue }
                .Select(v => (object)v);
        }

        if (type == typeof(nuint))
        {
            return new nuint[] { 0, 1, 2, 63, 64, 65, nuint.MaxValue / 2, nuint.MaxValue - 1, nuint.MaxValue }
                .Select(v => (object)v);
        }

        if (type == typeof(float))
        {
            return new[]
            {
                float.NaN, float.NegativeInfinity, float.MinValue, -1.5f, -1f, -0f, 0f, float.Epsilon, 0.5f, 1f, 2f,
                7.25f, float.MaxValue, float.PositiveInfinity,
            }.Select(v => (object)v);
        }

        if (type == typeof(double))
        {
            return new[]
            {
                double.NaN, double.NegativeInfinity, double.MinValue, -1.5, -1d, -0d, 0d, double.Epsilon, 0.5, 1d, 2d,
                7.25, double.MaxValue, double.PositiveInfinity,
            }.Select(v => (object)v);
        }

        if (type == typeof(decimal))
        {
            return new[] { decimal.MinValue, -1.5m, -1m, 0m, 0.5m, 1m, 2m, 7.25m, decimal.MaxValue }
                .Select(v => (object)v);
        }

        throw new InvalidOperationException($"No inputs for {type}.");
    }

    private static TNode Single<TNode>(GenericCopy copy) where TNode : SyntaxNode =>
        Assert.Single(Assert.IsAssignableFrom<MethodDeclarationSyntax>(copy.Declaration)
            .DescendantNodes()
            .OfType<TNode>());

    private static ISymbol? SymbolOf(GenericCopySet set, SyntaxNode node) =>
        ModelOf(set, node).GetSymbolInfo(node).Symbol;

    // Every 61st value of a 16-bit type, and its edges
    private static IEnumerable<int> Spread() =>
        Enumerable.Range(0, 65536).Where(i => i % 61 == 0 || i < 8 || i > 65528 || i is >= 32760 and <= 32775);

    private static SemanticModel ModelOf(GenericCopySet set, SyntaxNode node) =>
        set.Compilation.GetSemanticModel(node.SyntaxTree);

    private static TextSpan DeclarationSpan(string method)
    {
        var tree = CreateCompilation().SyntaxTrees.Single();
        return tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.ValueText == method).Span;
    }
}
