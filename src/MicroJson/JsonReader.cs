using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace MicroJson;

/// <summary>
/// The type of the next JSON token.
/// </summary>
public enum JsonTokenType
{
    /// <summary>The start of an object (<c>{</c>).</summary>
    StartObject,
    /// <summary>The end of an object (<c>}</c>).</summary>
    EndObject,
    /// <summary>The start of an array (<c>[</c>).</summary>
    StartArray,
    /// <summary>The end of an array (<c>]</c>).</summary>
    EndArray,
    /// <summary>A string.</summary>
    String,
    /// <summary>A number.</summary>
    Number,
    /// <summary>The literal <c>true</c>.</summary>
    True,
    /// <summary>The literal <c>false</c>.</summary>
    False,
    /// <summary>The literal <c>null</c>.</summary>
    Null,
}

/// <summary>
/// A forward-only reader over JSON text that follows RFC 8259 strictly.
/// </summary>
/// <remarks>
/// Objects are read with <see cref="ReadStartObject"/> followed by calls to <see cref="TryReadPropertyName"/>
/// until it returns <c>false</c>. After each property name, read (or <see cref="Skip"/>) exactly one value.
/// Arrays are read with <see cref="ReadStartArray"/> followed by calls to <see cref="TryReadNextItem"/>
/// until it returns <c>false</c>, reading exactly one value after each call that returns <c>true</c>.
/// All errors are reported as <see cref="ParserException"/>.
/// The input is either a string or a <see cref="TextReader"/>, which is read in chunks so that the whole input does not have to be
/// in memory.
/// </remarks>
public sealed class JsonReader
{
    const NumberStyles IntegerStyles = NumberStyles.AllowLeadingSign | NumberStyles.AllowExponent;
    const NumberStyles FloatStyles = IntegerStyles | NumberStyles.AllowDecimalPoint;
    const string UseDecimalSwitch = "MicroJson.UseDecimal";

    /// <summary>
    /// Gets a value indicating whether <see cref="ReadValue"/> returns <see cref="decimal"/> for numbers that are not integers.
    /// If <c>false</c>, they are returned as <see cref="double"/> instead.
    /// </summary>
    /// <remarks>
    /// Set through the <c>MicroJson.UseDecimal</c> runtime configuration switch (default <c>true</c>). When the switch is set to <c>false</c>
    /// in the project file with <c>Trim="true"</c>, the trimmer and the Native AOT compiler remove the decimal code, which makes the binary
    /// about 70 KB smaller: a boxed <see cref="decimal"/> pulls in enum formatting and reflection code.
    /// </remarks>
    [FeatureSwitchDefinition(UseDecimalSwitch)]
    internal static bool UseDecimal => !AppContext.TryGetSwitch(UseDecimalSwitch, out var enabled) || enabled;

    const int DefaultBufferSize = 4096;

    // The input is read into a fixed-size buffer in chunks, from a TextReader or (by copying) from a string, so that there is only
    // one code path to access it. buffer[0..end) holds the available characters, pos is the position of the next one. Characters
    // are consumed as they are read and the buffer is only refilled when all of them have been consumed. Strings that continue past
    // the end of the buffer are collected in a StringBuilder, numbers (which need their complete text) in numberBuffer.
    // string (or character segment) input; only used when source is null
    readonly ReadOnlyMemory<char> text;
    int textPosition;
    readonly TextReader? source;
    readonly char[] buffer;
    int end;
    bool sourceEnded;

