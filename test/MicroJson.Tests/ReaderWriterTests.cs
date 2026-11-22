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

        static string NumberError<T>(string json) where T : System.Numerics.INumberBase<T> =>
            Assert.Throws<ParserException>(() => new JsonReader(json).ReadNumber<T>())!.Message;

        [Test]
        public void TestNumberErrors()
        {
            // fractions read into integer types
            Assert.That(NumberError<int>("1.5"), Does.Contain("expected an integer"));
            Assert.That(NumberError<int>("15e-1"), Does.Contain("expected an integer"));
            Assert.That(NumberError<int>("-0.001"), Does.Contain("expected an integer"));
            Assert.That(NumberError<int>("1e-400"), Does.Contain("expected an integer"));
            Assert.That(NumberError<byte>("0.5"), Does.Contain("expected an integer"));
            Assert.That(NumberError<ulong>("1.25e1"), Does.Contain("expected an integer"));

            // whole numbers that do not fit
            Assert.That(NumberError<int>("2147483648"), Does.Contain("out of range"));
            Assert.That(NumberError<int>("2147483648.0"), Does.Contain("out of range"));
            Assert.That(NumberError<int>("1.5e10"), Does.Contain("out of range"));
            Assert.That(NumberError<int>("1e99999999999999999999"), Does.Contain("out of range"));
            Assert.That(NumberError<ulong>("-1"), Does.Contain("out of range"));
            Assert.That(NumberError<byte>("256"), Does.Contain("out of range"));

            // non-integer types always report the range
            Assert.That(NumberError<decimal>("1.5e29"), Does.Contain("out of range"));
            Assert.That(NumberError<double>("1.5e309"), Does.Contain("out of range"));

            // whole numbers written with a fraction or exponent are accepted
            Assert.That(new JsonReader("1.50e1").ReadNumber<int>(), Is.EqualTo(15));
            Assert.That(new JsonReader("150e-1").ReadNumber<int>(), Is.EqualTo(15));
            Assert.That(new JsonReader("-0e5").ReadNumber<int>(), Is.EqualTo(0));
            Assert.That(new JsonReader("2.0").ReadNumber<ulong>(), Is.EqualTo(2UL));
        }

        [Test]
        public void TestUnsignedIntegers()
        {
            Assert.That(new JsonParser().Parse("9223372036854775807"), Is.TypeOf<long>());
            Assert.That(new JsonParser().Parse("9223372036854775808"), Is.TypeOf<ulong>().And.EqualTo(9223372036854775808UL));
            Assert.That(new JsonParser().Parse("18446744073709551615"), Is.TypeOf<ulong>().And.EqualTo(ulong.MaxValue));
            Assert.That(new JsonParser().Parse("18446744073709551616"), Is.TypeOf<decimal>());
            Assert.That(new JsonParser().Parse("-9223372036854775809"), Is.TypeOf<decimal>());

            var w = new JsonWriter();
            w.WriteValue(new JsonParser().Parse("[18446744073709551615]"));
            Assert.That(w.ToString(), Is.EqualTo("[18446744073709551615]"));
        }

        static object? Preserve(string json, bool useDecimal = true)
        {
            try
            {
                AppContext.SetSwitch("MicroJson.UseDecimal", useDecimal);
                return new JsonParser { PreserveInexactNumbers = true }.Parse(json);
            }
            finally
            {
                AppContext.SetSwitch("MicroJson.UseDecimal", true);
            }
        }

        [Test]
        public void TestPreserveInexactNumbers()
        {
            // out of range
            Assert.That(Preserve("1e400"), Is.EqualTo(new JsonNumber("1e400")));
            Assert.That(Preserve("-1e400"), Is.EqualTo(new JsonNumber("-1e400")));

            // would become 0
            Assert.That(Preserve("1e-400"), Is.EqualTo(new JsonNumber("1e-400")));
            Assert.That(Preserve("1e-28"), Is.TypeOf<decimal>().And.EqualTo(1e-28m));
            // too small for decimal, but exact as double
            Assert.That(Preserve("1e-29"), Is.TypeOf<double>().And.EqualTo(1e-29));
            Assert.That(Preserve("1.2345678901234567e-29"), Is.EqualTo(new JsonNumber("1.2345678901234567e-29")));
            Assert.That(Preserve("1.50e-27"), Is.TypeOf<decimal>().And.EqualTo(1.5e-27m));

            // would be rounded
            Assert.That(Preserve("0.12345678901234567890123456789012"), Is.EqualTo(new JsonNumber("0.12345678901234567890123456789012")));
            Assert.That(Preserve("1.00000000000000000000000000001"), Is.TypeOf<JsonNumber>());
            Assert.That(Preserve("123123123123123123123123123123"), Is.TypeOf<JsonNumber>());
            Assert.That(Preserve("79228162514264337593543950336"), Is.TypeOf<JsonNumber>());
            Assert.That(Preserve("7.9228162514264337593543950336"), Is.TypeOf<JsonNumber>());

            // exact as decimal, including decimal's limits
            Assert.That(Preserve("79228162514264337593543950335"), Is.TypeOf<decimal>().And.EqualTo(decimal.MaxValue));
            Assert.That(Preserve("-79228162514264337593543950335"), Is.TypeOf<decimal>().And.EqualTo(decimal.MinValue));
            Assert.That(Preserve("7.9228162514264337593543950335"), Is.TypeOf<decimal>());
            Assert.That(Preserve("0.1"), Is.TypeOf<decimal>());
            Assert.That(Preserve("-0.0"), Is.TypeOf<decimal>());
            Assert.That(Preserve("99999999999999999999"), Is.TypeOf<decimal>());

            // too large for decimal, but exact as double
            Assert.That(Preserve("1e30"), Is.TypeOf<double>().And.EqualTo(1e30));

            // integers are unaffected
            Assert.That(Preserve("42"), Is.TypeOf<int>());
            Assert.That(Preserve("18446744073709551615"), Is.TypeOf<ulong>());

            // without the option, nothing changes
            Assert.Throws<ParserException>(() => new JsonParser().Parse("1e400"));
            Assert.That(new JsonParser().Parse("1e-400"), Is.TypeOf<decimal>());
        }

        [Test]
        public void TestPreserveInexactNumbersWithDouble()
        {
            Assert.That(Preserve("0.1", useDecimal: false), Is.TypeOf<double>().And.EqualTo(0.1));
            Assert.That(Preserve("0.123456789012345", useDecimal: false), Is.TypeOf<double>());
            Assert.That(Preserve("0.1234567890123456", useDecimal: false), Is.EqualTo(new JsonNumber("0.1234567890123456")));
            Assert.That(Preserve("123456789012345e5", useDecimal: false), Is.TypeOf<ulong>());
            Assert.That(Preserve("123456789012345e10", useDecimal: false), Is.TypeOf<double>().And.EqualTo(123456789012345e10));
            Assert.That(Preserve("99999999999999999999", useDecimal: false), Is.TypeOf<JsonNumber>());
            Assert.That(Preserve("1e20", useDecimal: false), Is.TypeOf<double>());
            Assert.That(Preserve("1e-300", useDecimal: false), Is.TypeOf<double>());
            Assert.That(Preserve("1e-320", useDecimal: false), Is.TypeOf<JsonNumber>()); // subnormal
            Assert.That(Preserve("1e-400", useDecimal: false), Is.TypeOf<JsonNumber>());
            Assert.That(Preserve("1e400", useDecimal: false), Is.TypeOf<JsonNumber>());
            Assert.That(Preserve("0.0", useDecimal: false), Is.TypeOf<double>());
        }

        [Test]
        public void TestJsonNumberRoundTrip()
        {
            var json = "[1e400,0.1,123456789012345678901234567890,-1e-400]";
            var w = new JsonWriter();
            w.WriteValue(new JsonParser { PreserveInexactNumbers = true }.Parse(json));
            Assert.That(w.ToString(), Is.EqualTo(json));

            // through an object member of a generated serializer
            var a = TestJson.Deserialize<AllTypes>(new JsonReader("""{"Dom":[1e400]}""") { PreserveInexactNumbers = true });
            Assert.That(TestJson.Serialize(a), Does.Contain("\"Dom\":[1e400]"));
        }

        [Test]
        public void TestJsonNumber()
        {
            var n = new JsonNumber("1e400");
            Assert.That(n.Text, Is.EqualTo("1e400"));
            Assert.That(n.ToString(), Is.EqualTo("1e400"));
            Assert.That(n.TryGetDouble(out _), Is.False);
            Assert.That(n.TryGetDecimal(out _), Is.False);

            var big = new JsonNumber("-123456789012345678901234567890.5");
            Assert.That(big.TryGetDouble(out var d), Is.True);
            Assert.That(d, Is.EqualTo(-1.2345678901234568E+29));
            Assert.That(big.TryGetDecimal(out _), Is.False);
            Assert.That(new JsonNumber("0.12345678901234567890123456789012").TryGetDecimal(out var m), Is.True);
            Assert.That(m, Is.EqualTo(0.1234567890123456789012345679m));

            Assert.That(n, Is.EqualTo(new JsonNumber("1e400")));
            Assert.That(n, Is.Not.EqualTo(new JsonNumber("1E400")));
            Assert.That(n.GetHashCode(), Is.EqualTo(new JsonNumber("1e400").GetHashCode()));
            Assert.That(n.Equals((object?)null), Is.False);
            Assert.That(n.Equals("1e400"), Is.False);

            foreach (var invalid in new[] { "", " 1", "1 ", "01", "1.", ".5", "-", "1e", "+1", "abc", "[1]", "1,2", "NaN" })
                Assert.Throws<ArgumentException>(() => _ = new JsonNumber(invalid), invalid);
            Assert.Throws<ArgumentNullException>(() => _ = new JsonNumber(null!));
        }

        [Test]
        public void TestUseDecimalSwitch()
        {
            try
            {
                AppContext.SetSwitch("MicroJson.UseDecimal", false);

                Assert.That(new JsonParser().Parse("0.1"), Is.TypeOf<double>().And.EqualTo(0.1));
                Assert.That(new JsonParser().Parse("1e-3"), Is.TypeOf<double>().And.EqualTo(0.001));
                Assert.That(new JsonParser().Parse("99999999999999999999"), Is.TypeOf<double>().And.EqualTo(1e20));
                Assert.That(new JsonParser().Parse("42"), Is.TypeOf<int>());
                Assert.That(new JsonParser().Parse("2147483648"), Is.TypeOf<long>());
                Assert.That(new JsonParser().Parse("18446744073709551615"), Is.TypeOf<ulong>().And.EqualTo(ulong.MaxValue));
                Assert.That(new JsonParser().Parse("-9223372036854775809"), Is.TypeOf<double>());
                Assert.Throws<ParserException>(() => new JsonParser().Parse("1e400"));

                AppContext.SetSwitch("MicroJson.UseDecimal", true);

                Assert.That(new JsonParser().Parse("0.1"), Is.TypeOf<decimal>().And.EqualTo(0.1m));
            }
            finally
            {
                AppContext.SetSwitch("MicroJson.UseDecimal", true);
            }
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
