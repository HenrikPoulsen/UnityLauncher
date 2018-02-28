using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using UnityLauncher.Core;

namespace UnityLauncher.Editor
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
        private static List<FailedTest> Failures;
        private static readonly TestSummary Summary = new TestSummary();

        public static RunResult Parse(string resultFileName)
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
                RunLogger.LogResultError($"Failed to parse {resultFileName}:\n{ex.Message}");
                return RunResult.FailedToStart;
            }

            var result = RunResult.Success;
            if (Failures.Count > 0)
            {
                RunLogger.LogError("Test failures found:");

                for (int i = 0; i < Failures.Count; i++)
                {
                    RunLogger.LogError($"  {(i + 1)}: {Failures[i].Name}\n  {Failures[i].Message}\n  {Failures[i].StackTrace}\n\n");
                }

                result = RunResult.Failure;
            }
            
            RunLogger.LogResultInfo("Test results:");
            RunLogger.LogResultInfo($"  Total: {Summary.Total}");
            RunLogger.LogResultInfo($"  Passed: {Summary.Passed}");
            RunLogger.LogResultInfo($"  Failed: {Summary.Failed}");
            RunLogger.LogResultInfo($"  Skipped: {Summary.Skipped}");
            RunLogger.LogResultInfo($"  Inconclusive: {Summary.Inconclusive}");

            if (Summary.Total == 0)
            {
                RunLogger.LogResultInfo("No tests were executed");
            }

            var actualCount = Summary.Passed +
                              Summary.Failed +
                              Summary.Skipped +
                              Summary.Inconclusive;

            if (Summary.Total != actualCount)
            {
                RunLogger.LogResultError($"Test result sums don't match. Total was reported as {Summary.Total} but the numbers add up to {actualCount}");
                result = RunResult.Failure;
            }

            return result;
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