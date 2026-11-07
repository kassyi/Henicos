#nullable enable
namespace Kassyi.GSharp.Tests.Shared.Analysis;

/// <summary>
/// Verifies what <see cref="DeviceFunctionEffects"/> says a call of a function may write (#608): nothing, its ref / out
/// arguments, or memory.
/// </summary>
[Category(TestCategory.Compiler)]
public class DeviceFunctionEffectsTests
{
    private static readonly Compilation s_compilation = RoslynTestUtility.CreateCompilation(
        RoslynTestUtility.ReadTestCase("Analysis/DeviceFunctionEffectCases.cs")
    );

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Square")]
    [InlineData("LocalsOnly")]
    [InlineData("CallsNone")]
    [InlineData("ReadsMemory")]
    [InlineData("LambdaLocal")]
    [InlineData("InitializerOnly")]
    [InlineData("QuietUsing")]
    [InlineData("GetterRead")]
    [InlineData("ExtensionOnLocal")]
    [InlineData("CapturedParameter")]
    public void Test_Classify_WritesOnlyOwnLocals_IsNone(string name) =>
        Assert.Equal(DeviceFunctionEffect.None, Classify("EffectCases.Effects", name));

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Increment")]
    [InlineData("WriteOut")]
    [InlineData("BumpField")]
    [InlineData("ForwardsRef")]
    [InlineData("CarryIntrinsic")]
    [InlineData("CloseByExpression")]
    [InlineData("ExtensionWrite")]
    public void Test_Classify_WritesThroughRefParameters_IsRefParameters(string name) =>
        Assert.Equal(DeviceFunctionEffect.RefParameters, Classify("EffectCases.Effects", name));

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("WriteElement")]
    [InlineData("WriteStatic")]
    [InlineData("UsesSharedMemory")]
    [InlineData("Barrier")]
    [InlineData("Atomic")]
    [InlineData("CallsMemory")]
    [InlineData("ForwardsElement")]
    [InlineData("LoggingUsing")]
    [InlineData("SetterWrite")]
    [InlineData("PropertyIncrement")]
    [InlineData("PropertyPrefixDecrement")]
    [InlineData("ConversionWrite")]
    public void Test_Classify_WritesMemory_IsMemory(string name) =>
        Assert.Equal(DeviceFunctionEffect.Memory, Classify("EffectCases.Effects", name));

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Ping")]
    [InlineData("Pong")]
    public void Test_Classify_CallCycle_IsMemory(string name) =>
        Assert.Equal(DeviceFunctionEffect.Memory, Classify("EffectCases.Effects", name));

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Classify_StructMembers_FollowTheirReceiver()
    {
        Assert.Equal(DeviceFunctionEffect.None, ClassifyConstructor("EffectCases.Counter"));
        Assert.Equal(DeviceFunctionEffect.RefParameters, Classify("EffectCases.Counter", "Increment"));
        Assert.Equal(DeviceFunctionEffect.None, Classify("EffectCases.Counter", "Peek"));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Classify_PrimaryConstructor_IncludesMemberInitializers()
    {
        Assert.Equal(DeviceFunctionEffect.None, ClassifyConstructor("EffectCases.Seeded"));
        Assert.Equal(DeviceFunctionEffect.Memory, ClassifyConstructor("EffectCases.InitializerWrites"));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Classify_LocalFunction_WritesToACapturedParameterAreMemory()
    {
        Assert.Equal(DeviceFunctionEffect.None, ClassifyLocalFunction("BumpOwn"));
        Assert.Equal(DeviceFunctionEffect.Memory, ClassifyLocalFunction("BumpCaptured"));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Classify_ChainedConstructor_IncludesTheConstructorItCalls() =>
        Assert.Equal(DeviceFunctionEffect.Memory,
            DeviceFunctionEffects.Classify(
                GetType("EffectCases.Chained").InstanceConstructors.First(static c => c.Parameters.Length == 1),
                s_compilation));

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("System.Math", "Max", nameof(DeviceFunctionEffect.None))]
    [InlineData("System.MathF", "Sqrt", nameof(DeviceFunctionEffect.None))]
    [InlineData("System.Numerics.BitOperations", "PopCount", nameof(DeviceFunctionEffect.None))]
    [InlineData("Kassyi.GSharp.Core.Gpu", "AtomicAdd", nameof(DeviceFunctionEffect.Memory))]
    [InlineData("Kassyi.GSharp.Core.Gpu", "SyncThreads", nameof(DeviceFunctionEffect.Memory))]
    [InlineData("Kassyi.GSharp.Core.Gpu", "AddWithCarry", nameof(DeviceFunctionEffect.RefParameters))]
    public void Test_Classify_IntrinsicWithoutBody_ByDeclaration(string type, string name, string expected) =>
        Assert.Equal(Enum.Parse<DeviceFunctionEffect>(expected), Classify(type, name));

    private static DeviceFunctionEffect Classify(string type, string name) =>
        DeviceFunctionEffects.Classify(GetType(type).GetMembers(name).OfType<IMethodSymbol>().First(), s_compilation);

    private static DeviceFunctionEffect ClassifyConstructor(string type) =>
        DeviceFunctionEffects.Classify(
            GetType(type).InstanceConstructors.First(static c => c.Parameters.Length > 0), s_compilation);

    private static DeviceFunctionEffect ClassifyLocalFunction(string name)
    {
        var function = s_compilation.SyntaxTrees
            .SelectMany(static tree => tree.GetRoot().DescendantNodes().OfType<LocalFunctionStatementSyntax>())
            .First(f => f.Identifier.ValueText == name);
        var model = s_compilation.GetSemanticModel(function.SyntaxTree);
        var symbol = model.GetDeclaredSymbol(function) as IMethodSymbol ??
                     throw new InvalidOperationException($"Local function '{name}' not found.");
        return DeviceFunctionEffects.Classify(symbol, s_compilation);
    }

    private static INamedTypeSymbol GetType(string metadataName) =>
        s_compilation.GetTypeByMetadataName(metadataName) ??
        throw new InvalidOperationException($"Type '{metadataName}' not found.");
}
