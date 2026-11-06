namespace Cases;

public interface IShape
{
    void Draw(int a, int b, int c, int d, int e);
}

public abstract class ShapeBase
{
    public abstract void Resize(int a, int b, int c, int d, int e);

    public virtual void Move(int a, int b, int c, int d, int e)
    {
    }
}

public class ParameterCountCases : ShapeBase, IShape
{
    public ParameterCountCases(int a, int b, int c)
    {
    }

    public void Zero()
    {
    }

    public void Five(int a, int b, int c, int d, int e)
    {
    }

    public void Six(int a, int b, int c, int d, int e, int f)
    {
    }

    public int ExpressionBodied(int a, int b, int c, int d, int e, int f) => a;

    public override void Resize(int a, int b, int c, int d, int e)
    {
    }

    public override void Move(int a, int b, int c, int d, int e)
    {
    }

    void IShape.Draw(int a, int b, int c, int d, int e)
    {
    }

    public static int operator +(ParameterCountCases left, ParameterCountCases right) => 0;

    public void WithLocal()
    {
        int Local(int a, int b, int c, int d, int e, int f) => a;
    }

    public void Modifiers(ref int a, out int b, in int c, params int[] d)
    {
        b = 0;
    }
}

public static class ParameterCountExtensions
{
    public static void Extend(this string value, int a, int b, int c, int d, int e)
    {
    }
}

public record PositionalRecord(int A, int B, int C, int D, int E, int F, int G, int H);

public class PrimaryConstructorClass(int a, int b, int c, int d, int e, int f, int g, int h)
{
}
