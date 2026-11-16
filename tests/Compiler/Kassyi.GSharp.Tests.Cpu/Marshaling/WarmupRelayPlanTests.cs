using Kassyi.GSharp.Compiler.Marshaling;

namespace Kassyi.GSharp.Tests.Shared.Marshaling;

/// <summary>
/// Verifies the warmup relays that the containers of a kernel class the module context cannot name declare: which
/// containers hold them, what each one calls, and which kernels get one.
/// </summary>
[Category(TestCategory.Compiler)]
public class WarmupRelayPlanTests
{
    private static readonly Compilation s_compilation = RoslynTestUtility.CreateCompilation(
        RoslynTestUtility.ReadTestCase("Marshaling/WarmupRelaySource.cs")
    );

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Create_NameableClass_HasNoRelays()
    {
        var header = HostClassHeader.CreateForKernels(GetType("Relay.PublicKernels"), s_compilation);

        Assert.True(header.WarmupRelayHosts.IsDefaultOrEmpty);
        Assert.True(WarmupRelayPlan.Create(header, [Kernel("Fill", "r_fill")]).IsDefaultOrEmpty);
    }

    // The container relays straight to the class's warmup method
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Create_PrivateClass_RelaysFromTheContainerToTheClass()
    {
        var header = HostClassHeader.CreateForKernels(GetType("Relay.OneLevel+Kernels"), s_compilation);

        Assert.Equal(["global::Relay.OneLevel"], header.WarmupRelayHosts);
        Assert.Equal(
            [new WarmupRelay(0, "r_fill", "global::Relay.OneLevel.Kernels", "WarmupFunctionPointerFill")],
            WarmupRelayPlan.Create(header, [Kernel("Fill", "r_fill")]));
    }

    // Each host calls the same relay of the next one, the last one the class; a kernel without a warmup (GSH0059)
    // gets no relay
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Create_PrivateClassInPrivateClass_ChainsTheRelaysInward()
    {
        var header = HostClassHeader.CreateForKernels(GetType("Relay.TwoLevels+Middle+Kernels"), s_compilation);

        Assert.Equal(["global::Relay.TwoLevels", "global::Relay.TwoLevels.Middle"], header.WarmupRelayHosts);
        Assert.Equal(
            [
                new WarmupRelay(0, "r_a", "global::Relay.TwoLevels.Middle", "r_a"),
                new WarmupRelay(0, "r_b", "global::Relay.TwoLevels.Middle", "r_b"),
                new WarmupRelay(1, "r_a", "global::Relay.TwoLevels.Middle.Kernels", "WarmupFunctionPointerA"),
                new WarmupRelay(1, "r_b", "global::Relay.TwoLevels.Middle.Kernels", "WarmupFunctionPointerB__o1"),
            ],
            WarmupRelayPlan.Create(
                header,
                [Kernel("A", "r_a"), Kernel("B__o1", "r_b"), Kernel("C", "r_c", isIndistinguishable: true)]));
    }

    // A container the assembly can name below one it cannot: the relays start at the innermost nameable container
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Create_InternalContainerOfPrivateClass_StartsAtTheInnermostNameableContainer()
    {
        var header = HostClassHeader.CreateForKernels(GetType("Relay.Outermost+Reachable+Kernels"), s_compilation);

        Assert.Equal(["global::Relay.Outermost.Reachable"], header.WarmupRelayHosts);
        Assert.Equal(
            [new WarmupRelay(1, "r_fill", "global::Relay.Outermost.Reachable.Kernels", "WarmupFunctionPointerFill")],
            WarmupRelayPlan.Create(header, [Kernel("Fill", "r_fill")]));
    }

    // A kernel in a file-local type is not launchable (KernelDeclaration.IsLaunchable), so its class never needs relays
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void CreateWarmupRelayHosts_FileLocalContainer_ThrowsInternalError()
    {
        var type = s_compilation.GetSymbolsWithName("Kernels", SymbolFilter.Type)
            .OfType<INamedTypeSymbol>()
            .Single(static t => t.ContainingType is { IsFileLocal: true });

        Assert.Throws<InvalidOperationException>(() => HostClassHeader.CreateWarmupRelayHosts(type, s_compilation));
    }

    private static KernelEmission Kernel(string memberKey, string relayName, bool isIndistinguishable = false) =>
        new(
            new KernelHostModel { Name = memberKey, WarmupRelayName = relayName },
            memberKey,
            IsOverloaded: false,
            isIndistinguishable,
            CudaSource: "",
            CppSource: "",
            ManagedCSharpSource: "",
            InternalError: "");

    private static INamedTypeSymbol GetType(string metadataName) =>
        s_compilation.GetTypeByMetadataName(metadataName)
        ?? throw new InvalidOperationException($"Type '{metadataName}' not found.");
}
