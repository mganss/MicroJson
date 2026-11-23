#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace MicroJson.Tests
{
    /// <summary>
    /// Tests reading from a <see cref="TextReader"/>. Small buffers put every token at every possible buffer boundary,
    /// and the results must be the same as when reading from a string.
    /// </summary>
    [TestFixture]
    public class StreamingTests
    {
        static readonly int[] BufferSizes = [1, 2, 3, 5, 8, 64];

        /// <summary>
        /// A reader that returns at most a few characters per call, like a slow network stream.
        /// </summary>
        sealed class TrickleReader(string text, int maxChars) : TextReader
        {
            int position;

            public override int Read(char[] buffer, int index, int count)
            {
                var n = Math.Min(Math.Min(count, maxChars), text.Length - position);
                text.CopyTo(position, buffer, index, n);
                position += n;
                return n;
            }
        }

        /// <summary>
        /// Parses and writes back the JSON, or returns the error message (with line info).
        /// </summary>
        static string Run(Func<JsonReader> createReader)
        {
            try
            {
                var reader = createReader();
                var value = reader.ReadValue();
                reader.ReadEndOfInput();
                var writer = new JsonWriter();
                writer.WriteValue(value);
                return writer.ToString();
            }
            catch (ParserException ex)
            {
                return "error: " + ex.Message;
            }
        }

        static void AssertSameAsString(string json, string context)
        {
            var expected = Run(() => new JsonReader(json) { CollectLineInfo = true, PreserveInexactNumbers = true });

            foreach (var size in BufferSizes)
            {
                var actual = Run(() => new JsonReader(new StringReader(json), size) { CollectLineInfo = true, PreserveInexactNumbers = true });
                Assert.That(actual, Is.EqualTo(expected), $"{context}, buffer size {size}");
            }

            var trickled = Run(() => new JsonReader(new TrickleReader(json, 1)) { CollectLineInfo = true, PreserveInexactNumbers = true });
            Assert.That(trickled, Is.EqualTo(expected), $"{context}, one character per read");
        }

        static IEnumerable<TestCaseData> ConformanceFiles() =>
            Directory.EnumerateFiles(ConformanceTests.TestDirectory, "*.json")
                .Select(Path.GetFileName)
                .Order(StringComparer.Ordinal)
                .Select(f => new TestCaseData(f).SetName("Streaming_" + f));

        [TestCaseSource(nameof(ConformanceFiles))]
        public void TestConformanceFile(string file)
        {
            var json = ConformanceTests.Decode(file);
            if (json == null) return; // not valid UTF-8

            AssertSameAsString(json, file);
        }

        [Test]
        public void TestDocuments()
        {
            string[] documents =
            [
                "{}",
                "  [ 1 , 2.5e-3 , -0 , true , false , null , \"a\\u00e9\\\"\\\\\\/\\b\\f\\n\\r\\t\" ]  ",
                "{\"a\":{\"b\":[[],{},[{}]]},\"c\":\"\"}\n",
                "\"" + new string('x', 10_000) + "\"",
                "\"" + string.Concat(Enumerable.Repeat("ab\\u0041\\n", 500)) + "\"",
                new string('9', 500),
                "123456789012345678901234567890.12345678901234567890e-5",
                "[1,\n2,\n\"x\ny\"]",
                "[1,2,]",
                "{\"a\" 1}",
                "[tru]",
                "[\"\\u12\"]",
                "1 2",
            ];

            foreach (var json in documents)
                AssertSameAsString(json, json.Length > 40 ? json[..40] + "..." : json);
        }

        [Test]
        public void TestLineInfoAcrossRefills()
        {
            var json = "[\n  1,\n  2,\n  x]";
            var ex = Assert.Throws<ParserException>(() => new JsonParser { CollectLineInfo = true }.Parse(new TrickleReader(json, 1)))!;
            Assert.That(ex.Line, Is.EqualTo(4));
            Assert.That(ex.Column, Is.EqualTo(3));
        }

        [Test]
        public void TestParserOverload()
        {
            var parser = new JsonParser { MaxDepth = 2, PreserveInexactNumbers = true };
            var value = (IList<object?>)parser.Parse(new StringReader("[1e400, [2]]"))!;
            Assert.That(value[0], Is.EqualTo(new JsonNumber("1e400")));
            Assert.Throws<ParserException>(() => parser.Parse(new StringReader("[[[1]]]")));
            Assert.Throws<ParserException>(() => parser.Parse(new StringReader("[1] x")));
        }

        [Test]
        public void TestGeneratedOverload()
        {
            var json = TestJson.Serialize(new Zoo
            {
                Animals = [new Dog { Name = "Rex" }, new Lion { Name = "Leo" }],
                Shape = new Circle(1),
            });

            var zoo = TestJson.Deserialize<Zoo>(new TrickleReader(json, 3));
            Assert.That(zoo.Animals.Select(a => a.GetType()), Is.EqualTo(new[] { typeof(Dog), typeof(Lion) }));
            Assert.That(TestJson.Serialize(zoo), Is.EqualTo(json));

            Assert.That(TestJson.Deserialize<Person>(new StringReader("""{"Name":"Ann","Age":3}""")).Age, Is.EqualTo(3));
            Assert.Throws<ParserException>(() => TestJson.Deserialize<Person>(new StringReader("{} {}")));
        }

        [Test]
        public void TestStreamReader()
        {
            var bytes = Encoding.UTF8.GetBytes("""{"Name":"Zoë 😀","Age":42}""");
            using var reader = new StreamReader(new MemoryStream(bytes));
            var person = TestJson.Deserialize<Person>(reader);
            Assert.That(person.Name, Is.EqualTo("Zoë 😀"));
            Assert.That(person.Age, Is.EqualTo(42));
        }
    }
}