    // Numbers are scanned from pos to scanIndex without being consumed, so that errors refer to their start. If a number continues
    // past the end of the buffer, the part in the buffer is moved to numberBuffer before refilling.
    char[]? numberBuffer;
    int numberLength;
    int scanIndex;
    readonly int maxDepth = 64;
    int pos;
    int line = 1;
    int col = 1;
    int depth;
    // true directly after the start of an object or array, i.e. no comma is expected before the next item
    bool afterStart;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonReader"/> class.
    /// </summary>
    /// <param name="json">The JSON text to read.</param>
    public JsonReader(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        text = json.AsMemory();
        buffer = new char[Math.Clamp(json.Length, 1, DefaultBufferSize)];
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonReader"/> class that reads a segment of characters (used by <see cref="JsonFeed"/>).
    /// </summary>
    internal JsonReader(ReadOnlyMemory<char> json)
    {
        text = json;
        buffer = new char[Math.Clamp(json.Length, 1, DefaultBufferSize)];
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonReader"/> class that reads from a <see cref="TextReader"/>.
    /// </summary>
    /// <param name="reader">The reader to read the JSON text from. It is not disposed.</param>
    public JsonReader(TextReader reader) : this(reader, DefaultBufferSize)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonReader"/> class that reads from a <see cref="TextReader"/>.
    /// </summary>
    /// <param name="reader">The reader to read the JSON text from. It is not disposed.</param>
    /// <param name="bufferSize">The number of characters to read at a time. Values (e.g. long strings) may be longer than the buffer.</param>
    public JsonReader(TextReader reader, int bufferSize)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentOutOfRangeException.ThrowIfLessThan(bufferSize, 1);
        source = reader;
        buffer = new char[bufferSize];
    }

    /// <summary>
    /// Gets the maximum nesting depth of arrays and objects. Input that is nested deeper causes a <see cref="ParserException"/>.
    /// The default is 64.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public int MaxDepth
    {
        get => maxDepth;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            maxDepth = value;
        }
    }

    /// <summary>
    /// Gets a value indicating whether to track line and column numbers for error messages.
    /// </summary>
    public bool CollectLineInfo { get; init; }

    /// <summary>
    /// Gets the logger that receives a trace of the values read by <see cref="ReadValue"/>.
    /// </summary>
    public ILogger? Logger { get; init; }

    /// <summary>
    /// Gets a value indicating whether <see cref="ReadValue"/> returns a <see cref="JsonNumber"/> with the original text
    /// for numbers that would otherwise lose digits (be rounded or become 0) or be rejected as out of range.
    /// The default is <c>false</c>.
    /// </summary>
    public bool PreserveInexactNumbers { get; init; }

    /// <summary>
    /// Returns the type of the next token without consuming it.
    /// </summary>
    public JsonTokenType Peek()
    {
        SkipWhitespace();

        if (!HasChar())
            throw CreateException("unexpected end of input");

        var c = buffer[pos];

        return c switch
        {
            '{' => JsonTokenType.StartObject,
            '}' => JsonTokenType.EndObject,
            '[' => JsonTokenType.StartArray,
            ']' => JsonTokenType.EndArray,
            '"' => JsonTokenType.String,
            't' => JsonTokenType.True,
            'f' => JsonTokenType.False,
            'n' => JsonTokenType.Null,
            _ when c == '-' || char.IsAsciiDigit(c) => JsonTokenType.Number,
            _ => throw CreateException("unknown token"),
        };
    }

    /// <summary>
    /// Reads the start of an object (<c>{</c>).
    /// </summary>
    public void ReadStartObject()
    {
        SkipWhitespace();
        Expect('{');
        EnterContainer();
    }

    /// <summary>
    /// Reads the name of the next property of the current object.
    /// </summary>
    /// <param name="name">The property name.</param>
    /// <returns><c>true</c> if a property name was read; <c>false</c> if the end of the object was reached (and consumed).</returns>
    public bool TryReadPropertyName([NotNullWhen(true)] out string? name)
    {
        if (!TryReadNext('}'))
        {
            name = null;
            return false;
        }

        SkipWhitespace();
        Expect('"');
        name = ReadStringContent();
        SkipWhitespace();
        Expect(':');

        return true;
    }

    /// <summary>
    /// Reads the start of an array (<c>[</c>).
    /// </summary>
    public void ReadStartArray()
    {
        SkipWhitespace();
        Expect('[');
        EnterContainer();
    }

    /// <summary>
    /// Advances to the next item of the current array.
    /// </summary>
    /// <returns><c>true</c> if there is another item to read; <c>false</c> if the end of the array was reached (and consumed).</returns>
    public bool TryReadNextItem() => TryReadNext(']');

