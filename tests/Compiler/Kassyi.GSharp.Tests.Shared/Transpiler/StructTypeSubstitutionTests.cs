using static Kassyi.GSharp.Compiler.Transpiler.StructTypeSubstitution;

namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// <see cref="StructTypeSubstitution.SubstituteTypeParameters"/> replaces the type parameters of a constructed struct
/// in the type of one of its members, also inside pointers, arrays and type arguments, and returns a type without them
/// as it is.
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
    [InlineData("Plain")]
    [InlineData("Pointer")]
    [InlineData("Array")]
    [InlineData("Nested")]
    [InlineData("SecondArgument")]
    public void SubstituteTypeParameters_MemberOfConstructedStruct_TakesTheTypeArgument(string field)
    {
        var closed = ParameterType("closed");

        var actual = SubstituteTypeParameters(DefinitionFieldType(field), closed, s_compilation.Value);

        Assert.Equal(ExpectedType(field), actual, SymbolEqualityComparer.Default);
    }

    private static ITypeSymbol ExpectedType(string field)
    {
        var compilation = s_compilation.Value;
        var single = compilation.GetSpecialType(SpecialType.System_Single);
        var int32 = compilation.GetSpecialType(SpecialType.System_Int32);
        return field switch
        {
            "Plain" => single,
            "Pointer" => compilation.CreatePointerTypeSymbol(single),
            "Array" => compilation.CreateArrayTypeSymbol(single),
            "Nested" => GetGenericType("Inner`1").Construct(single),
            "SecondArgument" => GetGenericType("Pair`2").Construct(int32, single),
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, null)
        };

        INamedTypeSymbol GetGenericType(string metadataName)
        {
            var fullName = "Kassyi.GSharp.Tests.TestCases.StructTranspiler." + metadataName;
            return compilation.GetTypeByMetadataName(fullName) ?? throw new InvalidOperationException(fullName);
        }
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

        Assert.Same(type, SubstituteTypeParameters(type, ParameterType("closed"), s_compilation.Value));
    }

    // Without a compilation, a pointer or an array cannot be constructed again and stays as it is
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Pointer")]
    [InlineData("Array")]
    public void SubstituteTypeParameters_PointerOrArrayWithoutCompilation_ReturnsTheSameSymbol(string field)
    {
        var type = DefinitionFieldType(field);

        Assert.Same(type, SubstituteTypeParameters(type, ParameterType("closed")));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void SubstituteTypeParameters_EachTypeParameter_TakesItsOwnTypeArgument()
    {
        var pair = ParameterType("pair");
        var definition = pair.OriginalDefinition;

        var compilation = s_compilation.Value;
        Assert.Equal(
            compilation.GetSpecialType(SpecialType.System_Int32),
            SubstituteTypeParameters(FieldType(definition, "A"), pair),
            SymbolEqualityComparer.Default
        );
        Assert.Equal(
            compilation.GetSpecialType(SpecialType.System_Single),
            SubstituteTypeParameters(FieldType(definition, "B"), pair),
            SymbolEqualityComparer.Default
        );
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void SubstituteTypeParameters_TypeParameterOfAnotherSymbol_ReturnsTheSameSymbol()
    {
        var methodTypeParameter = AnyParameterType("methodTypeParameter");

        Assert.Same(methodTypeParameter, SubstituteTypeParameters(methodTypeParameter, ParameterType("closed")));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void SubstituteTypeParameters_DefinitionOrNonGenericStruct_ReturnsTheSameSymbol()
    {
        var plain = DefinitionFieldType("Plain");
        var definition = ParameterType("closed").OriginalDefinition;

        Assert.Same(plain, SubstituteTypeParameters(plain, definition, s_compilation.Value));
        Assert.Same(plain, SubstituteTypeParameters(plain, ParameterType("plain"), s_compilation.Value));
        Assert.Same(plain, SubstituteTypeParameters(plain, null, s_compilation.Value));
        Assert.Null(SubstituteTypeParameters(null, ParameterType("closed"), s_compilation.Value));
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
