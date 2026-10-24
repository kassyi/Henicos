#pragma warning disable CS0219, CS8321
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Kassyi.GSharp.Tests.Generators.StateMachines;

public class IteratorTests : StateMachineTestBase
{
    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public Task Lower_Iterator_StraightLine_TwoYields() => VerifyIteratorLower(() =>
    {
        IEnumerable<int> Iterator()
        {
            int a = 1;
            yield return a;
            int b = a + 2;
            yield return b;
        }
    });

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public Task Lower_Iterator_YieldInIfStatement() => VerifyIteratorLower(() =>
    {
        IEnumerable<int> Iterator(bool flag)
        {
            if (flag)
            {
                yield return 10;
            }
            else
            {
                yield return 20;
            }
        }
    });

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public Task Lower_Iterator_YieldInForStatement() => VerifyIteratorLower(() =>
    {
        IEnumerable<int> Iterator(int start, int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return start + i;
            }
        }
    });

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public Task Lower_Iterator_YieldInArrayForEach() => VerifyIteratorLower(() =>
    {
        IEnumerable<int> Iterator(int[] data)
        {
            foreach (int x in data)
            {
                yield return x * 2;
            }
        }
    });

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public Task Lower_Iterator_YieldBreak() => VerifyIteratorLower(() =>
    {
        IEnumerable<int> Iterator(int x)
        {
            if (x < 0)
            {
                yield break;
            }

            yield return x;
        }
    });

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public Task Lower_Iterator_VariablesAcrossYield() => VerifyIteratorLower(() =>
    {
        IEnumerable<int> Iterator()
        {
            int a = 1;
            yield return a;
            int c = a + 3;
        }
    });

    [Fact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.Boundary)]
    public Task Lower_Iterator_AssignedParameters()
    {
        var lowered = LowerIteratorFromDelegate(() =>
        {
            IEnumerable<int> Iterator(int n, int step)
            {
                while (n > 0)
                {
                    n -= step;
                    yield return n;
                }
            }
        });

        Assert.Equal(["n"], lowered.AssignedParameters);

        return VerifyIteratorLower(() =>
        {
            IEnumerable<int> Iterator(int n, int step)
            {
                while (n > 0)
                {
                    n -= step;
                    yield return n;
                }
            }
        });
    }
}