    /// <summary>
    /// Reads a string.
    /// </summary>
    public string ReadString()
    {
        SkipWhitespace();
        Expect('"');
        return ReadStringContent();
    }

    /// <summary>
    /// Reads <c>true</c> or <c>false</c>.
    /// </summary>
    public bool ReadBoolean()
    {
        SkipWhitespace();

        if (IsNext('t'))
        {
            ExpectLiteral("true", "expected boolean");
            Log("bool: true");
            return true;
        }

        if (IsNext('f'))
        {
            ExpectLiteral("false", "expected boolean");
            Log("bool: false");
            return false;
        }

        throw CreateException("expected boolean");
    }

    /// <summary>
    /// Reads <c>null</c> if it is the next token.
    /// </summary>
    /// <returns><c>true</c> if <c>null</c> was read; otherwise, <c>false</c> (nothing is consumed).</returns>
    public bool TryReadNull()
    {
        SkipWhitespace();

        if (!IsNext('n'))
            return false;

        // only null starts with n
        ExpectLiteral("null", "unknown token");
        Log("null");

        return true;
    }

    /// <summary>
    /// Reads a number.
    /// </summary>
    /// <typeparam name="T">The numeric type to read, e.g. <see cref="int"/> or <see cref="double"/>.</typeparam>
    /// <exception cref="ParserException">The input is not a number or the number cannot be represented as <typeparamref name="T"/>.</exception>
    public T ReadNumber<T>() where T : INumberBase<T>
    {
        var num = ScanNumber(out _);

        if (!T.TryParse(num, FloatStyles, CultureInfo.InvariantCulture, out var value) || !T.IsFinite(value))
            throw CreateException(IsIntegerType<T>() && !IsIntegral(num) ? "expected an integer" : "number out of range");

        ConsumeNumber(num.Length);

        return value;
    }

    // typeof comparisons are constant for each instantiation, so this compiles to true or false without reflection
    private static bool IsIntegerType<T>() =>
        typeof(T) == typeof(int) || typeof(T) == typeof(long) || typeof(T) == typeof(short) || typeof(T) == typeof(sbyte)
        || typeof(T) == typeof(uint) || typeof(T) == typeof(ulong) || typeof(T) == typeof(ushort) || typeof(T) == typeof(byte);

    // The following methods work on the digits of a number so that no floating-point parsing code is needed.

    /// <summary>
    /// Analyzes a valid JSON number: its value is ± (significant digits) × 10^<paramref name="exponent"/>,
    /// where the significant digits have no leading or trailing zeros.
    /// </summary>
    /// <param name="num">The number.</param>
    /// <param name="digits">Receives the first significant digits (as many as fit).</param>
    /// <param name="exponent">The power of ten of the last significant digit.</param>
    /// <returns>The number of significant digits, 0 if the number is zero.</returns>
    private static int AnalyzeDigits(ReadOnlySpan<char> num, Span<char> digits, out long exponent)
    {
        int i = num[0] == '-' ? 1 : 0;
        int position = 0; // position of the current digit within the integer and fraction digits
        int integerLength = 0;
        int first = -1; // positions of the first and last non-zero digit
        int last = -1;
        int stored = 0;
        bool inFraction = false;

        for (; i < num.Length; i++)
        {
            var c = num[i];

            if (c == '.')
            {
                inFraction = true;
                continue;
            }

            if (!char.IsAsciiDigit(c))
                break;

            if (!inFraction)
                integerLength++;

            if (c != '0')
            {
                if (first < 0) first = position;
                last = position;
            }

            if (first >= 0 && stored < digits.Length)
                digits[stored++] = c;

            position++;
        }

        long e = 0;

        if (i < num.Length)
        {
            // e or E, optional sign, digits (saturated, the exact value of a huge exponent does not matter)
            i++;
            bool negative = num[i] == '-';
            if (num[i] == '-' || num[i] == '+') i++;
            for (; i < num.Length; i++)
                e = Math.Min(e * 10 + (num[i] - '0'), 1_000_000);
            if (negative) e = -e;
        }

        if (first < 0)
        {
            exponent = 0;
            return 0;
        }

        exponent = integerLength + e - last - 1;

        return last - first + 1;
    }

