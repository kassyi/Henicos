namespace Cases;

public enum Kind
{
    A,
    B,
    C,
    D,
}

public class LargeSwitchCases
{
    private int _total;

    public int TableStatement(int x)
    {
        switch (x)
        {
            case 1: return 10;
            case 2: return 20;
            case 3: return 30;
            case 4: return 40;
            case 5: return 50;
            case 6: return 60;
            case 7: return 70;
            default: return 0;
        }
    }

    public string TableExpression(int x) => x switch
    {
        1 => "one",
        2 => "two",
        3 => "three",
        4 => "four",
        5 => "five",
        6 => "six",
        7 => "seven",
        _ => "many",
    };

    public int TableWithOneStatementOnTwoLines(int x)
    {
        switch (x)
        {
            case 1:
                return 10;
            case 2:
                return
                    20;
            case 3: return 30;
            default: return 0;
        }
    }

    public int TableWithFallThroughLabelsOnTheirOwnLines(int x)
    {
        switch (x)
        {
            case 1:
            case 2:
            case 3:
                return 1;
            case 4:
                return 2;
            default:
                return 0;
        }
    }

    public int TableWithBreaksAndBlocks(int x)
    {
        switch (x)
        {
            case 1:
                _total = 1;
                break;
            case 2:
            {
                _total = 2;
                break;
            }
            case 3: { return 3; }
            case 4:
                if (x > 0) return 4;
                break;
            default:
                throw new System.InvalidOperationException();
        }

        return _total;
    }

    public int TableExpressionOnTwoLines(int x) => x switch
    {
        1 =>
            10,
        2 when x > 0 =>
            20,
        3 => 30,
        _ => 0,
    };

    public int TwoStatementsOnOneLine(int x, out int result)
    {
        switch (x)
        {
            case 1: result = 1; _total++; return 1;
            case 2: return 2;
            default: result = 0; return 0;
        }
    }

    public int StatementWithOneTallSection(int x)
    {
        switch (x)
        {
            case 1: return 10;
            case 2:
                _total = x; _total++;
                return 20;
            case 3: return 30;
            default: return 0;
        }
    }

    public int EnumSwitch(Kind kind)
    {
        switch (kind)
        {
            case Kind.A:
                _total++; _total++;
                return 1;
            case Kind.B:
                return 2;
            case Kind.C:
                return 3;
            case Kind.D:
                return 4;
            default:
                return 0;
        }
    }

    public int ExpressionWithABlockLambdaArm(int x) => x switch
    {
        1 => 10,
        2 => Apply(x, y => { return y * 2; }),
        3 => 30,
        4 => 40,
        _ => 0,
    };

    public int ExpressionWithANestedSwitchArm(int x, int y) => x switch
    {
        1 => 10,
        2 => y switch { 0 => 1, _ => 2 },
        3 => 30,
        _ => 0,
    };

    public int SharedLabels(int x)
    {
        switch (x)
        {
            case 1:
            case 2:
            case 3:
                return 1;
            case 4:
                x++; x++;
                return x;
            default:
                return 0;
        }
    }

    public int InsideLambda(int x)
    {
        var f = (int y) =>
        {
            switch (y)
            {
                case 1:
                    return 1;
                case 2:
                    y++; y++;
                    return y;
                default:
                    return 0;
            }
        };
        return f(x);
    }

    public int IfElseInASection(int x)
    {
        switch (x)
        {
            case 1: return 1;
            case 2:
                if (x > 0) return 2;
                else return 3;
            default: return 0;
        }
    }

    public int ExpressionWithADelegateArm(int x) => x switch
    {
        1 => 10,
        2 => Apply(x, delegate (int y) { return y; }),
        _ => 0,
    };

    public bool OneWorkAndReturnOnOneLine(object value, out int integer)
    {
        switch (value)
        {
            case int i: integer = i; return true;
            case long l: integer = (int)l; return true;
            case short s: integer = s; return true;
            case byte b: integer = b; return true;
            case sbyte sb: integer = sb; return true;
            case ushort us: integer = us; return true;
            default: integer = 0; return false;
        }
    }

    public void WorkAndBareReturn(int x)
    {
        switch (x)
        {
            case 1: _total += 1; return;
            case 2: _total += 2; return;
            case 3: _total += 3; return;
            case 4: _total += 4; return;
            case 5: _total += 5; return;
            case 6: _total += 6; return;
            default: _total = 0; return;
        }
    }

    public int LocalsAndIfs(int x)
    {
        switch (x)
        {
            case 1:
                var doubled = x * 2;
                if (doubled > 3) { _total += doubled; }
                break;
            case 2: _total++; break;
            case 3: _total--; break;
            default: break;
        }

        return _total;
    }

    public bool OptionParsing(string option, int i, int count, out string error)
    {
        error = string.Empty;
        switch (option)
        {
            case "--a":
                if (i + 1 >= count) { error = "needs a value"; return false; }
                _total = i;
                break;
            case "--b":
                _total = 0;
                break;
            default:
                break;
        }

        return true;
    }

    public int GotoCaseChain(int x)
    {
        switch (x)
        {
            case 1: _total = 1; _total++; goto case 2;
            case 2: return _total;
            default: return 0;
        }
    }

    public int TableEndingInEveryKindOfJump(int x)
    {
        for (var i = 0; i < x; i++)
        {
            switch (i)
            {
                case 1: _total++; continue;
                case 2: _total++; goto done;
                case 3: _total++; throw new System.InvalidOperationException();
                case 4: _total++; break;
                case 5: _total++; return _total;
                case 6: _total--; continue;
                default: continue;
            }
        }

    done:
        return _total;
    }

    public int BreakInTheMiddle(int x)
    {
        switch (x)
        {
            case 1:
                if (x > 0) break;
                _total++;
                break;
            case 2: _total++; break;
            default: break;
        }

        return _total;
    }

    private static int Apply(int value, System.Func<int, int> function) => function(value);
}
