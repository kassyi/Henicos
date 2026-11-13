using System;

namespace UsingExitPlanCases;

public struct Res : IDisposable
{
    public int[] Log;

    public void Dispose() => Log[0]++;
}

public static class Cases
{
    // The return leaves both usings; the plan of each has one exit, and the outer one declares the kept value
    public static int Nested(int[] log, bool early)
    {
        using (var a = new Res { Log = log })
        {
            using (var b = new Res { Log = log })
            {
                if (early)
                {
                    return 1;
                }
            }

            return 2;
        }
    }

    // goto case of the switch around the using leaves it; goto case of a switch inside the body does not
    public static void GotoCase(int[] log, int mode)
    {
        switch (mode)
        {
            case 0:
                using (var r = new Res { Log = log })
                {
                    switch (log[0])
                    {
                        case 0:
                            goto case 1;
                        case 1:
                            break;
                    }

                    if (mode == 0)
                    {
                        goto case 2;
                    }
                }

                break;
            case 2:
                log[1] = 2;
                break;
        }
    }

    // The returns of a lambda and of a local function in the body stay in the body
    public static void Functions(int[] log)
    {
        using (var r = new Res { Log = log })
        {
            Func<int, int> pick = v =>
            {
                if (v == 0)
                {
                    return 1;
                }

                return v;
            };

            int Twice(int v)
            {
                return v * 2;
            }

            log[0] = pick(Twice(log[1]));
        }
    }

    // Two gotos to one label share one exit
    public static void SameLabel(int[] log)
    {
        using (var r = new Res { Log = log })
        {
            if (log[0] == 1)
            {
                goto done;
            }

            if (log[0] == 2)
            {
                goto done;
            }

            log[1] = 1;
        }

        done:
        log[2] = 1;
    }

    // break and continue of the loop around the using, a break of a loop inside the body
    public static void Loops(int[] log)
    {
        for (int i = 0; i < 4; i++)
        {
            using (var r = new Res { Log = log })
            {
                for (int j = 0; j < 2; j++)
                {
                    if (j == 1)
                    {
                        break;
                    }
                }

                if (i == 1)
                {
                    continue;
                }

                if (i == 2)
                {
                    break;
                }
            }
        }
    }

    // A by-ref return: the plan keeps a pointer
    public static ref int ByRef(int[] log)
    {
        using var r = new Res { Log = log };
        return ref log[1];
    }
}