    /// <summary>
    /// Determines whether a number is a whole number, e.g. <c>1.5</c> and <c>15e-2</c> are not, <c>1.0</c> and <c>1.50e1</c> are.
    /// </summary>
    private static bool IsIntegral(ReadOnlySpan<char> num) =>
        AnalyzeDigits(num, default, out var exponent) == 0 || exponent >= 0;

    // 2^96 - 1, the largest mantissa of a decimal
    const string MaxDecimalMantissa = "79228162514264337593543950335";

    /// <summary>
    /// Determines whether a number can be represented exactly as a <see cref="decimal"/>, i.e. as m × 10^-s
    /// with m &lt; 2^96 and 0 &lt;= s &lt;= 28.
    /// </summary>
    private static bool FitsDecimal(ReadOnlySpan<char> num)
    {
        Span<char> digits = stackalloc char[MaxDecimalMantissa.Length];
        var count = AnalyzeDigits(num, digits, out var exponent);

        if (count == 0)
            return true;

        // for whole numbers, trailing zeros are part of the mantissa
        long mantissaLength = exponent >= 0 ? count + exponent : count;

        if (exponent < -28 || mantissaLength > MaxDecimalMantissa.Length)
            return false;

        if (mantissaLength < MaxDecimalMantissa.Length)
            return true;

        for (int k = 0; k < MaxDecimalMantissa.Length; k++)
        {
            var c = k < count ? digits[k] : '0';
            if (c != MaxDecimalMantissa[k])
                return c < MaxDecimalMantissa[k];
        }

        return true;
    }

    // the smallest positive double that is not subnormal
    const double MinNormalDouble = 2.2250738585072014E-308;

    /// <summary>
    /// Determines whether a number survives the conversion to <see cref="double"/> and back without losing digits:
    /// this is guaranteed for up to 15 significant digits unless the value is subnormal.
    /// </summary>
    private static bool FitsDouble(ReadOnlySpan<char> num, double value) =>
        AnalyzeDigits(num, default, out _) is var count && (count == 0 || (count <= 15 && Math.Abs(value) >= MinNormalDouble));

    /// <summary>
    /// Reads a string containing a date and time in ISO 8601 format.
    /// </summary>
    public DateTime ReadDateTime()
    {
        if (!DateTime.TryParse(ReadString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value))
            throw CreateException("invalid date");
        return value;
    }

    /// <summary>
    /// Reads a string containing a date and time with offset in ISO 8601 format.
    /// </summary>
    public DateTimeOffset ReadDateTimeOffset()
    {
        if (!DateTimeOffset.TryParse(ReadString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value))
            throw CreateException("invalid date");
        return value;
    }

    /// <summary>
    /// Reads a string and parses it using the invariant culture, e.g. a <see cref="Guid"/> or <see cref="TimeSpan"/>.
    /// </summary>
    /// <typeparam name="T">The type to parse.</typeparam>
    public T ReadParsable<T>() where T : ISpanParsable<T>
    {
        if (!T.TryParse(ReadString(), CultureInfo.InvariantCulture, out var value))
            throw CreateException("invalid value");
        return value;
    }

    /// <summary>
    /// Reads any value into generic CLR objects: objects become <see cref="IDictionary{TKey, TValue}"/> of <see cref="string"/> and <see cref="object"/>,
    /// arrays become <see cref="IList{T}"/> of <see cref="object"/>, integers become <see cref="int"/>, <see cref="long"/>, or <see cref="ulong"/>
    /// if they fit, other numbers become <see cref="decimal"/> (see <see cref="UseDecimal"/>) or <see cref="double"/>,
    /// or <see cref="JsonNumber"/> (see <see cref="PreserveInexactNumbers"/>).
    /// </summary>
    public object? ReadValue()
    {
        switch (Peek())
        {
            case JsonTokenType.String:
                return ReadString();
            case JsonTokenType.Number:
                return ReadDomNumber();
            case JsonTokenType.True:
            case JsonTokenType.False:
                return ReadBoolean();
            case JsonTokenType.Null:
                // reads null or throws
                TryReadNull();
                return null;
            case JsonTokenType.StartArray:
                return ReadDomList();
            case JsonTokenType.StartObject:
                return ReadDomDictionary();
            default:
                throw CreateException("unexpected token");
        }
    }

