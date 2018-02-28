using System;
using System.Collections.Generic;
using System.Drawing;

namespace UnityLogWrapper
{
    public static class RunLogger
    {
        public static List<Tuple<string, bool>> result { get; set; } = new List<Tuple<string,bool>>();
        public static void LogError(string error)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[e] {error}");
            Console.ResetColor();
        }

        public static void LogWarning(string warning)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[w] {warning}");
            Console.ResetColor();
        }

        public static void LogInfo(string info)
        {
            Console.WriteLine($"[i] {info}");
        }

        public static void LogResultInfo(string info)
        {
            result.Add(new Tuple<string, bool>(info, false));
        }

        public static void LogResultError(string error)
        {
            result.Add(new Tuple<string, bool>($"[Error] {error}", true));
        }

        public static void Dump()
        {
            Console.WriteLine("Run summary:");
            foreach (var entry in result)
            {
                if(entry.Item2)
                    Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(entry.Item1);
                Console.ResetColor();
            }
        }
    }
}