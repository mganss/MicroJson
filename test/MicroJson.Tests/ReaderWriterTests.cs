#nullable enable
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace MicroJson.Tests
{
    [TestFixture]
    public class ReaderWriterTests
    {
        [Test]
        public void TestPeek()
        {
            var r = new JsonReader(""" { } [ ] "s" -1 true false null x""");

            Assert.That(r.Peek(), Is.EqualTo(JsonTokenType.StartObject));
            r.ReadStartObject();
            Assert.That(r.Peek(), Is.EqualTo(JsonTokenType.EndObject));
            Assert.That(r.TryReadPropertyName(out _), Is.False);
            Assert.That(r.Peek(), Is.EqualTo(JsonTokenType.StartArray));
            r.ReadStartArray();
            Assert.That(r.Peek(), Is.EqualTo(JsonTokenType.EndArray));
            Assert.That(r.TryReadNextItem(), Is.False);
            Assert.That(r.Peek(), Is.EqualTo(JsonTokenType.String));
            r.Skip();
            Assert.That(r.Peek(), Is.EqualTo(JsonTokenType.Number));
            r.Skip();
            Assert.That(r.Peek(), Is.EqualTo(JsonTokenType.True));
            r.Skip();
            Assert.That(r.Peek(), Is.EqualTo(JsonTokenType.False));
            r.Skip();
            Assert.That(r.Peek(), Is.EqualTo(JsonTokenType.Null));
            r.Skip();
            Assert.Throws<ParserException>(() => r.Peek());
            Assert.Throws<ParserException>(() => new JsonReader("  ").Peek());
        }

        [Test]
        public void TestSkipErrors()
        {
            Assert.Throws<ParserException>(() => new JsonReader("nul").Skip());
            Assert.Throws<ParserException>(() => new JsonReader("]").Skip());
            Assert.Throws<ParserException>(() => new JsonReader("""{"a" 1}""").Skip());
            Assert.Throws<ParserException>(() => new JsonReader("-").Skip());
            Assert.Throws<ParserException>(() => new JsonReader("nope").ReadValue());
            Assert.Throws<ParserException>(() => new JsonReader("}").ReadValue());
            Assert.Throws<ParserException>(() => new JsonReader("tru").ReadBoolean());
        }

        [Test]
        public void TestReaderOptions()
        {
            Assert.That(new JsonReader("1").MaxDepth, Is.EqualTo(64));
            Assert.That(new JsonReader("1") { MaxDepth = 2 }.MaxDepth, Is.EqualTo(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new JsonReader("1") { MaxDepth = 0 });
            Assert.Throws<ArgumentNullException>(() => _ = new JsonReader(null!));

            var r = new JsonReader("[\n  1,\n  x]") { CollectLineInfo = true };
            r.ReadStartArray();
            r.TryReadNextItem();
            r.ReadNumber<int>();
            r.TryReadNextItem();
            var ex = Assert.Throws<ParserException>(() => r.ReadNumber<int>())!;
            Assert.That(ex.Line, Is.EqualTo(3));
            Assert.That(ex.Column, Is.EqualTo(3));
            Assert.That(ex.Message, Does.Contain("line 3, column 3"));
        }

        [Test]
        public void TestStringEscapes()
        {
            Assert.That(new JsonReader("\"a\\u0041\\\"b\\n\"").ReadString(), Is.EqualTo("aA\"b\n"));
            Assert.Throws<ParserException>(() => new JsonReader("\"a\\n\u0001\"").ReadString());
            // an invalid hex digit in each position
            Assert.Throws<ParserException>(() => new JsonReader("\"\\uG001\"").ReadString());
            Assert.Throws<ParserException>(() => new JsonReader("\"\\u0G01\"").ReadString());
            Assert.Throws<ParserException>(() => new JsonReader("\"\\u00G1\"").ReadString());
            Assert.Throws<ParserException>(() => new JsonReader("\"\\u000G\"").ReadString());
            Assert.Throws<ParserException>(() => new JsonParser().Parse("[\"\\u00g1\"]"));
            Assert.Throws<ParserException>(() => new JsonReader("\"a\\nb").ReadString());
        }

        [Test]
        public void TestWriterEscaping()
        {
            var w = new JsonWriter();
            w.WriteString("\"\\\b\f\n\r\t\u0000\u001f\u007f\u2028é😀/");
            Assert.That(w.ToString(), Is.EqualTo("\"\\\"\\\\\\b\\f\\n\\r\\t\\u0000\\u001f\u007f\u2028é😀/\""));

            var s = "\"\\\b\f\n\r\t\u0000\u001f\u007f\u2028é😀/";
            Assert.That(new JsonReader(w.ToString()).ReadString(), Is.EqualTo(s));

            w = new JsonWriter();
            w.WriteStartObject();
            w.WritePropertyName("a\"b");
            w.WriteNull();
            w.WritePropertyName("c");
            w.WriteBoolean(false);
            w.WriteEndObject();
            Assert.That(w.ToString(), Is.EqualTo("""{"a\"b":null,"c":false}"""));
            Assert.Throws<ArgumentNullException>(() => new JsonWriter().WritePropertyName(null!));
        }

        [Test]
        public void TestWriterNumbers()
        {
            var w = new JsonWriter();
            w.WriteStartArray();
            w.WriteNumber(-0.0);
            w.WriteNumber(1e22);
            w.WriteNumber(double.MaxValue);
            w.WriteNumber(1.5m);
            w.WriteNumber(float.Epsilon);
            w.WriteEndArray();
            Assert.That(w.ToString(), Is.EqualTo("[-0,1E+22,1.7976931348623157E+308,1.5,1E-45]"));

            // the output must be valid JSON that round-trips
            var values = (IList<object?>)new JsonParser().Parse(w.ToString())!;
            Assert.That(values[2], Is.EqualTo(double.MaxValue));

            Assert.Throws<ArgumentOutOfRangeException>(() => new JsonWriter().WriteNumber(double.NegativeInfinity));
        }

        [Test]
        public void TestWriteValue()
        {
            var dom = new Dictionary<string, object?>
            {
                ["s"] = "x",
                ["b"] = true,
                ["i"] = 1,
                ["l"] = 1L << 40,
                ["m"] = 1.5m,
                ["d"] = 2.5e100,
                ["n"] = null,
                ["a"] = new object?[] { 1, new List<object?> { "y" }, new Dictionary<string, object?>() },
                ["e"] = new List<string> { "covariant" },
            };

            var w = new JsonWriter();
            w.WriteValue(dom);
            var json = w.ToString();
            Assert.That(json, Is.EqualTo("""{"s":"x","b":true,"i":1,"l":1099511627776,"m":1.5,"d":2.5E+100,"n":null,"a":[1,["y"],{}],"e":["covariant"]}"""));

            // DOM round trip
            var parsed = new JsonParser().Parse(json);
            w = new JsonWriter();
            w.WriteValue(parsed);
            Assert.That(w.ToString(), Is.EqualTo(json));

            Assert.Throws<NotSupportedException>(() => new JsonWriter().WriteValue(new object()));
            Assert.Throws<NotSupportedException>(() => new JsonWriter().WriteValue(new Dictionary<string, int>()));
            Assert.Throws<NotSupportedException>(() => new JsonWriter().WriteValue('c'));
        }

        [Test]
        public void TestAttributes()
        {
            // the attributes are only read by the generator, but they must work when instantiated through reflection
            Assert.That(new MicroJsonSerializableAttribute(typeof(int)).Type, Is.EqualTo(typeof(int)));
            Assert.That(new MicroJsonPropertyNameAttribute("n").Name, Is.EqualTo("n"));
            Assert.That(new MicroJsonIgnoreAttribute(), Is.Not.Null);

            var options = new MicroJsonOptionsAttribute
            {
                PropertyNamingPolicy = MicroJsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = MicroJsonIgnoreCondition.WhenWritingNull,
                PropertyNameCaseInsensitive = true,
            };
            Assert.That(options.PropertyNamingPolicy, Is.EqualTo(MicroJsonNamingPolicy.CamelCase));
            Assert.That(options.DefaultIgnoreCondition, Is.EqualTo(MicroJsonIgnoreCondition.WhenWritingNull));
            Assert.That(options.PropertyNameCaseInsensitive, Is.True);
        }
    }
}
