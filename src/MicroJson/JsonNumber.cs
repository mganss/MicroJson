using System;
using System.Globalization;

namespace MicroJson;

/// <summary>
/// A JSON number kept as its original text.
/// </summary>
/// <remarks>
/// <see cref="JsonParser"/> and <see cref="JsonReader.ReadValue"/> return a <see cref="JsonNumber"/> instead of rounding a number,
/// turning it into 0, or rejecting it as out of range, if <see cref="JsonParser.PreserveInexactNumbers"/> is set.
/// <see cref="JsonWriter.WriteValue"/> writes the text unchanged, so such numbers survive a round trip exactly.
/// </remarks>
public sealed class JsonNumber : IEquatable<JsonNumber>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JsonNumber"/> class.
    /// </summary>
    /// <param name="text">The number in JSON syntax, e.g. <c>1e400</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="text"/> is not a valid JSON number.</exception>
    public JsonNumber(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (!IsValid(text))
            throw new ArgumentException("The text is not a valid JSON number.", nameof(text));

        Text = text;
    }

    // used by JsonReader, which has already validated the text
    internal JsonNumber(string text, bool validated)
    {
        _ = validated;
        Text = text;
    }

    /// <summary>
    /// Gets the number's text as it appears in the JSON.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Converts the number to a <see cref="double"/>, rounding if necessary.
    /// </summary>
    /// <param name="value">The converted value.</param>
    /// <returns><c>false</c> if the number is out of <see cref="double"/>'s range.</returns>
    public bool TryGetDouble(out double value) =>
        double.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    /// <summary>
    /// Converts the number to a <see cref="decimal"/>, rounding if necessary.
    /// </summary>
    /// <param name="value">The converted value.</param>
    /// <returns><c>false</c> if the number is out of <see cref="decimal"/>'s range.</returns>
    public bool TryGetDecimal(out decimal value) =>
        decimal.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    /// <summary>
    /// Returns the number's text.
    /// </summary>
    public override string ToString() => Text;

    /// <summary>
    /// Determines whether the text of two numbers is the same (<c>1e400</c> and <c>1E400</c> are different).
    /// </summary>
    /// <param name="other">The other number.</param>
    public bool Equals(JsonNumber? other) => other is not null && string.Equals(Text, other.Text, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as JsonNumber);

    /// <inheritdoc/>
    // string.GetHashCode() is ordinal; the overload taking a StringComparison would add about 1.3 KB with Native AOT
    // because it selects a comparer at runtime, so the culture-aware comparers are compiled in too
    public override int GetHashCode() => Text.GetHashCode();

    private static bool IsValid(string text)
    {
        // no surrounding whitespace, which the reader would skip
        if (text.Length == 0 || (text[0] != '-' && !char.IsAsciiDigit(text[0])) || !char.IsAsciiDigit(text[^1]))
            return false;

        try
        {
            var reader = new JsonReader(text);
            reader.Skip();
            reader.ReadEndOfInput();
            return true;
        }
        catch (ParserException)
        {
            return false;
        }
    }
}
