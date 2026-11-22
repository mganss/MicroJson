using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
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
/// </remarks>
public sealed class JsonReader
{
    const NumberStyles IntegerStyles = NumberStyles.AllowLeadingSign | NumberStyles.AllowExponent;
    const NumberStyles FloatStyles = IntegerStyles | NumberStyles.AllowDecimalPoint;

    readonly string input;
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
        input = json;
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
    /// Returns the type of the next token without consuming it.
    /// </summary>
    public JsonTokenType Peek()
    {
        SkipWhitespace();

        if (pos >= input.Length)
            throw CreateException("unexpected end of input");

        var c = input[pos];

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

        if (Accept("true"))
        {
            Log("bool: true");
            return true;
        }

        if (Accept("false"))
        {
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

        if (Accept("null"))
        {
            Log("null");
            return true;
        }

        return false;
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
            throw CreateException("number out of range");

        Advance(num.Length);

        return value;
    }

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
    /// arrays become <see cref="IList{T}"/> of <see cref="object"/>, numbers become <see cref="int"/>, <see cref="long"/>, <see cref="decimal"/>,
    /// or <see cref="double"/>.
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
                if (TryReadNull()) return null;
                throw CreateException("unknown token");
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
                Advance(ScanNumber(out _).Length);
                break;
            case JsonTokenType.True:
            case JsonTokenType.False:
                ReadBoolean();
                break;
            case JsonTokenType.Null:
                if (!TryReadNull()) throw CreateException("unknown token");
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

        if (pos != input.Length)
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

        // Numbers without a fraction become int or long if they fit (an exponent is allowed, e.g. 1e3),
        // everything else becomes decimal or, if out of decimal's range, double.
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
        else if (decimal.TryParse(num, FloatStyles, CultureInfo.InvariantCulture, out var d))
        {
            Log("decimal: {0}", d);
            result = d;
        }
        else if (double.TryParse(num, FloatStyles, CultureInfo.InvariantCulture, out var dbl) && double.IsFinite(dbl))
        {
            Log("double: {0}", dbl);
            result = dbl;
        }
        else
        {
            throw CreateException("number out of range");
        }

        Advance(num.Length);

        return result;
    }

    /// <summary>
    /// Validates the number at the current position (after whitespace) without consuming it.
    /// </summary>
    private ReadOnlySpan<char> ScanNumber(out bool isInteger)
    {
        SkipWhitespace();

        int currentPos = pos;
        isInteger = true;

        if (IsAt(currentPos, '-')) currentPos++;

        // RFC 8259: int = zero / ( digit1-9 *DIGIT )
        if (IsAt(currentPos, '0'))
        {
            currentPos++;
            if (currentPos < input.Length && char.IsAsciiDigit(input[currentPos]))
                throw CreateException("leading zero in number");
        }
        else
        {
            ExpectDigits(ref currentPos);
        }

        if (IsAt(currentPos, '.'))
        {
            currentPos++;
            isInteger = false;
            ExpectDigits(ref currentPos);
        }

        if (IsAt(currentPos, 'e') || IsAt(currentPos, 'E'))
        {
            currentPos++;
            if (IsAt(currentPos, '+') || IsAt(currentPos, '-')) currentPos++;
            ExpectDigits(ref currentPos);
        }

        return input.AsSpan(pos, currentPos - pos);
    }

    private void ExpectDigits(ref int p)
    {
        int start = p;
        while (p < input.Length && char.IsAsciiDigit(input[p])) p++;
        if (start == p) throw CreateException("not a number");
    }

    /// <summary>
    /// Reads the rest of a string after the opening quote.
    /// </summary>
    private string ReadStringContent()
    {
        int currentPos = pos;

        // fast path: no escape sequences
        while (currentPos < input.Length)
        {
            var c = input[currentPos];

            if (c == '"')
            {
                var s = input.Substring(pos, currentPos - pos);
                Advance(currentPos - pos + 1);
                Log("string: {0}", s);
                return s;
            }

            if (c == '\\') break;

            if (c < 0x20)
                throw CreateException("control character in string");

            currentPos++;
        }

        var sb = new StringBuilder();
        sb.Append(input, pos, currentPos - pos);

        while (true)
        {
            if (currentPos >= input.Length)
                throw CreateException("unterminated string");

            var c = input[currentPos];

            if (c == '"')
            {
                Advance(currentPos - pos + 1);
                var s = sb.ToString();
                Log("string: {0}", s);
                return s;
            }
            else if (c == '\\')
            {
                currentPos++;

                if (currentPos >= input.Length)
                    throw CreateException("unterminated escape sequence string");

                c = input[currentPos];

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
                        currentPos += 4;
                        if (currentPos >= input.Length)
                            throw CreateException("unterminated unicode escape in string");
                        var h1 = HexValue(input[currentPos - 3]);
                        var h2 = HexValue(input[currentPos - 2]);
                        var h3 = HexValue(input[currentPos - 1]);
                        var h4 = HexValue(input[currentPos]);
                        if ((h1 | h2 | h3 | h4) < 0)
                            throw CreateException("not a well-formed unicode escape sequence in string");
                        sb.Append((char)((h1 << 12) | (h2 << 8) | (h3 << 4) | h4));
                        break;
                    default:
                        throw CreateException("unknown escape sequence in string");
                }
            }
            else if (c < 0x20)
            {
                throw CreateException("control character in string");
            }
            else
            {
                sb.Append(c);
            }

            currentPos++;
        }
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

    private bool Accept(string s)
    {
        if (input.AsSpan(pos).StartsWith(s, StringComparison.Ordinal))
        {
            Advance(s.Length);
            return true;
        }

        return false;
    }

    private void Expect(char c)
    {
        if (pos >= input.Length || input[pos] != c)
            throw CreateException("expected '" + c + "'");

        Advance(1);
    }

    private void Advance(int n)
    {
        if (CollectLineInfo)
        {
            for (int i = pos; i < pos + n; i++)
            {
                if (input[i] == '\n')
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

        pos += n;
    }

    private void SkipWhitespace()
    {
        int n = pos;

        while (n < input.Length)
        {
            var c = input[n];
            if (c != ' ' && c != '\t' && c != '\r' && c != '\n') break;
            n++;
        }

        if (n != pos)
            Advance(n - pos);
    }

    private bool IsNext(char c) => pos < input.Length && input[pos] == c;

    private bool IsAt(int p, char c) => p < input.Length && input[p] == c;

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
