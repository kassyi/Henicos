#nullable enable
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

/// <summary>
/// The release of the result cache (#786): each owned value is disposed and cleared even when its release throws, and
/// the failures are thrown once at the end (the exception itself for one, an AggregateException for several).
/// </summary>
[Category(TestCategory.Unit)]
public sealed class VectorResultCacheTests
{
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Release_WhenDisposeThrows_CollectsTheErrorAndClearsTheField()
    {
        // mutant: Release without the finally that clears the field (the field still holds the value)
        Releasable.Disposed.Clear();
        var failing = new Releasable(1, Throws: true);
        var next = new Releasable(2, Throws: false);
        List<Exception>? errors = null;

        VectorResultCache.Release(ref failing, ref errors);
        VectorResultCache.Release(ref next, ref errors);

        Assert.Equal(default, failing);
        Assert.Equal(default, next);
        var error = Assert.Single(Assert.IsType<List<Exception>>(errors));
        Assert.Equal("1", error.Message);
        Assert.Equal([1, 2], Releasable.Disposed);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void ThrowIfReleaseFailed_WithoutErrors_DoesNothing()
    {
        Assert.Null(Record.Exception(() => VectorResultCache.ThrowIfReleaseFailed(null)));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void ThrowIfReleaseFailed_WithOneError_ThrowsItAsItIs()
    {
        // mutant: one error wrapped in an AggregateException as well
        var original = new InvalidOperationException("refused");

        var thrown = Assert.Throws<InvalidOperationException>(() => VectorResultCache.ThrowIfReleaseFailed([original]));

        Assert.Same(original, thrown);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void ThrowIfReleaseFailed_WithSeveralErrors_ThrowsThemAllInOrder()
    {
        // mutant: only the last error thrown (the earlier ones are lost, as with nested finally blocks)
        var first = new InvalidOperationException("first");
        var second = new ArgumentException("second");

        var thrown = Assert.Throws<AggregateException>(() => VectorResultCache.ThrowIfReleaseFailed([first, second]));

        Assert.Equal([first, second], thrown.InnerExceptions);
    }

    // A disposable value that records the order of the releases and may throw from Dispose
    private readonly record struct Releasable(int Id, bool Throws) : IDisposable
    {
        [ThreadStatic]
        private static List<int>? t_disposed;

        public static List<int> Disposed => t_disposed ??= [];

        public void Dispose()
        {
            if (Id == 0)
            {
                return;
            }

            Disposed.Add(Id);
            if (Throws)
            {
                throw new InvalidOperationException(Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }
    }
}
