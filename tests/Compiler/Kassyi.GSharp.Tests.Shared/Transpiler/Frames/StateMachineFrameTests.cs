#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;
using Kassyi.GSharp.Compiler.Dialects;
using Kassyi.GSharp.Compiler.Lowering;
using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Compiler.Transpiler.Frames;
using Kassyi.GSharp.Compiler.Transpiler.Managed;
using Kassyi.GSharp.Tests.Infrastructure;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Frames;

[Category(TestCategory.Compiler)]
public class StateMachineFrameTests
{
    private static readonly Lazy<CSharpCompilation> s_compilation = new(static () =>
    {
        var path = RepositoryPaths.Combine("tests", "Compiler", "Kassyi.GSharp.Tests.Shared", "TestCases", "Transpiler",
            "Frames", "StateMachineFrameSource.cs");
        var source = RoslynTestUtility.ReadTestCase("Transpiler/Frames/StateMachineFrameSource.cs");
        return RoslynTestUtility.CreateCompilationAt(path, source);
    });

    private static (MethodEmissionPlan Plan, SemanticModel Model) GetPlan(string methodName, bool isKernel)
    {
        var compilation = s_compilation.Value;
        var type = compilation.GetTypeByMetadataName("FrameCases.FrameKernels")
            ?? throw new InvalidOperationException("Type FrameCases.FrameKernels not found.");
        var symbol = type.GetMembers().OfType<IMethodSymbol>().First(m => m.Name == methodName);
        var syntaxRef = symbol.DeclaringSyntaxReferences.FirstOrDefault()
            ?? throw new InvalidOperationException($"Syntax reference not found for {methodName}");
        var declaration = (MethodDeclarationSyntax)syntaxRef.GetSyntax();
        var model = compilation.GetSemanticModel(declaration.SyntaxTree);
        var plan = MethodEmissionPlan.Create(declaration, model, isKernel);
        Assert.NotNull(plan.Lowered);
        return (plan, model);
    }

