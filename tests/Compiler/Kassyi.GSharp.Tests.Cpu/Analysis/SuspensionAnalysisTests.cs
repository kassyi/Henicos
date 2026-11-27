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
