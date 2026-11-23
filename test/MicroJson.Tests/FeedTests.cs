#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace MicroJson.Tests
{
    [TestFixture]
    public class FeedTests
    {
        static readonly int[] ChunkSizes = [1, 2, 3, 7, 64];

        static string Write(object? value)
        {
            var writer = new JsonWriter();
            writer.WriteValue(value);
            return writer.ToString();
        }

        /// <summary>
        /// Feeds the text in chunks and returns each value written back as JSON (or "error: ..." for a value that fails to parse).
        /// Fails if the feed stops making progress.
        /// </summary>
        static List<string> Feed(string json, int chunkSize, JsonFeedMode mode)
        {
            var feed = new JsonFeed(mode) { PreserveInexactNumbers = true };
            var values = new List<string>();
            int guard = json.Length + 10;

            void Drain()
            {
                while (feed.TryGetValue(out var reader))
                {
                    if (--guard < 0) Assert.Fail("the feed returns values without consuming input");

                    try
                    {
                        var value = reader.ReadValue();
                        reader.ReadEndOfInput();
                        values.Add(Write(value));
                    }
                    catch (ParserException ex)
                    {
                        values.Add("error: " + ex.Message);
                    }
                }
            }

            for (int i = 0; i < json.Length; i += chunkSize)
            {
                feed.Append(json.AsSpan(i, Math.Min(chunkSize, json.Length - i)));
                Drain();
            }

            feed.Complete();
            Drain();

            return values;
        }

        static IEnumerable<TestCaseData> AcceptFiles() =>
            Directory.EnumerateFiles(ConformanceTests.TestDirectory, "y_*.json")
                .Select(Path.GetFileName)
                .Order(StringComparer.Ordinal)
                .Select(f => new TestCaseData(f).SetName("Feed_" + f));

        static IEnumerable<TestCaseData> AllFiles() =>
            Directory.EnumerateFiles(ConformanceTests.TestDirectory, "*.json")
                .Select(Path.GetFileName)
                .Order(StringComparer.Ordinal)
                .Select(f => new TestCaseData(f).SetName("FeedRobustness_" + f));

        [TestCaseSource(nameof(AcceptFiles))]
        public void TestValuesMode(string file)
        {
            var json = ConformanceTests.Decode(file)!;
            var expected = Write(new JsonParser { PreserveInexactNumbers = true }.Parse(json));

            foreach (var size in ChunkSizes)
                Assert.That(Feed(json, size, JsonFeedMode.Values), Is.EqualTo(new[] { expected }), $"chunk size {size}");
        }

        [TestCaseSource(nameof(AcceptFiles))]
        public void TestArrayElementsMode(string file)
        {
            var json = ConformanceTests.Decode(file)!;
            if (new JsonParser { PreserveInexactNumbers = true }.Parse(json) is not IList<object?> array)
                return; // not an array at the top level

            var expected = array.Select(Write).ToList();

            foreach (var size in ChunkSizes)
                Assert.That(Feed(json, size, JsonFeedMode.ArrayElements), Is.EqualTo(expected), $"chunk size {size}");
        }

        [TestCaseSource(nameof(AllFiles))]
        public void TestRobustness(string file)
        {
            var json = ConformanceTests.Decode(file);
            if (json == null) return;

            foreach (var mode in new[] { JsonFeedMode.Values, JsonFeedMode.ArrayElements })
            {
                foreach (var size in new[] { 1, 5 })
                {
                    try
                    {
                        Feed(json, size, mode);
                    }
                    catch (ParserException)
                    {
                        // malformed around a value boundary
                    }
                }
            }
        }

        [Test]
        public void TestNewlineDelimited()
        {
            var json = "{\"X\":1,\"Y\":2}\n{\"X\":3,\"Y\":4}\r\n\n{\"X\":5,\"Y\":6}\n";
            var points = new List<Point>();
            var feed = new JsonFeed();

            foreach (var chunk in json.Chunk(4))
            {
                feed.Append(chunk);
                while (feed.TryGetValue(out var reader))
                    points.Add(TestJson.Deserialize<Point>(reader));
            }

            feed.Complete();
            Assert.That(feed.TryGetValue(out _), Is.False);
            Assert.That(points, Is.EqualTo(new[] { new Point(1, 2), new Point(3, 4), new Point(5, 6) }));
        }

        [Test]
        public void TestConcatenatedValues()
        {
            Assert.That(Feed("{\"a\":1}{\"b\":[2]}[3]\"x\\\"]\"4 true null -5", 3, JsonFeedMode.Values),
                Is.EqualTo(new[] { "{\"a\":1}", "{\"b\":[2]}", "[3]", "\"x\\\"]\"", "4", "true", "null", "-5" }));

            // numbers and literals end at whitespace or a structural character, so these are single (invalid) values
            Assert.That(Feed("null-5", 2, JsonFeedMode.Values).Single(), Does.StartWith("error:"));

            // a number at the end is only complete once the input is complete
            var feed = new JsonFeed();
            feed.Append("12");
            Assert.That(feed.TryGetValue(out _), Is.False);
            feed.Append("34");
            Assert.That(feed.TryGetValue(out _), Is.False);
            feed.Complete();
            Assert.That(feed.TryGetValue(out var reader), Is.True);
            Assert.That(reader!.ReadNumber<int>(), Is.EqualTo(1234));
            Assert.That(feed.TryGetValue(out _), Is.False);

            Assert.That(Feed("  \n ", 1, JsonFeedMode.Values), Is.Empty);
        }

        [Test]
        public void TestArrayElements()
        {
            Assert.That(Feed(" [ 1 , true,\"x\" ,null,[],{},[[\"]\"]], {\"a\":\"}\"} ] ", 2, JsonFeedMode.ArrayElements),
                Is.EqualTo(new[] { "1", "true", "\"x\"", "null", "[]", "{}", "[[\"]\"]]", "{\"a\":\"}\"}" }));
            Assert.That(Feed("[]", 1, JsonFeedMode.ArrayElements), Is.Empty);
            Assert.That(Feed(" [\n]\n", 1, JsonFeedMode.ArrayElements), Is.Empty);

            // typed elements, one at a time
            var json = TestJson.Serialize(Enumerable.Range(0, 100).Select(i => new Point(i, -i)).ToList());
            var feed = new JsonFeed(JsonFeedMode.ArrayElements);
            var points = new List<Point>();

            foreach (var chunk in json.Chunk(10))
            {
                feed.Append(chunk);
                while (feed.TryGetValue(out var reader))
                    points.Add(TestJson.Deserialize<Point>(reader));
            }

            feed.Complete();
            Assert.That(feed.TryGetValue(out _), Is.False);
            Assert.That(points, Has.Count.EqualTo(100));
            Assert.That(points[99], Is.EqualTo(new Point(99, -99)));
        }

        [Test]
        public void TestArrayErrors()
        {
            static string Error(string json)
            {
                var ex = Assert.Throws<ParserException>(() => Feed(json, 1, JsonFeedMode.ArrayElements))!;
                return ex.Message;
            }

            Assert.That(Error("{\"a\":1}"), Does.Contain("expected '['"));
            Assert.That(Error("[1,]"), Does.Contain("expected a value"));
            Assert.That(Error("[,1]"), Does.Contain("expected a value"));
            Assert.That(Error("[1 2]"), Does.Contain("expected ',' or ']'"));
            Assert.That(Error("[1] 2"), Does.Contain("extra characters at end"));
            Assert.That(Error("[1"), Does.Contain("unexpected end of input"));
            Assert.That(Error("[1,"), Does.Contain("unexpected end of input"));
            Assert.That(Error("[{\"a\":1"), Does.Contain("unexpected end of input"));
            Assert.That(Error(""), Does.Contain("unexpected end of input"));

            // malformed elements are reported by the reader
            Assert.That(Feed("[1, tru, [}]", 1, JsonFeedMode.ArrayElements), Has.Exactly(2).StartsWith("error:"));
            Assert.That(Feed("}", 1, JsonFeedMode.Values).Single(), Does.StartWith("error:"));
        }

        [Test]
        public void TestOptionsAndUsage()
        {
            var feed = new JsonFeed { MaxDepth = 2 };
            feed.Append("[[1]] [[[1]]]");
            feed.Complete();
            Assert.That(feed.TryGetValue(out var reader), Is.True);
            Assert.DoesNotThrow(() => reader!.ReadValue());
            Assert.That(feed.TryGetValue(out reader), Is.True);
            Assert.Throws<ParserException>(() => reader!.ReadValue());

            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new JsonFeed { MaxDepth = 0 });
            Assert.Throws<InvalidOperationException>(() => feed.Append("1"));
            Assert.That(new JsonFeed().MaxDepth, Is.EqualTo(64));
        }

        [Test]
        public void TestBoundedMemory()
        {
            // a large array streamed element by element only ever buffers about one element
            var feed = new JsonFeed(JsonFeedMode.ArrayElements);
            var element = "{\"name\":\"" + new string('x', 200) + "\",\"values\":[1,2,3]}";
            int count = 0;

            feed.Append("[");
            for (int i = 0; i < 50_000; i++)
            {
                feed.Append(i == 0 ? element : "," + element);
                while (feed.TryGetValue(out var reader))
                {
                    reader.Skip();
                    count++;
                }
            }
            feed.Append("]");
            feed.Complete();
            while (feed.TryGetValue(out var reader)) { reader.Skip(); count++; }

            var buffer = (char[])typeof(JsonFeed).GetField("buffer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(feed)!;
            Assert.That(count, Is.EqualTo(50_000));
            Assert.That(buffer.Length, Is.LessThanOrEqualTo(1024), "the input was about 11 MB, the buffer must stay small");
        }
    }
}
