using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AddTest
{
    class Program
    {
        static int Add(int cnt)
        {
            int sum = 0;
            for (int i = 0; i < cnt; i++)
            {
                sum += i;
            }
            return sum;
        }

        static void Main(string[] args)
        {
            int a = Add(5);
            Console.WriteLine(a);
            Console.ReadLine();
        }
    }
}
