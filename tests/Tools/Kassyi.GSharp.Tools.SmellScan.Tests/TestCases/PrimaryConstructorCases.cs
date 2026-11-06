namespace Cases;

public class PrimaryClass(int a, int b, int c, int d)
{
}

public struct PrimaryStruct(int a, int b, int c, int d)
{
}

public record PrimaryRecord(int A, int B, int C, int D);

public record struct PrimaryRecordStruct(int A, int B, int C, int D);

public class SmallPrimary(int a)
{
}
