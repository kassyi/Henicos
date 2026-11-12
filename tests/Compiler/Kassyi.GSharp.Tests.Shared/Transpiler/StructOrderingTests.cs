#pragma warning disable CS0649
namespace Kassyi.GSharp.Tests.Shared.Transpiler;

public class StructOrderingTests : TranspilerTestBase
{
    // Resolves within this file only: the shared compilation may contain other types with the same name.
    private static INamedTypeSymbol GetLocalTypeSymbol(string name, [CallerFilePath] string callerFilePath = "")
    {
        var (comp, tree) = RoslynTestUtility.GetCallerSourceContext(callerFilePath);
        var model = comp.GetSemanticModel(tree);
        return tree.GetRoot().DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .Select(t => model.GetDeclaredSymbol(t))
            .OfType<INamedTypeSymbol>()
            .First(t => t.Name == name);
    }

    private static Compilation GetLocalCompilation([CallerFilePath] string callerFilePath = "") =>
        RoslynTestUtility.GetCallerSourceContext(callerFilePath).Compilation;

    [Fact]
    [Category(TestCategory.Unit)]
    public void TopologicallySortedStructs_EmptyStruct_EmitsStructDeclaration()
    {
        var structSym = GetLocalTypeSymbol("EmptyStruct");
        var sorted = StructOrdering.GetTopologicallySortedStructs([structSym], GetLocalCompilation());

        Assert.Single(sorted);
        Assert.Equal("EmptyStruct", sorted[0].Name);
    }

    [Fact]
    [Category(TestCategory.Unit)]
    public void TopologicallySortedStructs_DeepNesting_OrdersNestedStructsFirst()
    {
        var symA = GetLocalTypeSymbol("StructEdgeCases_StructA");
        var symB = GetLocalTypeSymbol("StructEdgeCases_StructB");
        var symC = GetLocalTypeSymbol("StructEdgeCases_StructC");
        var symD = GetLocalTypeSymbol("StructEdgeCases_StructD");

        var sorted = StructOrdering.GetTopologicallySortedStructs([symA, symB, symC, symD], GetLocalCompilation());

        Assert.Equal(4, sorted.Length);
        // Order must be D -> C -> B -> A (dependents last)
        Assert.Equal("StructEdgeCases_StructD", sorted[0].Name);
        Assert.Equal("StructEdgeCases_StructC", sorted[1].Name);
        Assert.Equal("StructEdgeCases_StructB", sorted[2].Name);
        Assert.Equal("StructEdgeCases_StructA", sorted[3].Name);
    }

    [Fact]
    [Category(TestCategory.Unit)]
    public void TopologicallySortedStructs_SystemTypes_ExcludesSystemTypes()
    {
        var structSym = GetLocalTypeSymbol(nameof(MyStruct));
        var sorted = StructOrdering.GetTopologicallySortedStructs([structSym], GetLocalCompilation());

        Assert.Single(sorted);
        Assert.Equal(nameof(MyStruct), sorted[0].Name);
    }

    // A struct that only the constructor of another struct creates comes before it (#1158)
    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void TopologicallySortedStructs_StructInstantiatedInConstructor_PutsInstantiatedStructFirst()
    {
        var outer = GetLocalTypeSymbol("StructBodyEdge_Outer");
        var inner = GetLocalTypeSymbol("StructBodyEdge_Inner");

        var sorted = StructOrdering.GetTopologicallySortedStructs([outer, inner], GetLocalCompilation());

        Assert.Equal(["StructBodyEdge_Inner", "StructBodyEdge_Outer"], sorted.Select(static s => s.Name));
    }

    // The types of every emitted body: a constructor, a computed property, a field initializer; not a method
    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void StructBodyDependencies_EmittedBodies_ListsStructsOfEmittedBodiesOnly()
    {
        var outer = GetLocalTypeSymbol("StructBodyEdge_Outer");

        var names = StructBodyDependencies.Get(outer, GetLocalCompilation())
            .Select(static t => t.Name)
            .OrderBy(static n => n, StringComparer.Ordinal);

        Assert.Equal(["StructBodyEdge_Initialized", "StructBodyEdge_Inner", "StructBodyEdge_Read"], names);
    }

    // A type named only for a constant, as the receiver of a static member or inside nameof is not a dependency
    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void StructBodyDependencies_TypesNamedOnlyInStaticOrNameofContexts_LeavesOutTypes()
    {
        var user = GetLocalTypeSymbol("StructBodyEdge_NamesOnly");

        var names = StructBodyDependencies.Get(user, GetLocalCompilation())
            .Select(static t => t.Name);

        Assert.Equal(["StructBodyEdge_Inner"], names);
    }

    // A generic struct's body types come with its type arguments substituted
    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void StructBodyDependencies_GenericStructTypeArguments_SubstitutesTypeArguments()
    {
        var holder = GetLocalTypeSymbol("StructBodyEdge_Holder");
        var inner = GetLocalTypeSymbol("StructBodyEdge_Inner");
        var constructed = holder.Construct(inner);

        var types = StructBodyDependencies.Get(constructed, GetLocalCompilation());

        Assert.Contains(types, t => SymbolEqualityComparer.Default.Equals(t, inner));
        Assert.DoesNotContain(types, static t => t is ITypeParameterSymbol);
        Assert.DoesNotContain(types, t => SymbolEqualityComparer.Default.Equals(t, constructed));
    }
}

struct EmptyStruct
{
}

struct StructEdgeCases_StructA
{
    public StructEdgeCases_StructB b;
}

struct StructEdgeCases_StructB
{
    public StructEdgeCases_StructC c;
}

struct StructEdgeCases_StructC
{
    public StructEdgeCases_StructD d;
}

struct StructEdgeCases_StructD
{
    public int val;
}

struct MyStruct
{
    public System.DateTime time;
}

struct StructBodyEdge_Inner
{
    public int V;

    public StructBodyEdge_Inner(int v)
    {
        V = v;
    }
}

struct StructBodyEdge_Initialized
{
    public int V;
}

struct StructBodyEdge_Read
{
    public int V;
}

struct StructBodyEdge_OnlyInAMethod
{
    public int V;
}

struct StructBodyEdge_Outer
{
    public int V;
    public int W = new StructBodyEdge_Initialized().V;

    public StructBodyEdge_Outer(int v)
    {
        var inner = new StructBodyEdge_Inner(v);
        V = inner.V;
    }

    public int Read => default(StructBodyEdge_Read).V + V;

    public static int NotEmitted() => new StructBodyEdge_OnlyInAMethod().V;
}

struct StructBodyEdge_Holder<T>
    where T : struct
{
    public int V;

    public StructBodyEdge_Holder(int v)
    {
        T value = default;
        V = v + (value.Equals(default(T)) ? 0 : 1);
    }
}

struct StructBodyEdge_HostOnly
{
    public const int Value = 7;
    public string Name;

    public static int Twice(int x) => x * 2;
}

struct StructBodyEdge_NamesOnly
{
    public int V;

    public StructBodyEdge_NamesOnly(int v)
    {
        V = v + StructBodyEdge_HostOnly.Value + StructBodyEdge_HostOnly.Twice(v);
        V += nameof(StructBodyEdge_HostOnly).Length;
        V += new StructBodyEdge_Inner(v).V;
    }
}
