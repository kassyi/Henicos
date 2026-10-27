namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// <see cref="StructTranspiler.SubstituteTypeParameters"/> replaces the type parameters of a constructed struct in the
/// type of one of its members, also inside pointers, arrays and type arguments, and returns a type without them as it is.
/// </summary>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class StructTypeSubstitutionTests
{
    private static readonly Lazy<Compilation> s_compilation = new(static () =>
    {
        var compilation = RoslynTestUtility.CreateCompilation(
            RoslynTestUtility.ReadTestCase("Transpiler/StructTranspiler/GenericSubstitution.cs"));
        var errors = compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        return compilation;
    });

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Plain", "float")]
    [InlineData("Pointer", "float*")]
    [InlineData("Array", "float[]")]
    [InlineData("Nested", "Kassyi.GSharp.Tests.TestCases.StructTranspiler.Inner<float>")]
    [InlineData("SecondArgument", "Kassyi.GSharp.Tests.TestCases.StructTranspiler.Pair<int, float>")]
    public void SubstituteTypeParameters_MemberOfConstructedStruct_TakesTheTypeArgument(string field, string expected)
    {
        var closed = ParameterType("closed");

        var actual = StructTranspiler.SubstituteTypeParameters(DefinitionFieldType(field), closed, s_compilation.Value);

        Assert.Equal(expected, actual?.ToDisplayString());
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Closed")]
    [InlineData("Scalar")]
    [InlineData("PlainArray")]
    [InlineData("PlainPointer")]
    public void SubstituteTypeParameters_TypeWithoutTypeParameter_ReturnsTheSameSymbol(string field)
    {
        var type = DefinitionFieldType(field);

        Assert.Same(type, StructTranspiler.SubstituteTypeParameters(type, ParameterType("closed"), s_compilation.Value));
    }

    // Without a compilation, a pointer or an array cannot be constructed again and stays as it is
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Pointer")]
    [InlineData("Array")]
    public void SubstituteTypeParameters_PointerOrArrayWithoutCompilation_ReturnsTheSameSymbol(string field)
    {
        var type = DefinitionFieldType(field);

        Assert.Same(type, StructTranspiler.SubstituteTypeParameters(type, ParameterType("closed")));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void SubstituteTypeParameters_EachTypeParameter_TakesItsOwnTypeArgument()
    {
        var pair = ParameterType("pair");
        var definition = pair.OriginalDefinition;

        Assert.Equal("int", StructTranspiler.SubstituteTypeParameters(FieldType(definition, "A"), pair)?.ToDisplayString());
        Assert.Equal("float", StructTranspiler.SubstituteTypeParameters(FieldType(definition, "B"), pair)?.ToDisplayString());
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void SubstituteTypeParameters_TypeParameterOfAnotherSymbol_ReturnsTheSameSymbol()
    {
        var methodTypeParameter = AnyParameterType("methodTypeParameter");

        Assert.Same(methodTypeParameter, StructTranspiler.SubstituteTypeParameters(methodTypeParameter, ParameterType("closed")));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void SubstituteTypeParameters_DefinitionOrNonGenericStruct_ReturnsTheSameSymbol()
    {
        var plain = DefinitionFieldType("Plain");
        var definition = ParameterType("closed").OriginalDefinition;

        Assert.Same(plain, StructTranspiler.SubstituteTypeParameters(plain, definition, s_compilation.Value));
        Assert.Same(plain, StructTranspiler.SubstituteTypeParameters(plain, ParameterType("plain"), s_compilation.Value));
        Assert.Same(plain, StructTranspiler.SubstituteTypeParameters(plain, null, s_compilation.Value));
        Assert.Null(StructTranspiler.SubstituteTypeParameters(null, ParameterType("closed"), s_compilation.Value));
    }

    private static INamedTypeSymbol ParameterType(string name) =>
        AnyParameterType(name) as INamedTypeSymbol ?? throw new InvalidOperationException(name + " is not a named type.");

    private static ITypeSymbol AnyParameterType(string name) =>
        UsesMethod().Parameters.Single(p => p.Name == name).Type;

    private static ITypeSymbol DefinitionFieldType(string field) =>
        FieldType(ParameterType("closed").OriginalDefinition, field);

    private static ITypeSymbol FieldType(INamedTypeSymbol type, string field) =>
        type.GetMembers(field).OfType<IFieldSymbol>().Single().Type;

    private static IMethodSymbol UsesMethod()
    {
        var uses = s_compilation.Value.GetTypeByMetadataName("Kassyi.GSharp.Tests.TestCases.StructTranspiler.Uses")
            ?? throw new InvalidOperationException("Uses is not declared.");
        return uses.GetMembers("Generic").OfType<IMethodSymbol>().Single();
    }
}
