using Kassyi.GSharp.Compiler.Transpiler;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// The plan of how a managed context or frame holds the frame of a device function it calls that waits at a barrier
/// (<see cref="ManagedStateMachineAvailability.GetCallForm"/>), without reading generated text: by the frame's type where
/// the caller can name it, as an object run through the generated entries where the frame is private to another class,
/// and not at all where the frame would hold a file-local type.
/// </summary>
[Category(TestCategory.Compiler)]
public class SuspendingCallFormTests
{
    private const string Namespace = "Kassyi.GSharp.Tests.TestCases.Analyzers.";

    private static readonly Compilation s_compilation = RoslynTestUtility.CreateCompilation(
        RoslynTestUtility.ReadTestCase("Analyzers/SuspendingCallFrameSadPath.cs")
    );

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("FrameOwner", "FrameOwner", "HoldsPrivate", "Direct")]
    [InlineData("FrameOwner+NestedCaller", "FrameOwner", "HoldsPrivate", "Direct")]
    [InlineData("FrameCaller", "FrameOwner", "HoldsPrivate", "Entry")]
    [InlineData("FrameRelay", "FrameOwner", "HoldsPrivate", "Entry")]
    [InlineData("FrameCaller", "FrameRelay", "Relays", "Direct")]
    [InlineData("FrameDerived", "FrameBase", "HoldsProtected", "Entry")]
    [InlineData("FrameBase", "FrameBase", "HoldsProtected", "Direct")]
    [InlineData("FrameCaller", "FileLocalHolder", "HoldsFileLocal", "Unavailable")]
    [InlineData("FrameCaller", "FileLocalRelay", "Relays", "Unavailable")]
    public void GetCallForm_CallerAndCallee_HoldsTheFrameThatWay(string caller, string calleeClass, string callee,
        string expected)
    {
        var form = ManagedStateMachineAvailability.GetCallForm(GetFunction(calleeClass, callee), GetType(caller),
            s_compilation);

        // The form is internal, so the cases name it
        Assert.Equal(expected, form.ToString());
    }

    // The frame of a function that holds a private frame as an object names no private type, so it stays internal; a
    // private frame has the entries unless its function is private (a protected function's are protected)
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("FrameOwner", "HoldsPrivate", Accessibility.Private, true)]
    [InlineData("FrameRelay", "Relays", Accessibility.Internal, false)]
    [InlineData("FrameOwner", "CallsHoldsPrivate", Accessibility.Private, true)]
    [InlineData("FrameOwner", "PrivateHoldsPrivate", Accessibility.Private, false)]
    [InlineData("FrameBase", "HoldsProtected", Accessibility.Private, true)]
    public void GetFrameAccessibility_FrameHoldsPrivateFrame_EntriesOnlyForPrivateFrames(string calleeClass,
        string callee, Accessibility expected, bool hasEntries)
    {
        var function = GetFunction(calleeClass, callee);

        var frame = ManagedStateMachineAvailability.GetFrameAccessibility(function, s_compilation);

        Assert.Equal((expected, hasEntries), (frame, ManagedStateMachineAvailability.HasEntries(function, frame)));
    }

    // One call per form, in call order; a callee without a frame leaves no plan and names that callee
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void PlanCallFrames_CalleeWithoutFrame_ReturnsNoPlanAndTheCallee()
    {
        var within = GetType("FrameCaller");
        var shared = GetFunction("FrameOwner", "HoldsPrivate");
        var relay = GetFunction("FrameRelay", "Relays");
        var fileLocal = GetFunction("FileLocalHolder", "HoldsFileLocal");

        var plan = ManagedStateMachineAvailability.PlanCallFrames([relay, shared], within, s_compilation, out var none);
        var missing = ManagedStateMachineAvailability.PlanCallFrames([shared, fileLocal], within, s_compilation,
            out var unavailable);

        Assert.Equal([new ManagedCallFrame(relay, false), new ManagedCallFrame(shared, true)], plan);
        Assert.Null(none);
        Assert.Null(missing);
        Assert.True(SymbolEqualityComparer.Default.Equals(fileLocal, unavailable));
    }

    private static INamedTypeSymbol GetType(string name) =>
        s_compilation.GetTypeByMetadataName(Namespace + name)
        ?? throw new InvalidOperationException($"The test case has no type '{name}'.");

    private static IMethodSymbol GetFunction(string type, string name) =>
        GetType(type).GetMembers(name).OfType<IMethodSymbol>().Single();
}