    /// <summary>
    /// Skips the next value (including nested arrays and objects), validating its syntax.
    /// </summary>
    public void Skip()
    {
        switch (Peek())
        {
            case JsonTokenType.String:
                ReadString();
                break;
            case JsonTokenType.Number:
                ConsumeNumber(ScanNumber(out _).Length);
                break;
            case JsonTokenType.True:
            case JsonTokenType.False:
                ReadBoolean();
                break;
            case JsonTokenType.Null:
                TryReadNull();
                break;
            case JsonTokenType.StartArray:
                ReadStartArray();
                while (TryReadNextItem()) Skip();
                break;
            case JsonTokenType.StartObject:
                ReadStartObject();
                while (TryReadPropertyName(out _)) Skip();
                break;
            default:
                throw CreateException("unexpected token");
        }
    }

    /// <summary>
    /// Verifies that only whitespace follows.
    /// </summary>
    public void ReadEndOfInput()
    {
        SkipWhitespace();

        if (HasChar())
            throw CreateException("extra characters at end");
    }

    /// <summary>
    /// Creates a <see cref="ParserException"/> for the current position.
    /// </summary>
    /// <param name="message">The error message.</param>
    public ParserException CreateException(string message)
    {
        if (CollectLineInfo)
        {
            return new ParserException("Parse error: " + message + " at line " + line.ToString(CultureInfo.InvariantCulture)
                + ", column " + col.ToString(CultureInfo.InvariantCulture) + ".", line, col);
        }

        return new ParserException("Parse error: " + message + ".", 0, 0);
    }

    private bool TryReadNext(char end)
    {
        SkipWhitespace();

        if (IsNext(end))
        {
            Advance(1);
            depth--;
            afterStart = false;
            return false;
        }

        if (!afterStart)
            Expect(',');

        afterStart = false;

        return true;
    }

    private void EnterContainer()
    {
        if (++depth > maxDepth)
            throw CreateException("maximum depth exceeded");

        // Guards against a stack overflow (which cannot be caught) when MaxDepth is set very high.
        if (!RuntimeHelpers.TryEnsureSufficientExecutionStack())
            throw CreateException("insufficient stack to continue parsing");

        afterStart = true;
    }

    private List<object?> ReadDomList()
    {
        ReadStartArray();
        Log("list: [");

        var list = new List<object?>();

        while (TryReadNextItem())
            list.Add(ReadValue());

        Log("]");

        return list;
    }

    private Dictionary<string, object?> ReadDomDictionary()
    {
        ReadStartObject();
        Log("Dictionary: {");

        var dict = new Dictionary<string, object?>();

        while (TryReadPropertyName(out var name))
            dict[name] = ReadValue();

        Log("}");

        return dict;
    }

    private object ReadDomNumber()
    {
        var num = ScanNumber(out var isInteger);
        object result;

