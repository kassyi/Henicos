using System;
using Kassyi.GSharp.Core;

namespace SyncGridBodyCases
{
// Each case method holds one top-level Gpu.SyncGrid(body) statement: the body whose statements and sync gaps are checked
public static class Cases
{
    public static void TwoStatements(int x)
    {
        Gpu.SyncGrid(() =>
        {
            x += 1;
            x += 2;
        });
    }

    public static void Empty()
    {
        Gpu.SyncGrid(() =>
        {
        });
    }

    public static void OneStatement(int x)
    {
        Gpu.SyncGrid(() =>
        {
            x += 1;
        });
    }

    public static void ExpressionBody(int x)
    {
        Gpu.SyncGrid(() => x += 1);
    }

    public static void ExplicitNeighbour(int x)
    {
        Gpu.SyncGrid(() =>
        {
            x += 1;
            Gpu.SyncGrid();
            x += 2;
        });
    }

    public static void ExplicitBetweenEmpties(int x)
    {
        Gpu.SyncGrid(() =>
        {
            x += 1;
            ;
            Gpu.SyncGrid();
            ;
            x += 2;
        });
    }

    public static void TrailingEmpty(int x)
    {
        Gpu.SyncGrid(() =>
        {
            x += 1;
            x += 2;
            ;
        });
    }

    public static void NestedBody(int x)
    {
        Gpu.SyncGrid(() =>
        {
            x += 1;
            Gpu.SyncGrid(() =>
            {
                x += 2;
                x += 3;
            });
            x += 4;
        });
    }

    public static void NestedExpressionBody(int x)
    {
        Gpu.SyncGrid(() =>
        {
            x += 1;
            Gpu.SyncGrid(() => x += 2);
            x += 3;
        });
    }

    public static void DirectNested(int x)
    {
        Gpu.SyncGrid(() =>
        {
            Gpu.SyncGrid(() =>
            {
                x += 500;
            });
        });
    }

    public static void NestedFor(int x)
    {
        Gpu.SyncGrid(() =>
        {
            x += 1;
            for (var i = 0; i < 10; i++)
            {
                x += i;
                x *= 2;
            }
            x += 2;
        });
    }

    public static void LocalFunction(int x)
    {
        Gpu.SyncGrid(() =>
        {
            void Step()
            {
                x += 11;
                x += 22;
            }

            Step();
            x += 33;
        });
    }

    public static void LocalFunctionLast(int x)
    {
        Gpu.SyncGrid(() =>
        {
            x += 1;
            void Step()
            {
                x += 2;
            }
        });
    }

    public static void BlockBarrier(int x)
    {
        Gpu.SyncGrid(() =>
        {
            x += 1;
            Gpu.SyncThreads();
            x += 2;
        });
    }
}
}

namespace SyncGridBodyCases.Lookalikes
{
// A user type named Gpu outside Kassyi.GSharp.Core: not the macro host. Gpu below binds to it, not to the Core one
public static class Gpu
{
    public static void SyncGrid(Action body) => body();
}

public static class LookalikeCases
{
    public static void Lookalike(int x)
    {
        Gpu.SyncGrid(() =>
        {
            x += 1;
            x += 2;
        });
    }
}
}
