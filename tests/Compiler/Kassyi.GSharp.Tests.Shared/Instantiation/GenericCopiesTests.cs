#nullable enable

using Kassyi.GSharp.Compiler.Instantiation;
using Kassyi.GSharp.Compiler.Lowering;
using Microsoft.CodeAnalysis.Text;

namespace Kassyi.GSharp.Tests.Shared.Instantiation;

/// <summary>
/// Verifies the closed copies of generic declarations: the planned rewrites keep each call and operator bound to what
/// the declaration binds, and the check rejects a copy that binds something else.
/// </summary>
[Category(TestCategory.Compiler)]
public class GenericCopiesTests
{
    private static readonly string s_source = RoslynTestUtility.ReadTestCase("Instantiation/GenericCopySource.cs");

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

    // The probe that motivated the pinning: Helper(s) with T = int binds Helper(int) once the copy says int
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Check_InferredGenericCallWithoutItsPin_RejectsTheCopy()
    {
        var copy = CreateWithout("CallsHelper", "System.Int32", CopyRewriteKind.ExplicitTypeArguments);

        Assert.False(copy.IsFaithful);
        var finding = Assert.Single(copy.Findings, f => f.Reason.Contains("binds", StringComparison.Ordinal));
        Assert.Equal(LineOf("public static int CallsHelper<T>"), finding.Location.GetLineSpan().StartLinePosition.Line);
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

    // The second probe: d[0].Ext() with T = Odd binds Odd.Ext() once the copy says Odd
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Check_ExtensionCallWithoutItsPin_RejectsTheCopy()
    {
        var copy = CreateWithout("CallsExt", "App.Odd", CopyRewriteKind.StaticExtensionCall);

        Assert.False(copy.IsFaithful);
        Assert.Contains(copy.Findings, f => f.Reason.Contains("Odd.Ext", StringComparison.Ordinal));
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
    [InlineData("Locals", "System.Int32")]
    [InlineData("Recurse", "System.Int32")]
    public void Create_SupportedForm_GivesAFaithfulCopy(string method, string typeArgument)
    {
        var (copy, _) = CreateCopy(method, typeArgument);

        Assert.True(copy.IsFaithful, Describe(copy));
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("Sum", "App.Odd", "implements", "a + b")]
    [InlineData("ShiftAssign", "System.Byte", "compound shift", "a <<= n")]
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

    // A sibling member of a generic type is not pinned to the closed type yet: the check stops the copy
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Create_SiblingOfGenericType_IsRejectedNotEmitted()
    {
        var compilation = CreateCompilation();
        var outer = compilation.GetTypeByMetadataName("App.Outer`1") ?? throw new InvalidOperationException("No Outer.");
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
    [InlineData("Locals", "System.Int32")]
    public void Create_EveryNodeOfTheCopy_CarriesAnOriginalSpanInsideTheDeclaration(string method, string typeArgument)
    {
        var (copy, _) = CreateCopy(method, typeArgument);
        var declaration = Assert.IsAssignableFrom<MethodDeclarationSyntax>(copy.Declaration);
        var original = DeclarationSpan(method);

        foreach (var node in declaration.DescendantNodesAndSelf())
        {
            var annotation = Assert.Single(node.GetAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind));
            Assert.True(LoweringAnnotations.TryParseOriginalSpanData(annotation.Data, out var start, out var length, out _, out _));
            Assert.True(original.Contains(new TextSpan(start, length)), $"{node.Kind()} points outside the declaration");
        }
    }

    // The emitted copy computes what the generic method computes, for every input of the small types: the narrowing
    // forms, the shift masks and the checked forms (exceptions included)
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Sum", "System.Byte")]
    [InlineData("Sum", "System.SByte")]
    [InlineData("CheckedSum", "System.SByte")]
    [InlineData("CheckedSum", "System.Byte")]
    [InlineData("CheckedMultiply", "System.Int16")]
    [InlineData("CheckedMultiply", "System.UInt16")]
    [InlineData("CheckedDivide", "System.SByte")]
    [InlineData("CheckedDivide", "System.Int16")]
    [InlineData("Negate", "System.SByte")]
    [InlineData("Negate", "System.Byte")]
    [InlineData("Negate", "System.UInt32")]
    [InlineData("CheckedNegate", "System.SByte")]
    [InlineData("CheckedNegate", "System.UInt64")]
    [InlineData("Complement", "System.SByte")]
    [InlineData("Complement", "System.Char")]
    [InlineData("Shift", "System.Byte")]
    [InlineData("Shift", "System.Int16")]
    [InlineData("ShiftRight", "System.SByte")]
    [InlineData("ShiftRightUnsigned", "System.SByte")]
    [InlineData("ShiftRightUnsigned", "System.Int16")]
    [InlineData("ShiftRightUnsigned", "System.Char")]
    [InlineData("Identities", "System.Int32")]
    [InlineData("Identities", "System.Double")]
    public void Emit_NarrowedOperator_ComputesWhatGenericMathComputes(string method, string typeArgument)
    {
        var (copy, set) = CreateCopy(method, typeArgument);
        Assert.True(copy.IsFaithful, Describe(copy));
        var copyName = Assert.IsAssignableFrom<IMethodSymbol>(copy.Symbol).Name;
        using var stream = new MemoryStream();
        var emitted = set.Compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));

#pragma warning disable RS0030 // Reflection calls the assembly this test compiled at run time (test-architecture 3.10, case 9)
        var kernels = System.Reflection.Assembly.Load(stream.ToArray()).GetType("App.Kernels") ??
                      throw new InvalidOperationException("No App.Kernels in the emitted assembly.");
        var type = Type.GetType(typeArgument) ?? throw new InvalidOperationException(typeArgument);
        var generic = kernels.GetMethod(method)?.MakeGenericMethod(type) ?? throw new InvalidOperationException(method);
        var closed = kernels.GetMethod(copyName) ?? throw new InvalidOperationException(copyName);
        var takesCount = generic.GetParameters() is [_, { ParameterType: var second }] && second == typeof(int);
        var arity = generic.GetParameters().Length;
#pragma warning restore RS0030

        var compared = 0;
        foreach (var arguments in Inputs(type, arity, takesCount))
        {
            Assert.Equal(Run(generic, arguments), Run(closed, arguments));
            compared++;
        }

        Assert.True(compared >= (arity == 0 ? 1 : 100), $"only {compared} inputs");
    }