        // Numbers without a fraction become int, long, or ulong if they fit (an exponent is allowed, e.g. 1e3),
        // everything else becomes decimal (unless disabled through the feature switch) or, if out of decimal's range, double.
        if (isInteger && int.TryParse(num, IntegerStyles, CultureInfo.InvariantCulture, out var i))
        {
            Log("int: {0}", i);
            result = i;
        }
        else if (isInteger && long.TryParse(num, IntegerStyles, CultureInfo.InvariantCulture, out var l))
        {
            Log("long: {0}", l);
            result = l;
        }
        else if (isInteger && ulong.TryParse(num, IntegerStyles, CultureInfo.InvariantCulture, out var ul))
        {
            // positive integers between long.MaxValue and ulong.MaxValue, exact even if decimal is turned off
            Log("ulong: {0}", ul);
            result = ul;
        }
        else if (UseDecimal && (!PreserveInexactNumbers || FitsDecimal(num)) && decimal.TryParse(num, FloatStyles, CultureInfo.InvariantCulture, out var d))
        {
            Log("decimal: {0}", d);
            result = d;
        }
        else if (double.TryParse(num, FloatStyles, CultureInfo.InvariantCulture, out var dbl) && double.IsFinite(dbl)
            && (!PreserveInexactNumbers || FitsDouble(num, dbl)))
        {
            Log("double: {0}", dbl);
            result = dbl;
        }
        else if (PreserveInexactNumbers)
        {
            var numberText = num.ToString();
            Log("number: {0}", numberText);
            result = new JsonNumber(numberText, validated: true);
        }
        else
        {
            throw CreateException("number out of range");
        }

        ConsumeNumber(num.Length);

        return result;
    }

    /// <summary>
    /// Validates the number at the current position (after whitespace) without consuming it; call <see cref="ConsumeNumber"/> afterwards.
    /// The returned span is valid until the reader is used again.
    /// </summary>
    private ReadOnlySpan<char> ScanNumber(out bool isInteger)
    {
        SkipWhitespace();

        // fast path: the number ends within the buffer (if it reaches the end of the buffer, it may continue in the next chunk)
        var available = new ReadOnlySpan<char>(buffer, 0, end);
        int k = pos;
        isInteger = true;

        if (k < available.Length && available[k] == '-') k++;

        // RFC 8259: int = zero / ( digit1-9 *DIGIT )
        if (k < available.Length && available[k] == '0')
        {
            k++;
            if (k < available.Length && char.IsAsciiDigit(available[k]))
                throw CreateException("leading zero in number");
        }
        else if (!ScanDigits(available, ref k))
        {
            goto Slow;
        }

        if (k < available.Length && available[k] == '.')
        {
            k++;
            isInteger = false;
            if (!ScanDigits(available, ref k)) goto Slow;
        }

        if (k < available.Length && available[k] is 'e' or 'E')
        {
            k++;
            if (k < available.Length && available[k] is '+' or '-') k++;
            if (!ScanDigits(available, ref k)) goto Slow;
        }

        if (k < available.Length)
        {
            numberLength = 0;
            scanIndex = k;
            return available[pos..k];
        }

    Slow:
        return ScanNumberAcrossChunks(out isInteger);
    }

    /// <summary>
    /// Scans digits for the fast path of <see cref="ScanNumber"/>.
    /// </summary>
    /// <returns><c>false</c> if the end of the buffer was reached, i.e. the slow path is needed.</returns>
    private bool ScanDigits(ReadOnlySpan<char> available, ref int k)
    {
        int start = k;
        while (k < available.Length && char.IsAsciiDigit(available[k])) k++;

        if (k == available.Length)
            return false;

        if (k == start)
            throw CreateException("not a number");

        return true;
    }

    /// <summary>
    /// Scans a number that may continue past the end of the buffer.
    /// </summary>
    private ReadOnlySpan<char> ScanNumberAcrossChunks(out bool isInteger)
    {
        numberLength = 0;
        scanIndex = pos;
        isInteger = true;

        if (PeekNumberChar() == '-') scanIndex++;

        // RFC 8259: int = zero / ( digit1-9 *DIGIT )
        if (PeekNumberChar() == '0')
        {
            scanIndex++;
            if (IsAsciiDigit(PeekNumberChar()))
                throw CreateException("leading zero in number");
        }
        else
        {
            TakeDigits();
        }

        if (PeekNumberChar() == '.')
        {
            scanIndex++;
            isInteger = false;
            TakeDigits();
        }

        if (PeekNumberChar() is 'e' or 'E')
        {
            scanIndex++;
            if (PeekNumberChar() is '+' or '-') scanIndex++;
            TakeDigits();
        }

        // anything after the number (e.g. the second dot in 1.2.3) is left for the caller to reject

        // this path is only taken for numbers that reached the end of the buffer, so part of the number is in numberBuffer
        AppendNumberChars();
        return new ReadOnlySpan<char>(numberBuffer, 0, numberLength);
    }

