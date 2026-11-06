using System;

var total = 0;
foreach (var item in new[] { 1, 2, 3 })
{
    if (item > 1)
    {
        while (total < 10)
        {
            if (item > 2)
            {
                total += item;
            }
        }
    }
}

Console.WriteLine(total);

void Local()
{
}
