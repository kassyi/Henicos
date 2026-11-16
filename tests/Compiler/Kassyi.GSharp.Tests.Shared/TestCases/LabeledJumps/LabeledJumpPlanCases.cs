#nullable enable
using System;
using System.Collections.Generic;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.LabeledJumps;

public static class LabeledJumpPlanCases
{
    [GpuDeviceFunction]
    public static IEnumerable<int> CountTwo()
    {
        yield return 1;
        yield return 2;
    }

    public static int InnermostLoopNamed(int n)
    {
        int sum = 0;
        outer:
        for (int i = 0; i < n; i++)
        {
            if (i == 1)
            {
                continue outer;
            }

            if (i == 3)
            {
                break outer;
            }

            sum += i;
        }

        return sum;
    }

    public static int NestedLoops(int n)
    {
        int sum = 0;
        outer:
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                if (j == 2)
                {
                    break outer;
                }

                if (j == 1)
                {
                    continue outer;
                }

                if (j == 3)
                {
                    break;
                }

                sum += i + j;
            }
        }

        return sum;
    }

    public static int SwitchInsideLabeledLoop(int n)
    {
        int sum = 0;
        outer:
        for (int i = 0; i < n; i++)
        {
            switch (i & 3)
            {
                case 0:
                    continue outer;
                case 1:
                    break outer;
                default:
                    sum += i;
                    break;
            }
        }

        return sum;
    }

    public static int TwoLabelsOnOneLoop(int n)
    {
        int i = 0;
        a:
        b:
        while (i < n)
        {
            if (i == 1)
            {
                break b;
            }

            i++;
        }

        if (n < 0)
        {
            goto a;
        }

        return i;
    }

    public static int ThreeLoopsMiddleLabeled(int n)
    {
        int sum = 0;
        top:
        for (int i = 0; i < n; i++)
        {
            mid:
            for (int j = 0; j < n; j++)
            {
                for (int k = 0; k < n; k++)
                {
                    if (k == 1)
                    {
                        break mid;
                    }

                    if (k == 2)
                    {
                        continue top;
                    }

                    sum += i + j + k;
                }
            }
        }

        return sum;
    }

    public static int OnlyPlainJumps(int n)
    {
        int sum = 0;
        outer:
        for (int i = 0; i < n; i++)
        {
            if (i == 0)
            {
                continue outer;
            }

            switch (i)
            {
                case 1:
                    continue outer;
                default:
                    break;
            }

            if (i == 4)
            {
                break outer;
            }

            sum += i;
        }

        return sum;
    }

    public static int IteratorForEachLabeled()
    {
        int sum = 0;
        outer:
        foreach (var item in CountTwo())
        {
            for (int j = 0; j < 2; j++)
            {
                if (j == 0)
                {
                    continue outer;
                }

                if (item == 2)
                {
                    break outer;
                }

                sum += item + j;
            }
        }

        return sum;
    }

    public static int OnlyBreakOuter(int n)
    {
        int sum = 0;
        outer:
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                if (i + j > 4)
                {
                    break outer;
                }

                sum += j;
            }
        }

        return sum;
    }

    public static int OnlyContinueOuter(int n)
    {
        int sum = 0;
        outer:
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                if (j == 1)
                {
                    continue outer;
                }

                sum += j;
            }
        }

        return sum;
    }

    public static int NestedDifferentLabels(int n)
    {
        int sum = 0;
        outer:
        for (int i = 0; i < n; i++)
        {
            inner:
            for (int j = 0; j < n; j++)
            {
                for (int k = 0; k < n; k++)
                {
                    if (k == 1)
                    {
                        break inner;
                    }

                    sum += k;
                }
            }
        }

        return sum;
    }

    public static int IteratorBodyWithLabeledJumps(int n)
    {
        int sum = 0;
        other:
        for (int a = 0; a < n; a++)
        {
            outer:
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    if (j == 0)
                    {
                        continue outer;
                    }

                    if (j == 1)
                    {
                        break outer;
                    }

                    if (j == 2)
                    {
                        break other;
                    }

                    sum += j;
                }
            }
        }

        return sum;
    }

    public static int UnrollJumpToOuterLoop(int n)
    {
        int sum = 0;
        outer:
        for (int a = 0; a < n; a++)
        {
            Gpu.ForUnroll(0, 4, i =>
            {
                for (int j = 0; j < 2; j++)
                {
                    if (i + j == 3)
                    {
                        break outer;
                    }

                    sum += i + j;
                }
            });
        }

        return sum;
    }

    public static int UnrollJumpToInnerLoop(int n)
    {
        int sum = 0;
        Gpu.ForUnroll(0, 4, i =>
        {
            inner:
            for (int j = 0; j < 2; j++)
            {
                for (int k = 0; k < 2; k++)
                {
                    if (k == 1)
                    {
                        break inner;
                    }

                    sum += i + j + k;
                }
            }
        });

        return sum;
    }

    public static int UnrollLabelSameNameAsParameter(int n)
    {
        int sum = 0;
        Gpu.ForUnroll(0, 4, i =>
        {
            i:
            for (int j = 0; j < 2; j++)
            {
                if (j == 0)
                {
                    continue i;
                }

                if (j == 1)
                {
                    break i;
                }

                sum += i + j;
            }
        });

        return sum;
    }
}