    /// <summary>
    /// Returns the character at <see cref="scanIndex"/>, or -1 at the end of the input.
    /// </summary>
    private int PeekNumberChar()
    {
        if (scanIndex < end)
            return buffer[scanIndex];

        // the number continues past the end of the buffer (or the input ends): move the part in the buffer out, then refill
        AppendNumberChars();
        pos = end; // consumed without updating the line info, which ConsumeNumber does

        if (!Refill())
        {
            scanIndex = pos;
            return -1;
        }

        scanIndex = 0;
        return buffer[0];
    }

    private void TakeDigits()
    {
        if (!IsAsciiDigit(PeekNumberChar()))
            throw CreateException("not a number");

        do
        {
            var available = new ReadOnlySpan<char>(buffer, 0, end);
            int i = scanIndex + 1;
            while ((uint)i < (uint)available.Length && char.IsAsciiDigit(available[i])) i++;
            scanIndex = i;
        }
        while (IsAsciiDigit(PeekNumberChar()));
    }

    private void AppendNumberChars()
    {
        int count = scanIndex - pos;

        if (count == 0)
            return;

        if (numberBuffer == null || numberLength + count > numberBuffer.Length)
        {
            var larger = new char[Math.Max(Math.Max(64, numberLength + count), (numberBuffer?.Length ?? 0) * 2)];
            numberBuffer?.AsSpan(0, numberLength).CopyTo(larger);
            numberBuffer = larger;
        }

        Array.Copy(buffer, pos, numberBuffer, numberLength, count);
        numberLength += count;
    }

    private void ConsumeNumber(int length)
    {
        pos = scanIndex;

        // numbers contain no line breaks
        if (CollectLineInfo)
            col += length;
    }

    private static bool IsAsciiDigit(int c) => (uint)(c - '0') <= 9;

