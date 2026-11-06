namespace Cases;

public class MethodLengthCases
{
    public void FourCodeLines()
    {
        Work();
    }

    public void BlankAndCommentLinesAreFree()
    {
        // a comment-only line is not counted

        Work(); // a trailing comment shares the line of its code

        /* a block comment
           over two lines */
        Work();
    }

    public int ExpressionBody() =>
        Work2(1,
            2);

    public string MultiLineVerbatimString()
    {
        return @"first
second
third";
    }

    public void Outer()
    {
        Inner();
        void Inner()
        {
            Work();
        }
        System.Action action = () =>
        {
            Work();
        };
    }

    public int Property
    {
        get { return 1; }
        set { Work(); }
    }

    public MethodLengthCases()
    {
        Work();
    }

    public static MethodLengthCases operator +(MethodLengthCases a, MethodLengthCases b)
    {
        return a;
    }

    private static void Work()
    {
    }

    private static int Work2(int a, int b) => a + b;
}

public interface IMethodLengthCases
{
    void NoBody(int a);
}

public abstract class AbstractMethodLengthCases
{
    public abstract void NoBody();
}