    // The slot of the hoisted local named <local>: the slots follow the order of the context fields
    private static FrameSlot SlotOf(LoweredStateMachine lowered, StateMachineFrame frame, string local)
    {
        var index = lowered.ContextFields.Select(static f => f.Identifier.ValueText).ToList().IndexOf(local);
        Assert.True(index >= 0, $"No hoisted local '{local}'.");
        return frame.Slots[index];
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void BuildNative_SpanAndAssignedParameter_KeepsTheParameterAndRedeclaresTheSpan()
    {
        var (plan, _) = GetPlan("SlotsKernel", isKernel: true);
        var lowered = plan.Lowered ?? throw new InvalidOperationException("Lowered is null");
        var frame = LoweredFrameBuilder.BuildNative(lowered, in plan, new StandardCppDialect());

        Assert.Equal(["a", "n"], frame.CopiedParameters.Select(static p => p.Name));

        var spanSlot = Assert.Single(frame.Slots, static s =>
            s is
            {
                Kind: FrameSlotKind.RedeclaredStack,
                Type: INamedTypeSymbol { Name: "Span", TypeArguments: [{ SpecialType: SpecialType.System_Int32 }] }
            });
        Assert.Equal(SpecialType.System_Int32, spanSlot.ElementType?.SpecialType);
        Assert.Equal(3, spanSlot.Length);

        Assert.Equal(FrameSlotKind.Field, SlotOf(lowered, frame, "kept").Kind);
        Assert.Equal(FrameSlotKind.Field, SlotOf(lowered, frame, "tid").Kind);

        Assert.Empty(frame.CallFrameCallees);
        Assert.Equal(0, frame.WarpSiteCount);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void BuildNative_StackAllocPointer_RedeclaresItWithItsLength()
    {
        var (plan, _) = GetPlan("PointerKernel", isKernel: true);
        var lowered = plan.Lowered ?? throw new InvalidOperationException("Lowered is null");
        var frame = LoweredFrameBuilder.BuildNative(lowered, in plan, new StandardCppDialect());

        var pointerSlot = SlotOf(lowered, frame, "p");
        Assert.True(pointerSlot.Type is IPointerTypeSymbol { PointedAtType.SpecialType: SpecialType.System_Int32 });
        Assert.Equal(FrameSlotKind.RedeclaredStack, pointerSlot.Kind);
        Assert.Equal(2, pointerSlot.Length);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void BuildNative_SharedMemoryLocal_KeepsItInTheFrame()
    {
        var (plan, _) = GetPlan("SharedKernel", isKernel: true);
        var lowered = plan.Lowered ?? throw new InvalidOperationException("Lowered is null");
        var frame = LoweredFrameBuilder.BuildNative(lowered, in plan, new StandardCppDialect());

        var sharedSlot = SlotOf(lowered, frame, "shared");
        Assert.True(sharedSlot.Type is INamedTypeSymbol { Name: "SharedMemory" });
        Assert.Equal(FrameSlotKind.SharedMemory, sharedSlot.Kind);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void BuildNative_CallThatMaySuspend_RecordsTheCalleeAndTheWarpSite()
    {
        var (plan, _) = GetPlan("CallerKernel", isKernel: true);
        var lowered = plan.Lowered ?? throw new InvalidOperationException("Lowered is null");
        var frame = LoweredFrameBuilder.BuildNative(lowered, in plan, new StandardCppDialect());

        Assert.Equal(["Step"], frame.CallFrameCallees.Select(static c => c.Name));
        Assert.Equal(1, frame.WarpSiteCount);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void ProcessContextFields_SharedMemoryLocal_RedeclaresItWithItsInitializer()
    {
        var (plan, model) = GetPlan("SharedKernel", isKernel: true);
        var lowered = plan.Lowered ?? throw new InvalidOperationException("Lowered is null");
        var (_, _, _, _, slots) =
            ManagedContextFields.ProcessContextFields(lowered.ContextFields, model, "SharedKernel",
                lowered.LoweredBody);

        var sharedSlot = slots.FirstOrDefault(static s => s.Kind == FrameSlotKind.SharedMemory);
        Assert.NotNull(sharedSlot);
        Assert.NotNull(sharedSlot.Managed?.Initializer);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void ProcessContextFields_EverySlot_KeepsTheLoweringOrder()
    {
        var (plan, model) = GetPlan("SlotsKernel", isKernel: true);
        var lowered = plan.Lowered ?? throw new InvalidOperationException("Lowered is null");
        var (_, _, _, _, managedSlots) =
            ManagedContextFields.ProcessContextFields(lowered.ContextFields, model, "SlotsKernel", lowered.LoweredBody);
        var nativeFrame = LoweredFrameBuilder.BuildNative(lowered, in plan, new StandardCppDialect());

        var expectedNames = lowered.ContextFields.Select(static f => f.Identifier.ValueText).ToList();
        Assert.Equal(expectedNames, managedSlots.Select(static s => s.Name).ToList());

        Assert.Equal(expectedNames.Count, nativeFrame.Slots.Length);
        Assert.Equal(managedSlots.Length, nativeFrame.Slots.Length);

        for (var i = 0; i < managedSlots.Length; i++)
        {
            Assert.Equal(nativeFrame.Slots[i].Type, managedSlots[i].Type, SymbolEqualityComparer.Default);
        }
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void BuildNative_EverySlot_SplitsAsSeparateDoes()
    {
        var methods = new (string Name, bool IsKernel)[]
        {
            ("SlotsKernel", true),
            ("PointerKernel", true),
            ("SharedKernel", true),
            ("CallerKernel", true),
            ("Step", false)
        };

        var dialect = new StandardCppDialect();
        foreach (var (name, isKernel) in methods)
        {
            var (plan, _) = GetPlan(name, isKernel);
            var lowered = plan.Lowered ?? throw new InvalidOperationException($"Lowered is null for {name}");
            var frame = LoweredFrameBuilder.BuildNative(lowered, in plan, dialect);
            var (separateLowered, resumeLocals) = ResumeLocalEmitter.Separate(lowered, in plan, dialect);

            var inFrameCount = frame.Slots.Count(static s =>
                s.Kind is FrameSlotKind.Field or FrameSlotKind.SharedMemory or FrameSlotKind.HeldRef);
            var resumeCount = frame.Slots.Length - inFrameCount;

            Assert.Equal(separateLowered.ContextFields.Count, inFrameCount);
            Assert.Equal(resumeLocals.Length, resumeCount);
        }
    }
}
