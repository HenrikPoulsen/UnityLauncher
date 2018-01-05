using System;
using System.Collections.Generic;

namespace UnityLogWrapper
{
    public static class RunLogger
    {
        public static List<string> result { get; set; } = new List<string>();
        public static void LogError(string error)
        {
            Console.WriteLine($"[e] {error}");
        }

        public static void LogWarning(string warning)
        {
            Console.WriteLine($"[w] {warning}");
        }

        public static void LogInfo(string info)
        {
            Console.WriteLine($"[i] {info}");
        }

        public static void LogResultInfo(string info)
        {
            result.Add(info);
        }

        public static void LogResultError(string error)
        {
            result.Add($"[Error] {error}");
        }

        public static void Dump()
        {
            Console.WriteLine("Run summary:");
            foreach (var entry in result)
            {
                Console.WriteLine(entry);
            }
        }
    }
}