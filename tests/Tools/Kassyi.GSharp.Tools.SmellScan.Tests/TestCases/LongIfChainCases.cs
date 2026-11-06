namespace Cases;

public class LongIfChainCases
{
    public int FourBranches(int x)
    {
        if (x == 1)
        {
            return 1;
        }
        else if (x == 2)
        {
            return 2;
        }
        else if (x == 3)
        {
            return 3;
        }
        else
        {
            return 0;
        }
    }

    public int TwoBranches(int x)
    {
        if (x == 1) { return 1; } else { return 2; }
    }

    public int NoFinalElse(int x)
    {
        if (x == 1) return 1;
        else if (x == 2) return 2;
        else if (x == 3) return 3;
        return 0;
    }

    public int Seven(int x)
    {
        if (x == 1) return 1;
        else if (x == 2) return 2;
        else if (x == 3) return 3;
        else if (x == 4) return 4;
        else if (x == 5) return 5;
        else if (x == 6) return 6;
        else return 7;
    }

    public int IfInsideElseBlock(int x)
    {
        if (x == 1)
        {
            return 1;
        }
        else
        {
            if (x == 2) { return 2; }
            else if (x == 3) { return 3; }
            else if (x == 4) { return 4; }
            else { return 0; }
        }
    }

    public int Sequential(int x)
    {
        if (x == 1) return 1;
        if (x == 2) return 2;
        if (x == 3) return 3;
        return 0;
    }
}
