namespace Cases;

public class NestingDepthCases
{
    public void Flat()
    {
        if (Check())
        {
            Work();
        }
    }

    public void ThreeDeep()
    {
        if (Check())
        {
            foreach (var x in Items())
            {
                while (Check())
                {
                    Work();
                }
            }
        }
    }

    public void FourDeep()
    {
        if (Check())
        {
            foreach (var x in Items())
            {
                while (Check())
                {
                    try
                    {
                        Work();
                    }
                    finally
                    {
                    }
                }
            }
        }
    }

    public void ElseIfChain(int n)
    {
        if (n == 0)
        {
        }
        else if (n == 1)
        {
        }
        else if (n == 2)
        {
        }
        else if (n == 3)
        {
        }
        else if (n == 4)
        {
        }
    }

    public void SwitchTwoLoops(int n)
    {
        switch (n)
        {
            case 0:
                for (var i = 0; i < n; i++)
                {
                    do
                    {
                        Work();
                    }
                    while (Check());
                }

                break;
        }
    }

    public void UsingAndLockDoNotCount()
    {
        if (Check())
        {
            lock (this)
            {
                using (System.IO.Stream.Null)
                {
                    foreach (var x in Items())
                    {
                        if (Check())
                        {
                            Work();
                        }
                    }
                }
            }
        }
    }

    public void LambdaStartsAgain()
    {
        if (Check())
        {
            foreach (var x in Items())
            {
                System.Action action = () =>
                {
                    if (Check())
                    {
                        while (Check())
                        {
                            Work();
                        }
                    }
                };
            }
        }
    }

    public void LocalFunctionStartsAgain()
    {
        if (Check())
        {
            void Local()
            {
                if (Check())
                {
                    foreach (var x in Items())
                    {
                        Work();
                    }
                }
            }
        }
    }

    public void TwoSpotsAtTheSameDepth()
    {
        if (Check())
        {
            if (Check())
            {
                if (Check())
                {
                    if (Check())
                    {
                        Work();
                    }
                }
            }
        }

        if (Check())
        {
            if (Check())
            {
                if (Check())
                {
                    if (Check())
                    {
                        Work();
                    }
                }
            }
        }
    }

    public int Property
    {
        get
        {
            if (Check())
            {
                if (Check())
                {
                    if (Check())
                    {
                        if (Check())
                        {
                            return 1;
                        }
                    }
                }
            }

            return 0;
        }
    }

    private static bool Check() => true;

    private static System.Collections.Generic.IEnumerable<int> Items() => new int[0];

    private static void Work()
    {
    }
}
