#nullable enable
namespace Kassyi.GSharp.Tests.Shared.Analysis;

[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class SuspensionAnalysisTests
{
    private static readonly Compilation s_compilation = RoslynTestUtility.CreateCompilation(
        File.ReadAllText(RepositoryPaths.Combine(
            "tests", "Compiler", "Kassyi.GSharp.Tests.Cpu", "Analysis", "Cases", "SuspensionAnalysisCases.cs")));

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("ViaCtor", true)]
    [InlineData("ViaPlainCtor", false)]
    [InlineData("ViaPropertyGetter", true)]
    [InlineData("ViaPlainPropertyGetter", false)]
    [InlineData("ViaPropertySetter", true)]
    [InlineData("ViaGetterOnlyAssignmentOnly", false)]
    [InlineData("ViaCompoundAssignment", true)]
    [InlineData("ViaChainedCtor", true)]
    [InlineData("ViaPlainChainedCtor", false)]
    [InlineData("ViaFieldInitializer", true)]
    [InlineData("ViaPlainFieldInitializer", false)]
    [InlineData("ViaIndexer", true)]
    [InlineData("ViaPlainIndexer", false)]
    [InlineData("ViaImplicitConversion", true)]
    [InlineData("ViaPlainImplicitConversion", false)]
    [InlineData("ViaCollectionBuilder", true)]
    [InlineData("ViaPlainCollectionBuilder", false)]
    [InlineData("ViaImplicitDefaultCtorOnFieldInitStruct", false)]
    [InlineData("ViaParamCtorOnFieldInitStruct", true)]
    [InlineData("ViaImplicitDefaultCtorOnFieldInitRecordStruct", false)]
    [InlineData("ViaPrimaryCtorOnFieldInitRecordStruct", true)]
    [InlineData("ViaNestedTupleDeconstructionGetterOnly", false)]
    [InlineData("ViaNestedTupleDeconstructionSetterOnly", true)]
    [InlineData("ViaGenericConstrainedBarrier", true)]
    [InlineData("ViaGenericConstrainedPlain", false)]
    [InlineData("ViaGenericNewTBarrier", true)]
    [InlineData("ViaGenericNewTPlain", false)]
    [InlineData("ViaGenericUsingTBarrier", true)]
    [InlineData("ViaGenericUsingTPlain", false)]
    [InlineData("ViaNestedOuterInnerBarrier", true)]
    [InlineData("ViaNestedOuterInnerPlain", false)]
    [InlineData("ViaNestedOuterNestedFirstBarrier", true)]
    [InlineData("ViaNestedOuterNestedSecondBarrier", true)]
    [InlineData("ViaNestedOuterNestedPlain", false)]
    [InlineData("ViaNestedMultiLevelBarrier", true)]
    [InlineData("ViaNestedMultiLevelPlain", false)]
    [InlineData("ViaGenericBoxPropBarrier", true)]
    [InlineData("ViaGenericBoxPropPlain", false)]
    [InlineData("ViaExplicitInterfacePropSetterBarrier", true)]
    [InlineData("ViaExplicitInterfacePropSetterPlain", false)]
    [InlineData("ViaConstrainedGenMethodCtorBarrier", true)]
    [InlineData("ViaConstrainedGenMethodCtorPlain", false)]
    public void IsSuspendingFunction_ThroughStructMembersAndImplicitCalls_MatchesExpected(
        string methodName,
        bool expected)
    {
        var method = GetFunction(methodName);

        Assert.Equal(expected, SuspensionAnalysis.IsSuspendingFunction(method, s_compilation));
        Assert.Equal(expected, SuspensionAnalysis.ReachesBlockBarrier(method, s_compilation));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GetSuspendingCallees_BodyCallingStructConstructor_ExcludesStructConstructor()
    {
        var ctorOnly = GetBody("CtorOnlyBody");
        var ctorAndDevice = GetBody("CtorAndDeviceFunctionBody");

        Assert.Empty(SuspensionAnalysis.GetSuspendingCallees(ctorOnly, s_compilation));
        Assert.Equal(
            ["BarrierLeaf"],
            SuspensionAnalysis.GetSuspendingCallees(ctorAndDevice, s_compilation)
                .Select(static callee => callee.Name));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void IsSuspendingFunction_LocalFunctionInsideEmittedStruct_IsFalseWhileConstructorIsTrue()
    {
        var structType = s_compilation.GetTypeByMetadataName("SuspensionAnalysisCases.StructWithLocalFunction")
            ?? throw new InvalidOperationException("Type 'StructWithLocalFunction' was not found.");
        var ctor = structType.InstanceConstructors.Single(static c => c.Parameters.Length == 1);
        var ctorSyntax = (ConstructorDeclarationSyntax)ctor.DeclaringSyntaxReferences[0].GetSyntax();
        var model = s_compilation.GetSemanticModel(ctorSyntax.SyntaxTree);
        var localSyntax = ctorSyntax.DescendantNodes().OfType<LocalFunctionStatementSyntax>().Single();
        var localSymbol = (IMethodSymbol)(model.GetDeclaredSymbol(localSyntax)
            ?? throw new InvalidOperationException("Local function symbol not found."));

        Assert.False(SuspensionAnalysis.IsSuspendingFunction(localSymbol, s_compilation));
        Assert.True(SuspensionAnalysis.IsSuspendingFunction(ctor, s_compilation));
        Assert.True(SuspensionAnalysis.ReachesBlockBarrier(ctor, s_compilation));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void IsSuspendingFunction_ImplicitParameterlessStructCtorWithFieldInitializer_IsFalseWhileExplicitIsTrue()
    {
        var structType = s_compilation.GetTypeByMetadataName("SuspensionAnalysisCases.FieldInitWithParamCtor")
            ?? throw new InvalidOperationException("Type 'FieldInitWithParamCtor' was not found.");
        var implicitCtor = structType.InstanceConstructors.Single(static c => c.Parameters.Length == 0);
        var paramCtor = structType.InstanceConstructors.Single(static c => c.Parameters.Length == 1);

        Assert.True(implicitCtor.IsImplicitlyDeclared);
        Assert.False(SuspensionAnalysis.IsSuspendingFunction(implicitCtor, s_compilation));
        Assert.False(SuspensionAnalysis.ReachesBlockBarrier(implicitCtor, s_compilation));
        Assert.True(SuspensionAnalysis.IsSuspendingFunction(paramCtor, s_compilation));
        Assert.True(SuspensionAnalysis.ReachesBlockBarrier(paramCtor, s_compilation));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void IsSuspendingFunction_GenericDeviceFunctionInstantiationOrder_DoesNotCollideInCache()
    {
        var genericDef = GetFunction("GenericDeviceCall");
        var plainType = s_compilation.GetTypeByMetadataName("SuspensionAnalysisCases.PlainAction")
            ?? throw new InvalidOperationException("Type 'PlainAction' was not found.");
        var barrierType = s_compilation.GetTypeByMetadataName("SuspensionAnalysisCases.BarrierAction")
            ?? throw new InvalidOperationException("Type 'BarrierAction' was not found.");
        var plainCall = genericDef.Construct(plainType);
        var barrierCall = genericDef.Construct(barrierType);

        // Query the non-suspending instantiation first so any definition-level cache collision would poison BarrierAction.
        Assert.False(SuspensionAnalysis.IsSuspendingFunction(plainCall, s_compilation));
        Assert.False(SuspensionAnalysis.ReachesBlockBarrier(plainCall, s_compilation));
        Assert.True(SuspensionAnalysis.IsSuspendingFunction(barrierCall, s_compilation));
        Assert.True(SuspensionAnalysis.ReachesBlockBarrier(barrierCall, s_compilation));
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("ViaGenericNestedOuterInner")]
    [InlineData("ViaGenericNestedBoth")]
    [InlineData("ViaGenericNestedMultiLevel")]
    public void IsSuspendingFunction_NestedGenericThroughTypeArgumentMap_SubstitutesEnclosingTypeArguments(
        string methodName)
    {
        var genericDef = GetFunction(methodName);
        var plainType = s_compilation.GetTypeByMetadataName("SuspensionAnalysisCases.PlainAction")
            ?? throw new InvalidOperationException("Type 'PlainAction' was not found.");
        var barrierType = s_compilation.GetTypeByMetadataName("SuspensionAnalysisCases.BarrierAction")
            ?? throw new InvalidOperationException("Type 'BarrierAction' was not found.");
        var plainCall = genericDef.Construct(plainType);
        var barrierCall = genericDef.Construct(barrierType);

        Assert.False(SuspensionAnalysis.IsSuspendingFunction(plainCall, s_compilation));
        Assert.False(SuspensionAnalysis.ReachesBlockBarrier(plainCall, s_compilation));
        Assert.True(SuspensionAnalysis.IsSuspendingFunction(barrierCall, s_compilation));
        Assert.True(SuspensionAnalysis.ReachesBlockBarrier(barrierCall, s_compilation));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void IsSuspendingFunction_PolymorphicRecursion_TerminatesWithExpectedAnswer()
    {
        var intType = s_compilation.GetSpecialType(SpecialType.System_Int32);
        var deepPlain = GetFunction("DeepPlain").Construct(intType);
        var deepBarrier = GetFunction("DeepBarrier").Construct(intType);

        Assert.False(SuspensionAnalysis.IsSuspendingFunction(deepPlain, s_compilation));
        Assert.False(SuspensionAnalysis.ReachesBlockBarrier(deepPlain, s_compilation));
        Assert.True(SuspensionAnalysis.IsSuspendingFunction(deepBarrier, s_compilation));
        Assert.True(SuspensionAnalysis.ReachesBlockBarrier(deepBarrier, s_compilation));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void IsRecursiveSuspendingFunction_GenericInstantiation_DistinguishesBarrierTypeArgument()
    {
        var recursiveDef = GetFunction("RecursiveGeneric");
        var plainType = s_compilation.GetTypeByMetadataName("SuspensionAnalysisCases.PlainAction")
            ?? throw new InvalidOperationException("Type 'PlainAction' was not found.");
        var barrierType = s_compilation.GetTypeByMetadataName("SuspensionAnalysisCases.BarrierAction")
            ?? throw new InvalidOperationException("Type 'BarrierAction' was not found.");
        var plainCall = recursiveDef.Construct(plainType);
        var barrierCall = recursiveDef.Construct(barrierType);

        Assert.False(SuspensionAnalysis.IsRecursiveSuspendingFunction(plainCall, s_compilation));
        Assert.True(SuspensionAnalysis.IsRecursiveSuspendingFunction(barrierCall, s_compilation));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void IsSuspendingFunction_ConstrainedGenericInterfaceMethod_ResolvesImplementationTypeArguments()
    {
        var implGen = s_compilation.GetTypeByMetadataName("SuspensionAnalysisCases.ImplActionGen")
            ?? throw new InvalidOperationException("Type 'ImplActionGen' was not found.");
        var plainType = s_compilation.GetTypeByMetadataName("SuspensionAnalysisCases.PlainAction")
            ?? throw new InvalidOperationException("Type 'PlainAction' was not found.");
        var barrierType = s_compilation.GetTypeByMetadataName("SuspensionAnalysisCases.BarrierAction")
            ?? throw new InvalidOperationException("Type 'BarrierAction' was not found.");

        var callDef = GetFunction("CallConstrainedGenMethod");
        var plainCall = callDef.Construct(implGen, plainType);
        var barrierCall = callDef.Construct(implGen, barrierType);

        Assert.False(SuspensionAnalysis.IsSuspendingFunction(plainCall, s_compilation));
        Assert.False(SuspensionAnalysis.ReachesBlockBarrier(plainCall, s_compilation));
        Assert.True(SuspensionAnalysis.IsSuspendingFunction(barrierCall, s_compilation));
        Assert.True(SuspensionAnalysis.ReachesBlockBarrier(barrierCall, s_compilation));

        var implOfGenDef = s_compilation.GetTypeByMetadataName("SuspensionAnalysisCases.ImplActionOfGen`1")
            ?? throw new InvalidOperationException("Type 'ImplActionOfGen`1' was not found.");
        var callOfGenDef = GetFunction("CallConstrainedOfGenMethod");
        var plainBoth = callOfGenDef.Construct(implOfGenDef.Construct(plainType), plainType, plainType);
        var barrierOuter = callOfGenDef.Construct(implOfGenDef.Construct(barrierType), barrierType, plainType);
        var barrierInner = callOfGenDef.Construct(implOfGenDef.Construct(plainType), plainType, barrierType);

        Assert.False(SuspensionAnalysis.IsSuspendingFunction(plainBoth, s_compilation));
        Assert.True(SuspensionAnalysis.IsSuspendingFunction(barrierOuter, s_compilation));
        Assert.True(SuspensionAnalysis.IsSuspendingFunction(barrierInner, s_compilation));
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("PartialGeneric")]
    [InlineData("PartialGenericAttrOnImpl")]
    public void IsSuspendingFunction_PartialGenericDeviceFunction_MatchesForDefiningAndImplementingParts(
        string methodName)
    {
        var plainType = s_compilation.GetTypeByMetadataName("SuspensionAnalysisCases.PlainAction")
            ?? throw new InvalidOperationException("Type 'PlainAction' was not found.");
        var barrierType = s_compilation.GetTypeByMetadataName("SuspensionAnalysisCases.BarrierAction")
            ?? throw new InvalidOperationException("Type 'BarrierAction' was not found.");

        var def = GetFunction(methodName);
        var impl = def.PartialImplementationPart
            ?? throw new InvalidOperationException($"Expected PartialImplementationPart for '{methodName}'.");

        Assert.False(SuspensionAnalysis.IsSuspendingFunction(def.Construct(plainType), s_compilation));
        Assert.False(SuspensionAnalysis.ReachesBlockBarrier(def.Construct(plainType), s_compilation));
        Assert.True(SuspensionAnalysis.IsSuspendingFunction(def.Construct(barrierType), s_compilation));
        Assert.True(SuspensionAnalysis.ReachesBlockBarrier(def.Construct(barrierType), s_compilation));

        Assert.False(SuspensionAnalysis.IsSuspendingFunction(impl.Construct(plainType), s_compilation));
        Assert.False(SuspensionAnalysis.ReachesBlockBarrier(impl.Construct(plainType), s_compilation));
        Assert.True(SuspensionAnalysis.IsSuspendingFunction(impl.Construct(barrierType), s_compilation));
        Assert.True(SuspensionAnalysis.ReachesBlockBarrier(impl.Construct(barrierType), s_compilation));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void IsSuspendingFunction_PartialGenericMethodInGenericType_PreservesTypeAndMethodArguments()
    {
        var containerDef = s_compilation.GetTypeByMetadataName("SuspensionAnalysisCases.PartialGenericContainer`1")
            ?? throw new InvalidOperationException("Type 'PartialGenericContainer`1' was not found.");
        var plainType = s_compilation.GetTypeByMetadataName("SuspensionAnalysisCases.PlainAction")
            ?? throw new InvalidOperationException("Type 'PlainAction' was not found.");
        var barrierType = s_compilation.GetTypeByMetadataName("SuspensionAnalysisCases.BarrierAction")
            ?? throw new InvalidOperationException("Type 'BarrierAction' was not found.");

        var plainOuter = containerDef.Construct(plainType)
            .GetMembers("PartialGeneric").OfType<IMethodSymbol>().Single();
        var barrierOuter = containerDef.Construct(barrierType)
            .GetMembers("PartialGeneric").OfType<IMethodSymbol>().Single();

        // Roslyn sets PartialImplementationPart to null on constructed generic method symbols; TypeArgumentMap.Of
        // resolves OriginalDefinition.PartialImplementationPart so both type-level and method-level type arguments bind.
        var ppDef = plainOuter.Construct(plainType);
        var bpDef = barrierOuter.Construct(plainType);
        var pbDef = plainOuter.Construct(barrierType);
        var bbDef = barrierOuter.Construct(barrierType);

        Assert.False(SuspensionAnalysis.IsSuspendingFunction(ppDef, s_compilation));
        Assert.False(SuspensionAnalysis.ReachesBlockBarrier(ppDef, s_compilation));
        Assert.True(SuspensionAnalysis.IsSuspendingFunction(bpDef, s_compilation));
        Assert.True(SuspensionAnalysis.ReachesBlockBarrier(bpDef, s_compilation));
        Assert.True(SuspensionAnalysis.IsSuspendingFunction(pbDef, s_compilation));
        Assert.True(SuspensionAnalysis.ReachesBlockBarrier(pbDef, s_compilation));
        Assert.True(SuspensionAnalysis.IsSuspendingFunction(bbDef, s_compilation));
        Assert.True(SuspensionAnalysis.ReachesBlockBarrier(bbDef, s_compilation));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void IsRecursiveSuspendingFunction_PartialGeneric_MatchesForDefiningAndImplementingParts()
    {
        var plainType = s_compilation.GetTypeByMetadataName("SuspensionAnalysisCases.PlainAction")
            ?? throw new InvalidOperationException("Type 'PlainAction' was not found.");
        var barrierType = s_compilation.GetTypeByMetadataName("SuspensionAnalysisCases.BarrierAction")
            ?? throw new InvalidOperationException("Type 'BarrierAction' was not found.");

        var def = GetFunction("RecPartialGeneric");
        var impl = def.PartialImplementationPart
            ?? throw new InvalidOperationException("Expected PartialImplementationPart for 'RecPartialGeneric'.");

        Assert.False(SuspensionAnalysis.IsRecursiveSuspendingFunction(def.Construct(plainType), s_compilation));
        Assert.False(SuspensionAnalysis.IsRecursiveSuspendingFunction(impl.Construct(plainType), s_compilation));
        Assert.True(SuspensionAnalysis.IsRecursiveSuspendingFunction(def.Construct(barrierType), s_compilation));
        Assert.True(SuspensionAnalysis.IsRecursiveSuspendingFunction(impl.Construct(barrierType), s_compilation));
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("PartialDeepPlainAttrDef", false)]
    [InlineData("PartialDeepBarrierAttrDef", true)]
    [InlineData("PartialDeepPlainAttrImpl", false)]
    [InlineData("PartialDeepBarrierAttrImpl", true)]
    [InlineData("PartialDeepExtPlainAttrDef", false)]
    [InlineData("PartialDeepExtBarrierAttrDef", true)]
    [InlineData("PartialDeepExtPlainAttrImpl", false)]
    [InlineData("PartialDeepExtBarrierAttrImpl", true)]
    [InlineData("PartialIndirectPlainAttrDef", false)]
    [InlineData("PartialIndirectBarrierAttrDef", true)]
    [InlineData("PartialIndirectPlainAttrImpl", false)]
    [InlineData("PartialIndirectBarrierAttrImpl", true)]
    public void IsSuspendingFunction_PartialPolymorphicRecursion_TerminatesForDefiningAndImplementingParts(
        string methodName,
        bool expected)
    {
        var intType = s_compilation.GetSpecialType(SpecialType.System_Int32);
        var def = GetFunction(methodName);
        var impl = def.PartialImplementationPart
            ?? throw new InvalidOperationException($"Expected PartialImplementationPart for '{methodName}'.");

        if (!methodName.Contains("Indirect", StringComparison.Ordinal))
        {
            Assert.NotEmpty(GenericExpansion.GetExpandingCallees(impl, s_compilation));
        }

        Assert.Equal(
            GenericExpansion.GetExpandingCallees(def, s_compilation),
            GenericExpansion.GetExpandingCallees(impl, s_compilation),
            SymbolEqualityComparer.Default);

        Assert.Equal(expected, SuspensionAnalysis.IsSuspendingFunction(def.Construct(intType), s_compilation));
        Assert.Equal(expected, SuspensionAnalysis.ReachesBlockBarrier(def.Construct(intType), s_compilation));
        Assert.Equal(expected, SuspensionAnalysis.IsSuspendingFunction(impl.Construct(intType), s_compilation));
        Assert.Equal(expected, SuspensionAnalysis.ReachesBlockBarrier(impl.Construct(intType), s_compilation));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void IsSuspendingFunction_IndirectConstrainedPartialPolymorphicRecursion_TerminatesWithExpectedAnswer()
    {
        var intType = s_compilation.GetSpecialType(SpecialType.System_Int32);
        var plainWrap = (s_compilation.GetTypeByMetadataName("SuspensionAnalysisCases.ConstrainedPlainWrap`1")
            ?? throw new InvalidOperationException("Type 'ConstrainedPlainWrap`1' was not found.")).Construct(intType);
        var barrierWrap = (s_compilation.GetTypeByMetadataName("SuspensionAnalysisCases.ConstrainedBarrierWrap`1")
            ?? throw new InvalidOperationException("Type 'ConstrainedBarrierWrap`1' was not found.")).Construct(intType);
        var def = GetFunction("PartialConstrainedAttrImpl");
        var impl = def.PartialImplementationPart
            ?? throw new InvalidOperationException("Expected PartialImplementationPart for 'PartialConstrainedAttrImpl'.");

        Assert.False(SuspensionAnalysis.IsSuspendingFunction(def.Construct(plainWrap), s_compilation));
        Assert.False(SuspensionAnalysis.IsSuspendingFunction(impl.Construct(plainWrap), s_compilation));
        Assert.True(SuspensionAnalysis.IsSuspendingFunction(def.Construct(barrierWrap), s_compilation));
        Assert.True(SuspensionAnalysis.IsSuspendingFunction(impl.Construct(barrierWrap), s_compilation));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void IsSuspendingFunction_NonExpansivePartialSwap_DoesNotMisclassifyAsExpansive()
    {
        var plainType = s_compilation.GetTypeByMetadataName("SuspensionAnalysisCases.PlainAction")
            ?? throw new InvalidOperationException("Type 'PlainAction' was not found.");
        var barrierType = s_compilation.GetTypeByMetadataName("SuspensionAnalysisCases.BarrierAction")
            ?? throw new InvalidOperationException("Type 'BarrierAction' was not found.");
        var def = GetFunction("PartialSwap");
        var impl = def.PartialImplementationPart
            ?? throw new InvalidOperationException("Expected PartialImplementationPart for 'PartialSwap'.");

        Assert.False(SuspensionAnalysis.IsSuspendingFunction(def.Construct(plainType, plainType), s_compilation));
        Assert.True(SuspensionAnalysis.IsSuspendingFunction(def.Construct(plainType, barrierType), s_compilation));
        Assert.True(SuspensionAnalysis.IsSuspendingFunction(impl.Construct(plainType, barrierType), s_compilation));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void IsSuspendingFunction_PartialGenericStructPolymorphicRecursionAndSelfCall_ResolvesDefiningPart()
    {
        var intType = s_compilation.GetSpecialType(SpecialType.System_Int32);
        var polyStructDef = s_compilation.GetTypeByMetadataName("SuspensionAnalysisCases.PartialGenStructPoly`1")
            ?? throw new InvalidOperationException("Type 'PartialGenStructPoly`1' was not found.");
        var polyStructInt = polyStructDef.Construct(intType);
        var ctorInt = polyStructInt.InstanceConstructors.Single(static c => c.Parameters.Length == 1);
        var stepPlain = polyStructInt.GetMembers("StepPlain").OfType<IMethodSymbol>().Single();
        var stepBarrier = polyStructInt.GetMembers("StepBarrier").OfType<IMethodSymbol>().Single();

        Assert.False(SuspensionAnalysis.IsSuspendingFunction(ctorInt, s_compilation));
        Assert.False(SuspensionAnalysis.IsSuspendingFunction(stepPlain, s_compilation));
        Assert.True(SuspensionAnalysis.IsSuspendingFunction(stepBarrier, s_compilation));

        var genBoxDef = s_compilation.GetTypeByMetadataName("SuspensionAnalysisCases.GenBox`1")
            ?? throw new InvalidOperationException("Type 'GenBox`1' was not found.");
        var plainType = s_compilation.GetTypeByMetadataName("SuspensionAnalysisCases.PlainAction")
            ?? throw new InvalidOperationException("Type 'PlainAction' was not found.");
        var barrierType = s_compilation.GetTypeByMetadataName("SuspensionAnalysisCases.BarrierAction")
            ?? throw new InvalidOperationException("Type 'BarrierAction' was not found.");

        var openDefStep = genBoxDef.GetMembers("Step").OfType<IMethodSymbol>().Single();
        var openImplStep = openDefStep.PartialImplementationPart
            ?? throw new InvalidOperationException("Expected PartialImplementationPart for 'GenBox<T>.Step'.");
        var closedPlainStep = genBoxDef.Construct(plainType).GetMembers("Step").OfType<IMethodSymbol>().Single();
        var closedBarrierStep = genBoxDef.Construct(barrierType).GetMembers("Step").OfType<IMethodSymbol>().Single();

        Assert.True(SymbolEqualityComparer.Default.Equals(
            closedBarrierStep,
            TypeArgumentMap.Of(closedBarrierStep).Substitute(openImplStep)));
        Assert.False(SuspensionAnalysis.IsSuspendingFunction(closedPlainStep, s_compilation));
        Assert.True(SuspensionAnalysis.IsSuspendingFunction(closedBarrierStep, s_compilation));
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("RecPartialExt")]
    [InlineData("RecNonPartialExt")]
    public void IsRecursiveSuspendingFunction_RecursiveExtensionDeviceFunction_ReturnsTrue(string methodName)
    {
        var def = GetFunction(methodName);

        Assert.True(SuspensionAnalysis.IsRecursiveSuspendingFunction(def, s_compilation));
        if (def.PartialImplementationPart is { } impl)
        {
            Assert.True(SuspensionAnalysis.IsRecursiveSuspendingFunction(impl, s_compilation));
        }
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("PartialStructGenericDeviceFunctionBody", false)]
    [InlineData("PartialGenericStructDeviceFunctionBody", false)]
    [InlineData("CtorOnlyBody", true)]
    public void HasStructMemberSplit_PartialDeviceFunctionInStructVsCtor_MatchesExpected(
        string methodName,
        bool expected)
    {
        var body = GetBody(methodName);

        Assert.Equal(expected, SuspensionAnalysis.HasStructMemberSplit(body, TypeArgumentMap.None, s_compilation));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void IsSuspendingFunction_CrossAssemblyImportedStructsAndDeviceFunctions_MatchesExportedTraits()
    {
        const string LibrarySource = """
            using System;
            using Kassyi.GSharp.Core;

            namespace ImportedStructLib;

            public interface IImportedAction
            {
                void Run();
            }

            public interface IImportedActionGen
            {
                void Run<U>() where U : struct, IImportedAction;
            }

            public struct ImportedGenAction : IImportedActionGen
            {
                [GpuDeviceFunction]
                public void Run<U>() where U : struct, IImportedAction => Gpu.SyncThreads();
            }

            public struct ImportedBarrierAction : IImportedAction
            {
                [GpuDeviceFunction]
                public void Run() => Gpu.SyncThreads();
            }

            public struct ImportedPlainAction : IImportedAction
            {
                [GpuDeviceFunction]
                public void Run()
                {
                }
            }

            public struct Pair : IDisposable
            {
                public int A;

                public Pair(int a)
                {
                    A = a;
                }

                public int Doubled => A * 2;

                public static implicit operator int(Pair p) => p.A;

                public void Dispose()
                {
                }
            }

            public struct Box<T> where T : unmanaged
            {
                public T Value;

                public Box(T value)
                {
                    Value = value;
                }
            }

            public static partial class LibFunctions
            {
                [GpuDeviceFunction]
                public static int ImportedSuspending(int x)
                {
                    Gpu.SyncThreads();
                    return x + 1;
                }

                [GpuDeviceFunction]
                public static int ImportedPlain(int x) => x + 1;

                [GpuKernel]
                public static void ExportClosedBoxKernel(int[] data)
                {
                    var b = new Box<int>(data[ThreadIndex.X]);
                    data[ThreadIndex.X] = b.Value;
                }
            }
            """;

        const string ConsumerSource = """
            using ImportedStructLib;
            using Kassyi.GSharp.Core;

            namespace ImportedStructConsumer;

            public static partial class ConsumerFunctions
            {
                [GpuDeviceFunction]
                public static void CallConstrained<T>() where T : struct, IImportedAction => default(T).Run();

                [GpuDeviceFunction]
                public static void CallConstrainedGen<T, U>() where T : struct, IImportedActionGen where U : struct, IImportedAction =>
                    default(T).Run<U>();
            }
            """;

        var library = DeviceLibraryBuilder.Build("ImportedStructLib", null, LibrarySource);
        var consumer = DeviceLibraryBuilder.CreateConsumer(ConsumerSource, library);

        var pairType = consumer.GetTypeByMetadataName("ImportedStructLib.Pair")
            ?? throw new InvalidOperationException("Imported Pair type not found.");
        var pairCtor = pairType.InstanceConstructors.Single(static c => c.Parameters.Length == 1);
        var pairProp = pairType.GetMembers("Doubled").OfType<IPropertySymbol>().Single().GetMethod
            ?? throw new InvalidOperationException("Doubled getter not found.");
        var pairDispose = pairType.GetMembers("Dispose").OfType<IMethodSymbol>().Single();
        var pairConv = pairType.GetMembers(WellKnownMemberNames.ImplicitConversionName).OfType<IMethodSymbol>().Single();

        var intType = consumer.GetSpecialType(SpecialType.System_Int32);
        var boxType = (consumer.GetTypeByMetadataName("ImportedStructLib.Box`1")
            ?? throw new InvalidOperationException("Imported Box<T> type not found.")).Construct(intType);
        var boxCtor = boxType.InstanceConstructors.Single(static c => c.Parameters.Length == 1);

        var libFunctions = consumer.GetTypeByMetadataName("ImportedStructLib.LibFunctions")
            ?? throw new InvalidOperationException("Imported LibFunctions type not found.");
        var importedSuspending = libFunctions.GetMembers("ImportedSuspending").OfType<IMethodSymbol>().Single();
        var importedPlain = libFunctions.GetMembers("ImportedPlain").OfType<IMethodSymbol>().Single();

        var consumerFunctions = consumer.GetTypeByMetadataName("ImportedStructConsumer.ConsumerFunctions")
            ?? throw new InvalidOperationException("ConsumerFunctions type not found.");
        var callConstrained = consumerFunctions.GetMembers("CallConstrained").OfType<IMethodSymbol>().Single();
        var callConstrainedGen = consumerFunctions.GetMembers("CallConstrainedGen").OfType<IMethodSymbol>().Single();
        var importedBarrierAct = consumer.GetTypeByMetadataName("ImportedStructLib.ImportedBarrierAction")
            ?? throw new InvalidOperationException("ImportedBarrierAction not found.");
        var importedPlainAct = consumer.GetTypeByMetadataName("ImportedStructLib.ImportedPlainAction")
            ?? throw new InvalidOperationException("ImportedPlainAction not found.");
        var importedGenAct = consumer.GetTypeByMetadataName("ImportedStructLib.ImportedGenAction")
            ?? throw new InvalidOperationException("ImportedGenAction not found.");

        Assert.False(SuspensionAnalysis.IsSuspendingFunction(pairCtor, consumer));
        Assert.False(SuspensionAnalysis.IsSuspendingFunction(pairProp, consumer));
        Assert.False(SuspensionAnalysis.IsSuspendingFunction(pairDispose, consumer));
        Assert.False(SuspensionAnalysis.IsSuspendingFunction(pairConv, consumer));
        Assert.False(SuspensionAnalysis.IsSuspendingFunction(boxCtor, consumer));
        Assert.True(SuspensionAnalysis.IsSuspendingFunction(importedSuspending, consumer));
        Assert.False(SuspensionAnalysis.IsSuspendingFunction(importedPlain, consumer));
        Assert.False(SuspensionAnalysis.IsSuspendingFunction(callConstrained.Construct(importedPlainAct), consumer));
        Assert.True(SuspensionAnalysis.IsSuspendingFunction(callConstrained.Construct(importedBarrierAct), consumer));
        Assert.True(SuspensionAnalysis.IsSuspendingFunction(
            callConstrainedGen.Construct(importedGenAct, importedBarrierAct), consumer));
    }

    private static readonly Compilation s_recursionCompilation = RoslynTestUtility.CreateCompilation("""
        using System;
        using Kassyi.GSharp.Core;
        using Kassyi.ZeroAlloc.Linq;

        namespace RecursionReachCases;

        public struct RecMacroValue
        {
            public int Value;

            [GpuOperatorMacro(typeof(RecMacroOps), nameof(RecMacroOps.Add))]
            public static RecMacroValue operator +(RecMacroValue left, RecMacroValue right) =>
                throw new GpuOnlyException();
        }

        public static partial class RecMacroOps
        {
            [GpuDeviceFunction]
            public static void Add(ref RecMacroValue result, RecMacroValue left, RecMacroValue right)
            {
                result.Value = Functions.DirectRec(left.Value + right.Value);
            }
        }

        public struct RecCtorStruct
        {
            public int Value;

            public RecCtorStruct(int n)
            {
                Value = Functions.DirectRec(n);
            }
        }

        public struct PlainCtorStruct
        {
            public int Value;

            public PlainCtorStruct(int n)
            {
                Value = Functions.ChainC(n);
            }
        }

        public struct Wrap<T>
        {
            public T Value;
        }

        public static partial class Functions
        {
            [GpuDeviceFunction]
            public static int DirectRec(int n) => n <= 0 ? 0 : DirectRec(n - 1);

            [GpuDeviceFunction]
            public static int Ping(int n) => n <= 0 ? 0 : Pong(n - 1);

            [GpuDeviceFunction]
            public static int Pong(int n) => n <= 0 ? 1 : Ping(n - 1);

            [GpuDeviceFunction]
            public static int ChainC(int x) => x + 1;

            [GpuDeviceFunction]
            public static int ChainB(int x) => ChainC(x) * 2;

            [GpuDeviceFunction]
            public static int ChainA(int x) => ChainB(x) + ChainC(x);

            [GpuDeviceFunction]
            public static int UnreachableRec(int n) => n <= 0 ? 0 : UnreachableRec(n - 1);

            [GpuDeviceFunction]
            public static int WithRecursiveLocal(int x)
            {
                int localSum(int n) => n <= 0 ? 0 : n + localSum(n - 1);
                return localSum(x);
            }

            [GpuDeviceFunction]
            public static int PolyPlain<T>(int depth)
            {
                if (depth <= 0)
                {
                    return 0;
                }

                return PolyPlain<Wrap<T>>(depth - 1);
            }
        }

        public static partial class Kernels
        {
            [GpuKernel]
            public static void DirectRecursionKernel(int[] data)
            {
                data[0] = Functions.DirectRec(data[0]);
            }

            [GpuKernel]
            public static void MutualRecursionKernel(int[] data)
            {
                data[0] = Functions.Ping(data[0]);
            }

            [GpuKernel]
            public static void MethodGroupRecursionKernel(int[] data)
            {
                Func<int, int> step = Functions.DirectRec;
                data[0] = step(data[0]);
            }

            [GpuKernel]
            public static void RecursiveLocalFunctionKernel(int[] data)
            {
                int sum(int n) => n <= 0 ? 0 : n + sum(n - 1);
                data[0] = sum(data[0]);
            }

            [GpuKernel]
            public static void RecursiveBlockLocalFunctionKernel(int[] data)
            {
                int blockSum(int n)
                {
                    if (n <= 0)
                    {
                        return 0;
                    }

                    return n + blockSum(n - 1);
                }

                data[0] = blockSum(data[0]);
            }

            [GpuKernel]
            public static void MutualLocalFunctionRecursionKernel(int[] data)
            {
                int localA(int n) => n <= 0 ? 0 : localB(n - 1);
                int localB(int n) => n <= 0 ? 1 : localA(n - 1);
                data[0] = localA(data[0]);
            }

            [GpuKernel]
            public static void MethodGroupLocalFunctionRecursionKernel(int[] data)
            {
                int localA(int n)
                {
                    if (n <= 0)
                    {
                        return 0;
                    }

                    Func<int, int> next = localB;
                    return next(n - 1);
                }

                int localB(int n) => n <= 0 ? 1 : localA(n - 1);
                data[0] = localA(data[0]);
            }

            [GpuKernel]
            public static void DeviceFunctionWithRecursiveLocalKernel(int[] data)
            {
                data[0] = Functions.WithRecursiveLocal(data[0]);
            }

            [GpuKernel]
            public static void OperatorMacroRecursionKernel(int[] data)
            {
                var a = new RecMacroValue { Value = data[0] };
                var b = new RecMacroValue { Value = 1 };
                var c = a + b;
                data[0] = c.Value;
            }

            [GpuKernel]
            public static void StructCtorRecursionKernel(int[] data)
            {
                var s = new RecCtorStruct(data[0]);
                data[0] = s.Value;
            }

            [GpuKernel]
            public static void StructPlainCtorKernel(int[] data)
            {
                var s = new PlainCtorStruct(data[0]);
                data[0] = s.Value;
            }

            [GpuKernel]
            public static void PolymorphicRecursionKernel(int[] data)
            {
                data[0] = Functions.PolyPlain<int>(data[0]);
            }

            [GpuKernel]
            public static void NonRecursiveChainKernel(int[] data)
            {
                data[0] = Functions.ChainA(data[0]);
            }

            [GpuKernel]
            public static void NonRecursiveLocalFunctionKernel(int[] data)
            {
                int step(int n) => Functions.ChainB(n);
                data[0] = step(data[0]);
            }

            [GpuKernel]
            public static void UnreachableRecursionKernel(int[] data)
            {
                _ = nameof(Functions.UnreachableRec);
                data[0] = Functions.ChainC(data[0]);
            }

            [GpuKernel]
            public static void CoreAndMathIntrinsicsKernel(int[] data)
            {
                var d = new Dim3();
                var v = new Float2(1f, 2f);
                Gpu.SyncThreads();
                data[0] = ThreadIndex.X + (int)d.X + (int)v.X + Math.Abs(data[0]) + (int)FastMath.Sqrt(data[0]);
            }

            [GpuKernel]
            public static void StackLinqQueryKernel(int[] data)
            {
                data[0] = data.AsStackEnumerable().Where(static x => x > 0).Count();
            }
        }
        """);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("DirectRecursionKernel", true)]
    [InlineData("MutualRecursionKernel", true)]
    [InlineData("MethodGroupRecursionKernel", true)]
    [InlineData("RecursiveLocalFunctionKernel", true)]
    [InlineData("RecursiveBlockLocalFunctionKernel", true)]
    [InlineData("MutualLocalFunctionRecursionKernel", true)]
    [InlineData("MethodGroupLocalFunctionRecursionKernel", true)]
    [InlineData("DeviceFunctionWithRecursiveLocalKernel", true)]
    [InlineData("OperatorMacroRecursionKernel", true)]
    [InlineData("StructCtorRecursionKernel", true)]
    [InlineData("StructPlainCtorKernel", false)]
    [InlineData("PolymorphicRecursionKernel", true)]
    [InlineData("NonRecursiveChainKernel", false)]
    [InlineData("NonRecursiveLocalFunctionKernel", false)]
    [InlineData("UnreachableRecursionKernel", false)]
    [InlineData("CoreAndMathIntrinsicsKernel", false)]
    [InlineData("StackLinqQueryKernel", false)]
    public void ReachesRecursion_KernelCallGraph_MatchesExpected(string kernelName, bool expected)
    {
        var kernelType = s_recursionCompilation.GetTypeByMetadataName("RecursionReachCases.Kernels")
            ?? throw new InvalidOperationException("Type 'RecursionReachCases.Kernels' was not found.");
        var kernel = kernelType.GetMembers(kernelName).OfType<IMethodSymbol>().Single();

        Assert.Equal(expected, SuspensionAnalysis.ReachesRecursion(kernel, s_recursionCompilation));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ReachesRecursion_ImportedFunctionWithoutRecord_ReturnsTrue()
    {
        const string LibrarySource = """
            using Kassyi.GSharp.Core;

            namespace NoExportRecursionLib;

            public static partial class LibFunctions
            {
                [GpuDeviceFunction]
                public static int ExternalStep(int x) => x + 1;
            }
            """;

        const string ConsumerSource = """
            using Kassyi.GSharp.Core;
            using NoExportRecursionLib;

            namespace ImportedRecursionConsumer;

            public static partial class ConsumerKernels
            {
                [GpuKernel]
                public static void CallImportedKernel(int[] data)
                {
                    data[0] = LibFunctions.ExternalStep(data[0]);
                }
            }
            """;

        var library = DeviceLibraryBuilder.Build(
            "NoExportRecursionLib",
            new Dictionary<string, string> { ["build_property.GSharpExportDeviceCode"] = "false" },
            LibrarySource);
        var consumer = DeviceLibraryBuilder.CreateConsumer(ConsumerSource, library);

        var kernelType = consumer.GetTypeByMetadataName("ImportedRecursionConsumer.ConsumerKernels")
            ?? throw new InvalidOperationException("Type 'ConsumerKernels' was not found.");
        var kernel = kernelType.GetMembers("CallImportedKernel").OfType<IMethodSymbol>().Single();

        Assert.True(SuspensionAnalysis.ReachesRecursion(kernel, consumer));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ReachesRecursion_ImportedFunctionWithExportRecordLackingRecursionTrait_ReturnsTrue()
    {
        const string LibrarySource = """
            using Kassyi.GSharp.Core;

            namespace ExportedRecursionLib;

            public static partial class LibFunctions
            {
                [GpuDeviceFunction]
                public static int ExternalStep(int x) => x + 1;
            }
            """;

        const string ConsumerSource = """
            using Kassyi.GSharp.Core;
            using ExportedRecursionLib;

            namespace ExportedRecursionConsumer;

            public static partial class ConsumerKernels
            {
                [GpuKernel]
                public static void CallExportedKernel(int[] data)
                {
                    data[0] = LibFunctions.ExternalStep(data[0]);
                }
            }
            """;

        var library = DeviceLibraryBuilder.Build("ExportedRecursionLib", null, LibrarySource);
        var consumer = DeviceLibraryBuilder.CreateConsumer(ConsumerSource, library);

        var kernelType = consumer.GetTypeByMetadataName("ExportedRecursionConsumer.ConsumerKernels")
            ?? throw new InvalidOperationException("Type 'ConsumerKernels' was not found.");
        var kernel = kernelType.GetMembers("CallExportedKernel").OfType<IMethodSymbol>().Single();

        Assert.True(SuspensionAnalysis.ReachesRecursion(kernel, consumer));
    }

    private static readonly Lazy<CSharpCompilation> s_unexportedStructConsumer = new(BuildUnexportedStructConsumer);
    private static readonly Lazy<CSharpCompilation> s_exportedStructConsumer = new(BuildExportedStructConsumer);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("CallRecStructCtorKernel", true)]
    [InlineData("CallCtorCallingDeviceAttrKernel", true)]
    [InlineData("CallRecStructMethodKernel", true)]
    [InlineData("CallRecStructComputedPropKernel", true)]
    [InlineData("CallUserOperatorKernel", true)]
    [InlineData("CallConversionKernel", true)]
    [InlineData("CallDisposeKernel", true)]
    [InlineData("CallFixedPinnableKernel", true)]
    [InlineData("CallOperatorMacroKernel", true)]
    [InlineData("CallTruthOperatorKernel", true)]
    [InlineData("CallCrossAssemblyOperatorMacroKernel", true)]
    [InlineData("CallCrossAssemblyTruthOperatorKernel", true)]
    [InlineData("CallExplicitDefaultCtorKernel", true)]
    [InlineData("CallExplicitParamCtorKernel", true)]
    [InlineData("CallImplicitDefaultCtorKernel", false)]
    [InlineData("CallAutoPropertyKernel", false)]
    public void ReachesRecursion_ImportedStructWithoutRecord_MatchesExpected(string kernelName, bool expected)
    {
        var consumer = s_unexportedStructConsumer.Value;
        var kernelType = consumer.GetTypeByMetadataName("NoExportStructConsumer.ConsumerKernels")
            ?? throw new InvalidOperationException("Type 'NoExportStructConsumer.ConsumerKernels' was not found.");
        var kernel = kernelType.GetMembers(kernelName).OfType<IMethodSymbol>().Single();

        Assert.Equal(expected, SuspensionAnalysis.ReachesRecursion(kernel, consumer));
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("CallImplicitDefaultCtorKernel", false)]
    [InlineData("CallAutoPropertyKernel", false)]
    [InlineData("CallFieldInitImplicitDefaultCtorKernel", false)]
    [InlineData("CallRecordImplicitDefaultCtorKernel", false)]
    [InlineData("CallRecordAutoPropertyKernel", false)]
    [InlineData("CallExplicitDefaultCtorKernel", true)]
    [InlineData("CallExplicitParamCtorKernel", true)]
    [InlineData("CallRecordPrimaryCtorKernel", true)]
    [InlineData("CallRecStructCtorKernel", true)]
    public void ReachesRecursion_ExportedImportedStructConstructorsAndAutoProperties_MatchesExpected(
        string kernelName,
        bool expected)
    {
        var consumer = s_exportedStructConsumer.Value;
        var kernelType = consumer.GetTypeByMetadataName("ExportedStructConsumer.ConsumerKernels")
            ?? throw new InvalidOperationException("Type 'ExportedStructConsumer.ConsumerKernels' was not found.");
        var kernel = kernelType.GetMembers(kernelName).OfType<IMethodSymbol>().Single();

        Assert.Equal(expected, SuspensionAnalysis.ReachesRecursion(kernel, consumer));
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false, true, "CallLibBFunctionKernel")]
    [InlineData(false, true, "CallLibBStructCtorKernel")]
    [InlineData(true, false, "CallLibBFunctionKernel")]
    [InlineData(true, false, "CallLibBStructCtorKernel")]
    public void ReachesRecursion_TransitiveImportedAssemblies_ReturnsTrue(
        bool exportLibA,
        bool exportLibB,
        string kernelName)
    {
        const string LibASource = """
            using Kassyi.GSharp.Core;

            namespace TransitiveLibA;

            public struct LibAStruct
            {
                public int Value;

                public LibAStruct(int v)
                {
                    Value = Step(v);
                }

                public int Step(int n) => n <= 0 ? 0 : Step(n - 1);
            }

            public static partial class LibAFns
            {
                [GpuDeviceFunction]
                public static int StepA(int n) => n <= 0 ? 0 : StepA(n - 1);
            }
            """;

        const string LibBSource = """
            using Kassyi.GSharp.Core;
            using TransitiveLibA;

            namespace TransitiveLibB;

            public struct LibBStruct
            {
                public int Value;

                public LibBStruct(int v)
                {
                    var a = new LibAStruct(v);
                    Value = a.Value;
                }
            }

            public static partial class LibBFns
            {
                [GpuDeviceFunction]
                public static int CallA(int n) => LibAFns.StepA(n);
            }
            """;

        const string ConsumerSource = """
            using Kassyi.GSharp.Core;
            using TransitiveLibB;

            namespace TransitiveConsumer;

            public static partial class ConsumerKernels
            {
                [GpuKernel]
                public static void CallLibBFunctionKernel(int[] data)
                {
                    data[0] = LibBFns.CallA(data[0]);
                }

                [GpuKernel]
                public static void CallLibBStructCtorKernel(int[] data)
                {
                    var b = new LibBStruct(data[0]);
                    data[0] = b.Value;
                }
            }
            """;

        var libAOptions = new Dictionary<string, string>
        {
            ["build_property.GSharpExportDeviceCode"] = exportLibA ? "true" : "false"
        };
        var libBOptions = new Dictionary<string, string>
        {
            ["build_property.GSharpExportDeviceCode"] = exportLibB ? "true" : "false"
        };

        var libA = DeviceLibraryBuilder.Build("TransitiveLibA", libAOptions, LibASource);
        var libB = DeviceLibraryBuilder.BuildWithReferences("TransitiveLibB", libBOptions, [libA], LibBSource);
        var consumer = DeviceLibraryBuilder.CreateConsumer(ConsumerSource, libA, libB);

        Assert.Empty(consumer.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));

        var kernelType = consumer.GetTypeByMetadataName("TransitiveConsumer.ConsumerKernels")
            ?? throw new InvalidOperationException("Type 'TransitiveConsumer.ConsumerKernels' was not found.");
        var kernel = kernelType.GetMembers(kernelName).OfType<IMethodSymbol>().Single();

        Assert.True(SuspensionAnalysis.ReachesRecursion(kernel, consumer));
    }

    private static CSharpCompilation BuildUnexportedStructConsumer()
    {
        const string MacroTargetLibSource = """
            using Kassyi.GSharp.Core;

            namespace ExportedMacroTargetLib;

            public static partial class ExportedMacroOps
            {
                [GpuDeviceFunction]
                public static void CrossAdd(ref int result, int left, int right)
                {
                    result = left + right;
                }

                [GpuDeviceFunction]
                public static void CrossAnd(ref int result, int left, int right)
                {
                    result = left != 0 && right != 0 ? 1 : 0;
                }
            }
            """;

        const string LibrarySource = """
            using System;
            using ExportedMacroTargetLib;
            using Kassyi.GSharp.Core;

            namespace NoExportStructLib;

            public static partial class LibFns
            {
                [GpuDeviceFunction]
                public static int Rec(int n) => n <= 0 ? 0 : Rec(n - 1);
            }

            public struct ImportedPlainStruct
            {
                public int Value;
                public int AutoProp { get; set; }

                public ImportedPlainStruct(int value)
                {
                    Value = value;
                    AutoProp = value;
                }
            }

            public struct ImportedExplicitDefaultStruct
            {
                public int Value;

                public ImportedExplicitDefaultStruct()
                {
                    Value = 1;
                }
            }

            public struct ImportedRecStruct
            {
                public int Value;

                public ImportedRecStruct(int n)
                {
                    Value = Compute(n);
                }

                public int Compute(int n) => n <= 0 ? 0 : Compute(n - 1);

                public int RecProp => Compute(Value);

                public static ImportedRecStruct operator *(ImportedRecStruct left, ImportedRecStruct right) =>
                    new ImportedRecStruct(left.Value * right.Value);

                public static implicit operator int(ImportedRecStruct value) => value.Value;
            }

            public struct ImportedCtorCallingDeviceAttrStruct
            {
                public int Value;

                public ImportedCtorCallingDeviceAttrStruct(int n)
                {
                    Value = LibFns.Rec(n);
                }
            }

            public struct ImportedDisposableStruct : IDisposable
            {
                public int Value;

                public void Dispose()
                {
                }
            }

            public unsafe struct ImportedPinnableStruct
            {
                public int* Ptr;

                public ref int GetPinnableReference() => ref *Ptr;
            }

            public struct ImportedMacroStruct
            {
                public int Value;

                [GpuOperatorMacro(typeof(ImportedMacroOps), nameof(ImportedMacroOps.Add))]
                public static ImportedMacroStruct operator +(ImportedMacroStruct left, ImportedMacroStruct right) =>
                    throw new GpuOnlyException();

                [GpuOperatorMacro(typeof(ImportedMacroOps), nameof(ImportedMacroOps.BitwiseAnd))]
                public static ImportedMacroStruct operator &(ImportedMacroStruct left, ImportedMacroStruct right) =>
                    throw new GpuOnlyException();

                public static bool operator false(ImportedMacroStruct value) => value.Value == 0;
                public static bool operator true(ImportedMacroStruct value) => value.Value != 0;
            }

            public static partial class ImportedMacroOps
            {
                [GpuDeviceFunction]
                public static void Add(ref ImportedMacroStruct result, ImportedMacroStruct left, ImportedMacroStruct right)
                {
                    result.Value = left.Value + right.Value;
                }

                [GpuDeviceFunction]
                public static void BitwiseAnd(
                    ref ImportedMacroStruct result,
                    ImportedMacroStruct left,
                    ImportedMacroStruct right)
                {
                    result.Value = left.Value != 0 && right.Value != 0 ? 1 : 0;
                }
            }

            public struct ImportedCrossMacroStruct
            {
                public int Value;

                [GpuOperatorMacro(typeof(ExportedMacroOps), nameof(ExportedMacroOps.CrossAdd))]
                public static ImportedCrossMacroStruct operator +(
                    ImportedCrossMacroStruct left,
                    ImportedCrossMacroStruct right) =>
                    throw new GpuOnlyException();

                [GpuOperatorMacro(typeof(ExportedMacroOps), nameof(ExportedMacroOps.CrossAnd))]
                public static ImportedCrossMacroStruct operator &(
                    ImportedCrossMacroStruct left,
                    ImportedCrossMacroStruct right) =>
                    throw new GpuOnlyException();

                public static bool operator false(ImportedCrossMacroStruct value) => value.Value == 0;
                public static bool operator true(ImportedCrossMacroStruct value) => value.Value != 0;
            }
            """;

        const string ConsumerSource = """
            using Kassyi.GSharp.Core;
            using NoExportStructLib;

            namespace NoExportStructConsumer;

            public static partial class ConsumerKernels
            {
                [GpuKernel]
                public static void CallRecStructCtorKernel(int[] data)
                {
                    var s = new ImportedRecStruct(data[0]);
                    data[0] = s.Value;
                }

                [GpuKernel]
                public static void CallCtorCallingDeviceAttrKernel(int[] data)
                {
                    var s = new ImportedCtorCallingDeviceAttrStruct(data[0]);
                    data[0] = s.Value;
                }

                [GpuKernel]
                public static void CallRecStructMethodKernel(int[] data)
                {
                    var s = default(ImportedRecStruct);
                    data[0] = s.Compute(data[0]);
                }

                [GpuKernel]
                public static void CallRecStructComputedPropKernel(int[] data)
                {
                    var s = default(ImportedRecStruct);
                    s.Value = data[0];
                    data[0] = s.RecProp;
                }

                [GpuKernel]
                public static void CallUserOperatorKernel(int[] data)
                {
                    var a = default(ImportedRecStruct);
                    a.Value = data[0];
                    var b = default(ImportedRecStruct);
                    b.Value = 2;
                    var c = a * b;
                    data[0] = c.Value;
                }

                [GpuKernel]
                public static void CallConversionKernel(int[] data)
                {
                    var s = default(ImportedRecStruct);
                    s.Value = data[0];
                    int x = s;
                    data[0] = x;
                }

                [GpuKernel]
                public static void CallDisposeKernel(int[] data)
                {
                    using var s = default(ImportedDisposableStruct);
                    data[0] = s.Value;
                }

                [GpuKernel]
                public static unsafe void CallFixedPinnableKernel(int[] data)
                {
                    var s = default(ImportedPinnableStruct);
                    fixed (int* p = s)
                    {
                        data[0] = *p;
                    }
                }

                [GpuKernel]
                public static void CallOperatorMacroKernel(int[] data)
                {
                    var a = default(ImportedMacroStruct);
                    a.Value = data[0];
                    var b = default(ImportedMacroStruct);
                    b.Value = 1;
                    var c = a + b;
                    data[0] = c.Value;
                }

                [GpuKernel]
                public static void CallTruthOperatorKernel(int[] data)
                {
                    var a = default(ImportedMacroStruct);
                    a.Value = data[0];
                    var b = default(ImportedMacroStruct);
                    b.Value = 1;
                    var c = a && b;
                    data[0] = c.Value;
                }

                [GpuKernel]
                public static void CallCrossAssemblyOperatorMacroKernel(int[] data)
                {
                    var a = default(ImportedCrossMacroStruct);
                    a.Value = data[0];
                    var b = default(ImportedCrossMacroStruct);
                    b.Value = 1;
                    var c = a + b;
                    data[0] = c.Value;
                }

                [GpuKernel]
                public static void CallCrossAssemblyTruthOperatorKernel(int[] data)
                {
                    var a = default(ImportedCrossMacroStruct);
                    a.Value = data[0];
                    var b = default(ImportedCrossMacroStruct);
                    b.Value = 1;
                    var c = a && b;
                    data[0] = c.Value;
                }

                [GpuKernel]
                public static void CallExplicitDefaultCtorKernel(int[] data)
                {
                    var s = new ImportedExplicitDefaultStruct();
                    data[0] = s.Value;
                }

                [GpuKernel]
                public static void CallExplicitParamCtorKernel(int[] data)
                {
                    var s = new ImportedPlainStruct(data[0]);
                    data[0] = s.Value;
                }

                [GpuKernel]
                public static void CallImplicitDefaultCtorKernel(int[] data)
                {
                    var s = new ImportedPlainStruct();
                    data[0] = s.Value;
                }

                [GpuKernel]
                public static void CallAutoPropertyKernel(int[] data)
                {
                    var s = default(ImportedPlainStruct);
                    s.AutoProp = data[0];
                    data[0] = s.AutoProp;
                }
            }
            """;

        var macroTargetLib = DeviceLibraryBuilder.Build("ExportedMacroTargetLib", null, MacroTargetLibSource);
        var library = DeviceLibraryBuilder.BuildWithReferences(
            "NoExportStructLib",
            new Dictionary<string, string> { ["build_property.GSharpExportDeviceCode"] = "false" },
            [macroTargetLib],
            LibrarySource);
        var consumer = DeviceLibraryBuilder.CreateConsumer(ConsumerSource, macroTargetLib, library);
        Assert.Empty(consumer.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));
        return consumer;
    }

    private static CSharpCompilation BuildExportedStructConsumer()
    {
        const string LibrarySource = """
            using Kassyi.GSharp.Core;

            namespace ExportedStructLib;

            public struct ImportedPlainStruct
            {
                public int Value;
                public int AutoProp { get; set; }

                public ImportedPlainStruct(int value)
                {
                    Value = value;
                    AutoProp = value;
                }
            }

            public struct ImportedFieldInitStruct
            {
                public int Value = 5;

                public ImportedFieldInitStruct(int value)
                {
                    Value = value;
                }
            }

            public record struct ImportedRecordStruct(int Value);

            public struct ImportedExplicitDefaultStruct
            {
                public int Value;

                public ImportedExplicitDefaultStruct()
                {
                    Value = 1;
                }
            }

            public struct ImportedRecStruct
            {
                public int Value;

                public ImportedRecStruct(int n)
                {
                    Value = Compute(n);
                }

                public int Compute(int n) => n <= 0 ? 0 : Compute(n - 1);
            }

            public static partial class ExportDrivers
            {
                [GpuKernel]
                public static void ExportAllStructsKernel(int[] data)
                {
                    var p = new ImportedPlainStruct(data[0]);
                    var f = new ImportedFieldInitStruct(data[0]);
                    var r = new ImportedRecordStruct(data[0]);
                    var e = new ImportedExplicitDefaultStruct();
                    var rec = new ImportedRecStruct(data[0]);
                    data[0] = p.AutoProp + f.Value + r.Value + e.Value + rec.Value;
                }
            }
            """;

        const string ConsumerSource = """
            using ExportedStructLib;
            using Kassyi.GSharp.Core;

            namespace ExportedStructConsumer;

            public static partial class ConsumerKernels
            {
                [GpuKernel]
                public static void CallImplicitDefaultCtorKernel(int[] data)
                {
                    var s = new ImportedPlainStruct();
                    data[0] = s.Value;
                }

                [GpuKernel]
                public static void CallAutoPropertyKernel(int[] data)
                {
                    var s = default(ImportedPlainStruct);
                    s.AutoProp = data[0];
                    data[0] = s.AutoProp;
                }

                [GpuKernel]
                public static void CallFieldInitImplicitDefaultCtorKernel(int[] data)
                {
                    var s = new ImportedFieldInitStruct();
                    data[0] = s.Value;
                }

                [GpuKernel]
                public static void CallRecordImplicitDefaultCtorKernel(int[] data)
                {
                    var r = new ImportedRecordStruct();
                    data[0] = r.Value;
                }

                [GpuKernel]
                public static void CallRecordAutoPropertyKernel(int[] data)
                {
                    var r = default(ImportedRecordStruct);
                    data[0] = r.Value;
                }

                [GpuKernel]
                public static void CallExplicitDefaultCtorKernel(int[] data)
                {
                    var s = new ImportedExplicitDefaultStruct();
                    data[0] = s.Value;
                }

                [GpuKernel]
                public static void CallExplicitParamCtorKernel(int[] data)
                {
                    var s = new ImportedPlainStruct(data[0]);
                    data[0] = s.Value;
                }

                [GpuKernel]
                public static void CallRecordPrimaryCtorKernel(int[] data)
                {
                    var r = new ImportedRecordStruct(data[0]);
                    data[0] = r.Value;
                }

                [GpuKernel]
                public static void CallRecStructCtorKernel(int[] data)
                {
                    var s = new ImportedRecStruct(data[0]);
                    data[0] = s.Value;
                }
            }
            """;

        var library = DeviceLibraryBuilder.Build("ExportedStructLib", null, LibrarySource);
        var consumer = DeviceLibraryBuilder.CreateConsumer(ConsumerSource, library);
        Assert.Empty(consumer.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error));
        return consumer;
    }

    private static IMethodSymbol GetFunction(string methodName) =>
        (s_compilation.GetTypeByMetadataName("SuspensionAnalysisCases.Functions")
            ?? throw new InvalidOperationException("Type 'Functions' was not found."))
        .GetMembers(methodName)
        .OfType<IMethodSymbol>()
        .Single();

    private static SyntaxNode GetBody(string methodName)
    {
        var method = GetFunction(methodName);
        var declaration = (MethodDeclarationSyntax)method.DeclaringSyntaxReferences[0].GetSyntax();
        return declaration.Body ?? (SyntaxNode?)declaration.ExpressionBody
            ?? throw new InvalidOperationException($"Method '{methodName}' has no body.");
    }
}
