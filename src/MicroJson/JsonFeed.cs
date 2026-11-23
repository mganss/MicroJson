using System;
using System.Diagnostics.CodeAnalysis;

namespace MicroJson;

/// <summary>
/// Specifies how <see cref="JsonFeed"/> splits its input into values.
/// </summary>
public enum JsonFeedMode
{
    /// <summary>
    /// The input is a sequence of JSON values, e.g. a single document, newline-delimited JSON, or concatenated values.
    /// Each value is returned when it is complete.
    /// </summary>
    Values,

    /// <summary>
    /// The input is a single JSON array. Each element is returned when it is complete, so only one element at a time has to be
    /// buffered, however large the array is.
    /// </summary>
    ArrayElements,
}

/// <summary>
/// Splits JSON text that arrives in chunks into complete values, so that each value can be read as soon as it is complete.
/// </summary>
/// <remarks>
/// <para>
/// The feed involves no asynchronous code: the caller reads the input in whatever way it likes (e.g. with <c>await ReadAsync</c>),
/// passes each chunk to <see cref="Append"/>, and calls <see cref="TryGetValue"/> to get the values that are complete so far.
/// Call <see cref="Complete"/> at the end of the input and then <see cref="TryGetValue"/> once more, because a number at the
/// end of the input is only known to be complete then.
/// </para>
/// <para>
/// The feed only finds the boundaries of values; the values themselves are validated when they are read through the returned
/// <see cref="JsonReader"/>. A reader is only valid until the next call to a method of the feed, so read the value first
/// (e.g. with a generated <c>Deserialize&lt;T&gt;(JsonReader)</c> method or <see cref="JsonReader.ReadValue"/>).
/// </para>
/// </remarks>
public sealed class JsonFeed
{
    enum ArrayState
    {
        Start,
        BeforeFirstElement,
        BeforeElement,
        AfterElement,
        End,
    }

    readonly JsonFeedMode mode;
    readonly int maxDepth = 64;
    char[] buffer = new char[1024];
    int start; // characters before start are no longer needed
    int length; // characters appended so far
    int scan; // the next character to scan
    bool completed;

    // the value currently being scanned
    int valueStart = -1;
    int depth;
    bool inString;
    bool escape;
    bool inScalar;
    ArrayState arrayState;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonFeed"/> class.
    /// </summary>
    /// <param name="mode">How to split the input into values.</param>
    public JsonFeed(JsonFeedMode mode = JsonFeedMode.Values)
    {
        this.mode = mode;
    }

    /// <summary>
    /// Gets the maximum nesting depth of arrays and objects within a value (passed on to the returned readers). The default is 64.
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
    /// Gets a value indicating whether the returned readers return <see cref="JsonNumber"/> for numbers that would otherwise lose
    /// digits or be rejected (see <see cref="JsonReader.PreserveInexactNumbers"/>). The default is <c>false</c>.
    /// </summary>
    public bool PreserveInexactNumbers { get; init; }

    /// <summary>
    /// Appends a chunk of the input.
    /// </summary>
    /// <param name="chars">The characters.</param>
    /// <exception cref="InvalidOperationException"><see cref="Complete"/> has been called.</exception>
    public void Append(ReadOnlySpan<char> chars)
    {
        if (completed)
            throw new InvalidOperationException("The input has already been completed.");

        EnsureCapacity(chars.Length);
        chars.CopyTo(buffer.AsSpan(length));
        length += chars.Length;
    }

    /// <summary>
    /// Signals the end of the input. Call <see cref="TryGetValue"/> afterwards to get the remaining values.
    /// </summary>
    public void Complete()
    {
        completed = true;
    }

    /// <summary>
    /// Gets the next complete value.
    /// </summary>
    /// <param name="reader">A reader for the value, valid until the next call to a method of this feed.</param>
    /// <returns><c>true</c> if a complete value is available; <c>false</c> if more input is needed, or if <see cref="Complete"/>
    /// has been called and all values have been returned.</returns>
    /// <exception cref="ParserException">The input is malformed around a value boundary, or (after <see cref="Complete"/>) ends
    /// within a value.</exception>
    public bool TryGetValue([NotNullWhen(true)] out JsonReader? reader)
    {
        if (!TryScan(out var end))
        {
            reader = null;
            return false;
        }

        reader = new JsonReader(buffer.AsMemory(valueStart, end - valueStart)) { MaxDepth = maxDepth, PreserveInexactNumbers = PreserveInexactNumbers };

        valueStart = -1;
        start = end;
        scan = end;

        if (mode == JsonFeedMode.ArrayElements)
            arrayState = ArrayState.AfterElement;

        return true;
    }

