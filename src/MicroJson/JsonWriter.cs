using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace MicroJson;

/// <summary>
/// Writes compact JSON text.
/// </summary>
/// <remarks>
/// Commas between array items and object properties are inserted automatically.
/// Inside an object, call <see cref="WritePropertyName"/> before each value.
/// Call <see cref="ToString"/> to get the JSON text.
/// </remarks>
public sealed class JsonWriter
{
    readonly StringBuilder sb = new();
    bool needComma;

    /// <summary>
    /// Writes the start of an object (<c>{</c>).
    /// </summary>
    public void WriteStartObject()
    {
        BeginValue();
        sb.Append('{');
        needComma = false;
    }

    /// <summary>
    /// Writes the end of an object (<c>}</c>).
    /// </summary>
    public void WriteEndObject()
    {
        sb.Append('}');
        needComma = true;
    }

    /// <summary>
    /// Writes the start of an array (<c>[</c>).
    /// </summary>
    public void WriteStartArray()
    {
        BeginValue();
        sb.Append('[');
        needComma = false;
    }

    /// <summary>
    /// Writes the end of an array (<c>]</c>).
    /// </summary>
    public void WriteEndArray()
    {
        sb.Append(']');
        needComma = true;
    }

    /// <summary>
    /// Writes a property name. The property's value must be written next.
    /// </summary>
    /// <param name="name">The property name.</param>
    public void WritePropertyName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        BeginValue();
        WriteQuoted(name);
        sb.Append(':');
        needComma = false;
    }

    /// <summary>
    /// Writes a string, or <c>null</c> if <paramref name="value"/> is <c>null</c>.
    /// </summary>
    /// <param name="value">The string.</param>
    public void WriteString(string? value)
    {
        BeginValue();

        if (value is null)
            sb.Append("null");
        else
            WriteQuoted(value);

        needComma = true;
    }

    /// <summary>
    /// Writes <c>null</c>.
    /// </summary>
    public void WriteNull()
    {
        BeginValue();
        sb.Append("null");
        needComma = true;
    }

    /// <summary>
    /// Writes <c>true</c> or <c>false</c>.
    /// </summary>
    /// <param name="value">The value.</param>
    public void WriteBoolean(bool value)
    {
        BeginValue();
        sb.Append(value ? "true" : "false");
        needComma = true;
    }

    /// <summary>
    /// Writes a number using the invariant culture.
    /// </summary>
    /// <typeparam name="T">The numeric type, e.g. <see cref="int"/> or <see cref="double"/>.</typeparam>
    /// <param name="value">The number.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is NaN or infinity, which JSON cannot represent.</exception>
    public void WriteNumber<T>(T value) where T : INumberBase<T>
    {
        if (!T.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), "JSON does not support NaN or infinity.");

        BeginValue();
        AppendFormatted(value, default);
        needComma = true;
    }

    /// <summary>
    /// Writes a value as a string formatted with the invariant culture, e.g. a <see cref="DateTime"/> with format <c>"O"</c>.
    /// The formatted value must not contain characters that need to be escaped in JSON strings.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="value">The value.</param>
    /// <param name="format">The format string.</param>
    public void WriteFormatted<T>(T value, ReadOnlySpan<char> format) where T : ISpanFormattable
    {
        BeginValue();
        sb.Append('"');
        AppendFormatted(value, format);
        sb.Append('"');
        needComma = true;
    }

    /// <summary>
    /// Writes a generic CLR object as produced by <see cref="JsonParser"/>: <c>null</c>, <see cref="string"/>, <see cref="bool"/>,
    /// numbers, <see cref="IDictionary{TKey, TValue}"/> of <see cref="string"/> and <see cref="object"/>,
    /// and <see cref="IEnumerable{T}"/> of <see cref="object"/>.
    /// Dictionary keys are written in their enumeration order.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <exception cref="NotSupportedException">The value (or a nested value) has an unsupported type.</exception>
    public void WriteValue(object? value)
    {
        switch (value)
        {
            case null:
                WriteNull();
                break;
            case string s:
                WriteString(s);
                break;
            case bool b:
                WriteBoolean(b);
                break;
            case int i:
                WriteNumber(i);
                break;
            case long l:
                WriteNumber(l);
                break;
            case decimal m:
                WriteNumber(m);
                break;
            case double d:
                WriteNumber(d);
                break;
            case IEnumerable<KeyValuePair<string, object?>> dict:
                WriteStartObject();
                foreach (var kvp in dict)
                {
                    WritePropertyName(kvp.Key);
                    WriteValue(kvp.Value);
                }
                WriteEndObject();
                break;
            case IEnumerable<object?> list:
                WriteStartArray();
                foreach (var item in list)
                    WriteValue(item);
                WriteEndArray();
                break;
            default:
                throw new NotSupportedException("Cannot write a value of this type as JSON.");
        }
    }

    /// <summary>
    /// Returns the JSON text written so far.
    /// </summary>
    public override string ToString() => sb.ToString();

    private void BeginValue()
    {
        if (needComma)
            sb.Append(',');
    }

    private void AppendFormatted<T>(T value, ReadOnlySpan<char> format) where T : ISpanFormattable
    {
        Span<char> buffer = stackalloc char[64];

        if (value.TryFormat(buffer, out var written, format, CultureInfo.InvariantCulture))
            sb.Append(buffer[..written]);
        else
            sb.Append(value.ToString(format.ToString(), CultureInfo.InvariantCulture));
    }

    private void WriteQuoted(string s)
    {
        sb.Append('"');

        int start = 0;

        for (int i = 0; i < s.Length; i++)
        {
            var c = s[i];

            if (c >= 0x20 && c != '"' && c != '\\')
                continue;

            sb.Append(s, start, i - start);
            start = i + 1;

            switch (c)
            {
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '\b':
                    sb.Append("\\b");
                    break;
                case '\f':
                    sb.Append("\\f");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                default:
                    sb.Append("\\u00");
                    sb.Append(HexDigit(c >> 4));
                    sb.Append(HexDigit(c & 0xF));
                    break;
            }
        }

        sb.Append(s, start, s.Length - start);
        sb.Append('"');
    }

    private static char HexDigit(int value) => (char)(value < 10 ? '0' + value : 'a' + value - 10);
}
