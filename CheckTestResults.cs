using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.IO;
using System.Xml;

namespace UnityLogWrapper
{
    class CheckTestResults
    {
        public class FailedTest
        {
            public string Name;
            public string Message;
            public string StackTrace;
        }

        public class TestSummary
        {
            public int Total;
            public int Inconclusive;
            public int Passed;
            public int Failed;
            public int Skipped;
        }

        private static string resultFileName = "";
        public static List<FailedTest> Failures;
        public static TestSummary Summary = new TestSummary();

        public static UnityLauncher.RunResult Parse(string resultFileName)
        {
            Failures = new List<FailedTest>();


            CheckTestResults.resultFileName = resultFileName;

            try
            {
                var fstream = new FileStream(resultFileName,
                    FileMode.Open, FileAccess.Read);
                try
                {
                    XmlDocument xmlDoc = new XmlDocument();
                    xmlDoc.Load(fstream);
                    LoadTestResults(xmlDoc.DocumentElement);
                }
                finally
                {
                    fstream.Close();
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;

                Console.WriteLine($"Failed to parse {resultFileName}:");
                Console.WriteLine(ex.Message);
                return UnityLauncher.RunResult.FailedToStart;
            }


            if (Failures.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;

                //Print errors
                Console.WriteLine("Tests failed");
                Console.WriteLine("");

                for (int I = 0; I < Failures.Count; I++)
                {
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    Console.WriteLine("  " + (I + 1).ToString() +
                                      ": " + Failures[I].Name);
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.Write(Failures[I].Message);
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.Write(Failures[I].StackTrace);

                    Console.WriteLine("");
                    Console.WriteLine("");
                }

                //Stop and wait for user input
                return UnityLauncher.RunResult.Failure;
            }

            return UnityLauncher.RunResult.Success;
        }

        private static void LoadTestResults(XmlElement rootNode)
        {
            Summary.Total = Convert.ToInt32(rootNode.Attributes["total"].Value);
            Summary.Passed = Convert.ToInt32(rootNode.Attributes["passed"].Value);
            Summary.Failed = Convert.ToInt32(rootNode.Attributes["failed"].Value);
            Summary.Inconclusive = Convert.ToInt32(rootNode.Attributes["inconclusive"].Value);
            Summary.Skipped = Convert.ToInt32(rootNode.Attributes["skipped"].Value);
            foreach (XmlNode childNode in rootNode.ChildNodes)
            {
                if (childNode is XmlElement &&
                    childNode.LocalName == "test-suite")
                    LoadTestSuite((XmlElement) childNode);
            }    
        }

        private static void LoadTestSuite(XmlElement suiteNode)
        {
            foreach (XmlNode node in suiteNode.ChildNodes)
            {
                if (node is XmlElement)
                {
                    if (node.LocalName == "test-suite")
                    {
                        LoadTestSuite((XmlElement) node);
                    }
                    else if (node.LocalName == "test-case")
                    {
                        LoadTestCase((XmlElement) node);
                    }
                }
            }
        }

        private static void LoadTestCase(XmlElement caseNode)
        {
            foreach (XmlNode node in caseNode.ChildNodes)
            {
                if (node is XmlElement &&
                    node.LocalName == "failure")
                {
                    LoadTestFailure((XmlElement) node);
                }
            }
        }

        private static void LoadTestFailure(XmlElement failureNode)
        {
            FailedTest error = new FailedTest();

            error.Name =
                failureNode.ParentNode.Attributes["fullname"].Value;

            foreach (XmlNode node in failureNode.ChildNodes)
            {
                if (node.LocalName == "message")
                {
                    error.Message = node.FirstChild.Value;
                }
                else if (node.LocalName == "stack-trace")
                {
                    error.StackTrace = node.FirstChild.Value;
                }
            }

            Failures.Add(error);
        }
    }
}