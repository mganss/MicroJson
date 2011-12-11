using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Serialization;
using System.Security.Permissions;
using System.Text;

namespace MicroJson
{
    [Serializable]
    public class ParserException : Exception
    {
        public int Line { get; set; }
        public int Column { get; set; }

        public ParserException(string msg, int line, int col)
            : base(msg)
        {
            Line = line;
            Column = col;
        }

        [SecurityPermission(SecurityAction.Demand, SerializationFormatter = true)]
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
        }
    }

    public interface ILogger
    {
        void WriteLine(string message, params object[] arguments);
    }

    public class ConsoleLogger : ILogger
    {
        public void WriteLine(string message, params object[] arguments)
        {
            if (arguments != null && arguments.Length > 0)
                Console.Out.WriteLine(message, arguments);
            else 
                Console.Out.WriteLine(message);
        }
    }

    public class JsonParser
    {
        public string Input { get; private set; }
        public int InputLength { get; private set; }
        public int Pos { get; private set; }
        public int Line { get; private set; }
        public int Col { get; private set; }
        public ILogger Logger { get; set; }
        public bool CollectLineInfo { get; set; }

        private void WriteLineLog(string msg, params object[] args)
        {
            if (Logger != null)
            {
                Logger.WriteLine(msg, args);
            }
        }

        private void ThrowParserException(string msg)
        {
            if (CollectLineInfo)
            {
                throw new ParserException(string.Format(CultureInfo.InvariantCulture, "Parse error: {0} at line {1}, column {2}.", msg, Line, Col), Line, Col);
            }
            else
            {
                throw new ParserException("Parse error: " + msg + ".", 0, 0);
            }
        }

        private void AdvanceInput(int n)
        {
            if (n == 0)
            {
                return;
            }

            if (CollectLineInfo)
            {
                for (int i = Pos; i < Pos + n; i++)
                {
                    var c = Input[i];

                    if (c == '\n')
                    {
                        Line++;
                        Col = 1;
                    }
                    else
                    {
                        Col++;
                    }
                }
            }

            Pos += n;
        }

        private string Accept(string s)
        {
            var len = s.Length;

            if (Pos + len > InputLength)
            {
                return null;
            }

            if (Input.IndexOf(s, Pos, len, StringComparison.Ordinal) != -1)
            {
                var match = Input.Substring(Pos, len);
                AdvanceInput(len);
                return match;
            }

            return null;
        }

        private char Expect(char c)
        {
            if (Pos >= InputLength || Input[Pos] != c)
            {
                ThrowParserException("expected '" + c + "'");
            }

            AdvanceInput(1);

            return c;
        }

        public object Parse(string text)
        {
            if (text == null)
            {
                ThrowParserException("input is null");
                return null;
            }

            Input = text;
            InputLength = text.Length;
            Pos = 0;
            Line = 1;
            Col = 1;
            
            var o = Value();

            SkipWhitespace();

            if (Pos != InputLength) ThrowParserException("extra characters at end");

            return o;
        }

        private object Value()
        {
            SkipWhitespace();

            if (Pos >= InputLength)
            {
                ThrowParserException("input contains no value");
            }

            var nextChar = Input[Pos];

            if (nextChar == '"')
            {
                AdvanceInput(1);
                return String();
            }
            else if (nextChar == '[')
            {
                AdvanceInput(1);
                return List();
            }
            else if (nextChar == '{')
            {
                AdvanceInput(1);
                return Dictionary();
            }
            else if (char.IsDigit(nextChar) || nextChar == '-')
            {
                return Number();
            }
            else
            {
                return Literal();
            }
        }

        private object Number()
        {
            int currentPos = Pos;
            bool dotSeen = false;

            Accept(c => c == '-', ref currentPos);
            ExpectDigits(ref currentPos);

            if (Accept(c => c == '.', ref currentPos))
            {
                dotSeen = true;
                ExpectDigits(ref currentPos);
                if (Accept(c => (c == 'e' || c == 'E'), ref currentPos))
                {
                    Accept(c => (c == '-' || c == '+'), ref currentPos);
                    ExpectDigits(ref currentPos);
                }
            }

            var len = currentPos - Pos;
            var num = Input.Substring(Pos, len);

            if (dotSeen)
            {
                decimal d;
                if (!decimal.TryParse(num, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out d))
                    ThrowParserException("not a number");
                WriteLineLog("decimal: {0}", d);
                AdvanceInput(len);
                return d;
            }
            else
            {
                int i;
                if (!int.TryParse(num, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out i))
                    ThrowParserException("not a number");
                WriteLineLog("int: {0}", i);
                AdvanceInput(len);
                return i;
            }
        }

        private bool Accept(Predicate<char> predicate, ref int pos)
        {
            if (pos < InputLength && predicate(Input[pos]))
            {
                pos++;
                return true;
            }

            return false;
        }

        private void ExpectDigits(ref int pos)
        {
            int start = pos;
            while (pos < InputLength && char.IsDigit(Input[pos])) pos++;
            if (start == pos) ThrowParserException("not a number");
        }

        private string String()
        {
            int currentPos = Pos;
            StringBuilder sb = new StringBuilder();

            while (true)
            {
                if (currentPos >= InputLength)
                {
                    ThrowParserException("unterminated string");
                }

                var c = Input[currentPos];

                if (c == '"')
                {
                    var len = currentPos - Pos;
                    AdvanceInput(len + 1);
                    WriteLineLog("string: {0}", sb);
                    return sb.ToString();
                }
                else if (c == '\\')
                {
                    currentPos++;

                    if (currentPos >= InputLength)
                    {
                        ThrowParserException("unterminated escape sequence string");
                    }

                    c = Input[currentPos];

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
                            if (currentPos >= InputLength)
                                ThrowParserException("unterminated unicode escape in string");
                            else
                            {
                                int u;
                                if (!int.TryParse(Input.Substring(currentPos - 3, 4), NumberStyles.AllowHexSpecifier, NumberFormatInfo.InvariantInfo, out u))
                                    ThrowParserException("not a well-formed unicode escape sequence in string");
                                sb.Append((char)u);
                            }
                            break;
                        default:
                            ThrowParserException("unknown escape sequence in string");
                            break;
                    }
                }
                else if ((int)c < 0x20)
                {
                    ThrowParserException("control character in string");
                }
                else
                {
                    sb.Append(c);
                }

                currentPos++;
            }
        }

        private object Literal()
        {
            if (Accept("true") != null)
            {
                WriteLineLog("bool: true");
                return true;
            }

            if (Accept("false") != null)
            {
                WriteLineLog("bool: false");
                return false;
            }

            if (Accept("null") != null)
            {
                WriteLineLog("null");
                return null;
            }

            ThrowParserException("unknown token");
            return null;
        }

        private IList<object> List()
        {
            WriteLineLog("list: [");

            List<object> list = new List<object>();

            SkipWhitespace();
            if (IsNext(']'))
            {
                AdvanceInput(1); return list;
            }

            object obj = null;
            do
            {
                SkipWhitespace();
                obj = Value();
                if (obj != null)
                {
                    list.Add(obj);
                    SkipWhitespace();
                    if (IsNext(']')) break;
                    Expect(',');
                }
            }
            while (obj != null);

            Expect(']');

            WriteLineLog("]");

            return list;
        }

        private IDictionary<string, object> Dictionary()
        {
            WriteLineLog("Dictionary: {");

            Dictionary<string, object> dict = new Dictionary<string, object>();

            SkipWhitespace();
            if (IsNext('}'))
            {
                AdvanceInput(1); return dict;
            }

            KeyValuePair<string, object>? kvp = null;
            do
            {
                SkipWhitespace();

                kvp = KeyValuePair();

                if (kvp.HasValue)
                {
                    dict[kvp.Value.Key] = kvp.Value.Value;
                }

                SkipWhitespace();
                if (IsNext('}')) break;
                Expect(',');
            }
            while (kvp != null);

            Expect('}');

            WriteLineLog("}");

            return dict;
        }

        private KeyValuePair<string, object>? KeyValuePair()
        {
            Expect('"');

            var key = String();

            SkipWhitespace();

            Expect(':');

            var obj = Value();

            return new KeyValuePair<string, object>(key, obj);
        }

        private void SkipWhitespace()
        {
            int n = Pos;
            while (IsWhiteSpace(n)) n++;
            if (n != Pos)
            {
                AdvanceInput(n - Pos);
            }
        }

        private bool IsWhiteSpace(int n)
        {
            if (n >= InputLength) return false;
            char c = Input[n];
            return c == ' ' || c == '\t' || c == '\r' || c == '\n';
        }

        private bool IsNext(char c)
        {
            return Pos < InputLength && Input[Pos] == c;
        }
    }
}
