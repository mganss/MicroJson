#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace MicroJson
{
    /// <summary>
    /// Runs the parsing test cases from https://github.com/nst/JSONTestSuite (see JSONTestSuite/README.md).
    /// </summary>
    [TestFixture]
    public class ConformanceTests
    {
        static readonly string TestDirectory = Path.Combine(AppContext.BaseDirectory, "JSONTestSuite", "test_parsing");

        static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        static IEnumerable<TestCaseData> Cases(string prefix) =>
            Directory.EnumerateFiles(TestDirectory, prefix + "*.json")
                .Select(Path.GetFileName)
                .Order(StringComparer.Ordinal)
                .Select(f => new TestCaseData(f).SetName(f));

        static IEnumerable<TestCaseData> AcceptCases() => Cases("y_");
        static IEnumerable<TestCaseData> RejectCases() => Cases("n_");
        static IEnumerable<TestCaseData> ImplementationDefinedCases() => Cases("i_");

        /// <summary>
        /// Decodes a test file. Returns null if the file is not valid UTF-8, which means the input is rejected
        /// before it reaches the parser (the parser operates on strings).
        /// </summary>
        static string? Decode(string file)
        {
            var bytes = File.ReadAllBytes(Path.Combine(TestDirectory, file));

            try
            {
                return StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return null;
            }
        }

        [Test]
        public void TestSuiteIsPresent()
        {
            Assert.That(AcceptCases().Count(), Is.EqualTo(95));
            Assert.That(RejectCases().Count(), Is.EqualTo(188));
            Assert.That(ImplementationDefinedCases().Count(), Is.EqualTo(35));
        }

        [TestCaseSource(nameof(AcceptCases))]
        public void Accepts(string file)
        {
            var json = Decode(file);
            Assert.That(json, Is.Not.Null, "file is not valid UTF-8");
            Assert.DoesNotThrow(() => new JsonParser().Parse(json!));
        }

        [TestCaseSource(nameof(RejectCases))]
        public void Rejects(string file)
        {
            var json = Decode(file);
            if (json == null) return; // rejected at decoding
            Assert.Throws<ParserException>(() => new JsonParser().Parse(json));
        }

        [TestCaseSource(nameof(ImplementationDefinedCases))]
        public void DoesNotCrash(string file)
        {
            var json = Decode(file);
            if (json == null) return;

            try
            {
                new JsonParser().Parse(json);
            }
            catch (ParserException)
            {
                // rejecting is allowed
            }
        }
    }
}
