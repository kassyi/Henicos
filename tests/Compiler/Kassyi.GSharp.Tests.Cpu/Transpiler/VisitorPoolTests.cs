namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// The pool of <see cref="RoslynToCudaVisitor"/>: a visitor that is still rented is never handed out again, a returned
/// visitor is handed out again, and a second <see cref="RoslynToCudaVisitor.Dispose"/> does not return it twice.
/// </summary>
/// <remarks>
/// The pool is process-wide, so these tests run with no other test in parallel: a visitor returned by this test
/// would otherwise be taken by another thread before the next <c>Rent</c>.
/// </remarks>
[Collection(TestCollections.DeviceExclusive)]
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class VisitorPoolTests
{
    private static readonly GSharpCudaDialect s_dialect = new();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Rent_WhileOthersAreRented_ReturnsADifferentVisitorEachTime()
    {
        var model = RoslynTestUtility.CreateDummyContext().Model;
        var rented = new List<RoslynToCudaVisitor>();
        try
        {
            for (var i = 0; i < 17; i++)
            {
                rented.Add(RoslynToCudaVisitor.Rent(s_dialect, model));
            }

            Assert.Equal(rented.Count, rented.Distinct(ReferenceEqualityComparer.Instance).Count());
        }
        finally
        {
            foreach (var visitor in rented)
            {
                visitor.Dispose();
            }
        }
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Rent_AfterDispose_ReusesTheReturnedVisitor()
    {
        var model = RoslynTestUtility.CreateDummyContext().Model;
        var first = RoslynToCudaVisitor.Rent(s_dialect, model);
        first.Dispose();

        using var second = RoslynToCudaVisitor.Rent(s_dialect, model);

        Assert.Same(first, second);
        Assert.Same(model, second.Context.Query.SemanticModel);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Dispose_Twice_ReturnsTheVisitorToThePoolOnce()
    {
        var model = RoslynTestUtility.CreateDummyContext().Model;
        var visitor = RoslynToCudaVisitor.Rent(s_dialect, model);
        visitor.Dispose();
        visitor.Dispose();

        using var first = RoslynToCudaVisitor.Rent(s_dialect, model);
        using var second = RoslynToCudaVisitor.Rent(s_dialect, model);

        Assert.NotSame(first, second);
    }
}