    private static string Run(System.Reflection.MethodBase method, object[] arguments)
    {
        try
        {
#pragma warning disable RS0030 // Reflection calls the assembly this test compiled at run time (test-architecture 3.10, case 9)
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
            : type == typeof(byte) || type == typeof(sbyte) ? values
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
            return Enumerable.Range(0, 65536).Where(i => i % 61 == 0 || i < 8 || i > 65528).Select(i => (object)(short)i);
        }

        if (type == typeof(ushort))
        {
            return Enumerable.Range(0, 65536).Where(i => i % 61 == 0 || i < 8 || i > 65528).Select(i => (object)(ushort)i);
        }

        if (type == typeof(char))
        {
            return Enumerable.Range(0, 65536).Where(i => i % 61 == 0 || i < 8 || i > 65528).Select(i => (object)(char)i);
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

        throw new InvalidOperationException($"No inputs for {type}.");
    }

    private static (GenericCopy Copy, GenericCopySet Set) CreateCopy(string method, string typeArgument)
    {
        var compilation = CreateCompilation();
        var type = compilation.GetTypeByMetadataName(typeArgument) ??
                   throw new InvalidOperationException($"No type '{typeArgument}'.");
        return CreateCopy(compilation, method, type);
    }

    private static (GenericCopy Copy, GenericCopySet Set) CreateCopy(Compilation compilation, string method, ITypeSymbol type)
    {
        var set = GenericCopies.Create(compilation, [Instance(compilation, method, type)]);
        return (set.Copies.Single(), set);
    }

    private static GenericCopy CreateWithout(string method, string typeArgument, CopyRewriteKind kind)
    {
        var compilation = CreateCompilation();
        var type = compilation.GetTypeByMetadataName(typeArgument) ??
                   throw new InvalidOperationException($"No type '{typeArgument}'.");
        var plan = GenericCopies.Plan(compilation, Instance(compilation, method, type));
        var pinned = plan.Rewrites.Values.Where(r => r.Kind == kind).Select(r => r.NodeIndex).ToArray();
        Assert.NotEmpty(pinned);

        return GenericCopies.Create(compilation, [plan with { Rewrites = plan.Rewrites.RemoveRange(pinned) }]).Copies.Single();
    }

    private static GenericInstance Instance(Compilation compilation, string method, ITypeSymbol type)
    {
        var kernels = compilation.GetTypeByMetadataName("App.Kernels") ?? throw new InvalidOperationException("No Kernels.");
        var definition = kernels.GetMembers(method).OfType<IMethodSymbol>().Single(m => m.IsGenericMethod);
        return GenericInstance.Of(definition.Construct(type));
    }

    private static TNode Single<TNode>(GenericCopy copy) where TNode : SyntaxNode =>
        Assert.Single(Assert.IsAssignableFrom<MethodDeclarationSyntax>(copy.Declaration).DescendantNodes().OfType<TNode>());

    private static ISymbol? SymbolOf(GenericCopySet set, SyntaxNode node) => ModelOf(set, node).GetSymbolInfo(node).Symbol;

    private static SemanticModel ModelOf(GenericCopySet set, SyntaxNode node) =>
        set.Compilation.GetSemanticModel(node.SyntaxTree);

    private static int LineOf(string marker)
    {
        var text = SourceText.From(s_source);
        return text.Lines.First(line => text.ToString(line.Span).Contains(marker, StringComparison.Ordinal)).LineNumber;
    }

    private static TextSpan DeclarationSpan(string method)
    {
        var tree = CreateCompilation().SyntaxTrees.Single();
        return tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.ValueText == method).Span;
    }

    private static string Describe(GenericCopy copy) =>
        string.Join("; ", copy.Findings.Select(f => $"{f.Location.GetLineSpan().StartLinePosition}: {f.Reason}"));

    private static CSharpCompilation CreateCompilation() =>
        RoslynTestUtility.CreateCompilation(s_source)
            .WithAssemblyName("GenericCopiesTests")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
}