    /// <summary>
    /// Scans for the end of the next value.
    /// </summary>
    private bool TryScan(out int end)
    {
        var chars = buffer;
        int i = scan;
        end = 0;

        if (valueStart < 0)
        {
            // between values: skip whitespace and, in array mode, the array's brackets and commas
            while (true)
            {
                while (i < length && IsWhitespace(chars[i])) i++;

                // nothing before i is needed any more
                start = scan = i;

                if (i == length)
                {
                    if (completed && mode == JsonFeedMode.ArrayElements && arrayState != ArrayState.End)
                        throw Error("unexpected end of input");
                    return false;
                }

                var c = chars[i];

                if (mode == JsonFeedMode.Values || arrayState is ArrayState.BeforeElement)
                {
                    if (arrayState is ArrayState.BeforeElement && c is ',' or ']')
                        throw Error("expected a value");
                    break;
                }

                switch (arrayState)
                {
                    case ArrayState.Start:
                        if (c != '[') throw Error("expected '['");
                        arrayState = ArrayState.BeforeFirstElement;
                        i++;
                        continue;
                    case ArrayState.BeforeFirstElement:
                        if (c == ']')
                        {
                            arrayState = ArrayState.End;
                            i++;
                            continue;
                        }
                        if (c == ',') throw Error("expected a value");
                        break;
                    case ArrayState.AfterElement:
                        if (c == ',')
                        {
                            arrayState = ArrayState.BeforeElement;
                            i++;
                            continue;
                        }
                        if (c == ']')
                        {
                            arrayState = ArrayState.End;
                            i++;
                            continue;
                        }
                        throw Error("expected ',' or ']'");
                    default:
                        throw Error("extra characters at end");
                }

                break;
            }

            // the start of a value
            var first = chars[i];
            valueStart = i;
            depth = 0;
            escape = false;
            inString = first == '"';
            inScalar = first is not ('"' or '{' or '[');
            if (first is '{' or '[') depth = 1;
            i++;

            // a closing bracket or comma where a value should start: return it as a one-character value, which the reader rejects
            if (first is ']' or '}' or ',')
            {
                end = i;
                return true;
            }
        }

        if (inScalar)
        {
            // a number or literal ends at whitespace or a structural character, or at the end of the input
            while (i < length && !EndsScalar(chars[i])) i++;

            if (i == length && !completed)
            {
                scan = i;
                return false;
            }

            end = i;
            return true;
        }

        while (i < length)
        {
            var c = chars[i++];

            if (inString)
            {
                if (escape)
                {
                    escape = false;
                }
                else if (c == '\\')
                {
                    escape = true;
                }
                else if (c == '"')
                {
                    inString = false;

                    if (depth == 0)
                    {
                        end = i;
                        return true;
                    }
                }
            }
            else if (c == '"')
            {
                inString = true;
            }
            else if (c is '{' or '[')
            {
                depth++;
            }
            else if (c is '}' or ']' && --depth == 0)
            {
                end = i;
                return true;
            }
        }

        scan = i;

        if (completed)
            throw Error("unexpected end of input");

        return false;
    }

    private static bool IsWhitespace(char c) => c is ' ' or '\t' or '\r' or '\n';

    private static bool EndsScalar(char c) => IsWhitespace(c) || c is ',' or ']' or '}' or '[' or '{' or '"';

    private static ParserException Error(string message) => new("Parse error: " + message + ".", 0, 0);

    /// <summary>
    /// Makes room for <paramref name="count"/> more characters, moving the characters that are still needed to the start of the buffer.
    /// </summary>
    private void EnsureCapacity(int count)
    {
        if (length + count <= buffer.Length)
            return;

        int keep = length - start;

        if (keep + count > buffer.Length)
        {
            var larger = new char[Math.Max(buffer.Length * 2, keep + count)];
            Array.Copy(buffer, start, larger, 0, keep);
            buffer = larger;
        }
        else
        {
            Array.Copy(buffer, start, buffer, 0, keep);
        }

        scan -= start;
        if (valueStart >= 0) valueStart -= start;
        length = keep;
        start = 0;
    }
}