    /// <summary>
    /// Reads the rest of a string after the opening quote.
    /// </summary>
    private string ReadStringContent()
    {
        StringBuilder? sb = null;

        while (true)
        {
            // scan the characters in the buffer up to a quote, backslash, or control character
            // (looping over a span of the available characters lets the compiler drop the bounds checks)
            var chars = buffer;
            var available = new ReadOnlySpan<char>(chars, 0, end);
            int i = pos;

            while ((uint)i < (uint)available.Length)
            {
                var ch = available[i];
                if (ch == '"' || ch == '\\' || ch < 0x20) break;
                i++;
            }

            if (i < end && chars[i] == '"')
            {
                string s;

                if (sb == null)
                {
                    // fast path: the whole string is in the buffer and has no escape sequences
                    s = new string(chars, pos, i - pos);
                }
                else
                {
                    sb.Append(chars, pos, i - pos);
                    s = sb.ToString();
                }

                Advance(i - pos + 1);
                Log("string: {0}", s);
                return s;
            }

            // the string continues in the next chunk or has an escape sequence: collect it in a StringBuilder
            sb ??= new StringBuilder();
            sb.Append(chars, pos, i - pos);
            Advance(i - pos);

            if (i == end)
            {
                if (!Refill())
                    throw CreateException("unterminated string");
                continue;
            }

            if (chars[i] < 0x20)
                throw CreateException("control character in string");

            // escape sequence
            Advance(1);
            var c = ReadEscapeChar("unterminated escape sequence string");

            switch (c)
            {
                case '"':
                case '/':
                case '\\':
                    sb.Append(c);
                    break;
                case 'b':
                    sb.Append('\b');
                    break;
                case 'f':
                    sb.Append('\f');
                    break;
                case 'n':
                    sb.Append('\n');
                    break;
                case 'r':
                    sb.Append('\r');
                    break;
                case 't':
                    sb.Append('\t');
                    break;
                case 'u':
                    int u = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        var h = HexValue(ReadEscapeChar("unterminated unicode escape in string"));
                        if (h < 0)
                            throw CreateException("not a well-formed unicode escape sequence in string");
                        u = (u << 4) | h;
                    }
                    sb.Append((char)u);
                    break;
                default:
                    throw CreateException("unknown escape sequence in string");
            }
        }
    }

    private char ReadEscapeChar(string error)
    {
        if (!HasChar())
            throw CreateException(error);

        var c = buffer[pos];
        Advance(1);

        return c;
    }

    /// <summary>
    /// Returns the value of a hex digit, or -1 if the character is not a hex digit.
    /// </summary>
    private static int HexValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1,
    };

    private void ExpectLiteral(string literal, string error)
    {
        if (end - pos >= literal.Length && new ReadOnlySpan<char>(buffer, pos, literal.Length).SequenceEqual(literal))
        {
            Advance(literal.Length);
            return;
        }

        for (int i = 0; i < literal.Length; i++)
        {
            if (!HasChar() || buffer[pos] != literal[i])
                throw CreateException(error);

            Advance(1);
        }
    }

    // The per-token checks below (Expect, IsNext, SkipWhitespace, Advance) run several times per token. Each has a tiny fast path
    // that is inlined into its callers, and a separate slow path (refilling, line info, errors) that is not, so that the callers
    // don't pay for the slow path's code (e.g. saving registers for the refill call) when it isn't needed.

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Expect(char c)
    {
        if (pos < end && buffer[pos] == c && !CollectLineInfo)
        {
            pos++;
            return;
        }

        ExpectSlow(c);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ExpectSlow(char c)
    {
        if (!HasChar() || buffer[pos] != c)
            throw CreateException("expected '" + c + "'");

        Advance(1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Advance(int n)
    {
        if (CollectLineInfo)
            CountLines(n);

        pos += n;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void CountLines(int n)
    {
        foreach (var c in new ReadOnlySpan<char>(buffer, pos, n))
        {
            if (c == '\n')
            {
                line++;
                col = 1;
            }
            else
            {
                col++;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SkipWhitespace()
    {
        // all JSON whitespace is <= ' ', so most tokens in compact JSON need no further work
        if (pos < end && buffer[pos] > ' ')
            return;

        SkipWhitespaceSlow();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void SkipWhitespaceSlow()
    {
        while (true)
        {
            var available = new ReadOnlySpan<char>(buffer, 0, end);
            int i = pos;

            while ((uint)i < (uint)available.Length)
            {
                var c = available[i];
                if (c != ' ' && c != '\t' && c != '\r' && c != '\n') break;
                i++;
            }

            if (i != pos)
                Advance(i - pos);

            // stop at a non-whitespace character or at the end of the input
            if (i < end || !Refill())
                return;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsNext(char c) => pos < end ? buffer[pos] == c : Refill() && buffer[pos] == c;

    /// <summary>
    /// Returns <c>true</c> if there is a character at <see cref="pos"/>, refilling the buffer if necessary.
    /// </summary>
    private bool HasChar() => pos < end || Refill();

    /// <summary>
    /// Reads the next chunk into the buffer. Only called when all characters in the buffer have been consumed.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool Refill()
    {
        if (sourceEnded)
            return false;

        int n;

        if (source == null)
        {
            n = Math.Min(buffer.Length, text.Length - textPosition);
            text.Span.Slice(textPosition, n).CopyTo(buffer);
            textPosition += n;
        }
        else
        {
            n = source.Read(buffer, 0, buffer.Length);
        }

        pos = 0;
        end = Math.Max(n, 0);

        if (end == 0)
        {
            sourceEnded = true;
            return false;
        }

        return true;
    }

    private void Log(string message)
    {
        Logger?.WriteLine(message);
    }

    private void Log<T>(string message, T arg)
    {
        // check first to avoid boxing and the params array when no logger is set
        if (Logger != null)
            Logger.WriteLine(message, arg!);
    }
}
