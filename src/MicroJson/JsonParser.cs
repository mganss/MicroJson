//
// JsonParser.cs
//
// Author:
//       Michael Ganss <michael@ganss.org>
//
// Copyright (c) 2011 Michael Ganss
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.IO;

namespace MicroJson
{
#pragma warning disable 1591
    public class ParserException : Exception
    {
        public int Line { get; private set; }
        public int Column { get; private set; }

        public ParserException(string msg, int line, int col)
            : base(msg)
        {
            Line = line;
            Column = col;
        }
    }

    public interface ILogger
    {
        void WriteLine(string message, params object[] arguments);
    }

    public class TextWriterLogger : ILogger
    {
        public TextWriter Writer { get; set; }

        public TextWriterLogger(TextWriter writer)
        {
            Writer = writer;
        }

        public void WriteLine(string message, params object[] arguments)
        {
            if (arguments != null && arguments.Length > 0)
                Writer.WriteLine(message, arguments);
            else
                Writer.WriteLine(message);
        }
    }
#pragma warning restore 1591

    /// <summary>
    /// Parses JSON into generic CLR objects: objects become <see cref="IDictionary{TKey, TValue}"/> of <see cref="string"/> and <see cref="object"/>,
    /// arrays become <see cref="IList{T}"/> of <see cref="object"/>.
    /// </summary>
    public class JsonParser
    {
        int maxDepth = 64;

        /// <summary>
        /// Gets or sets the logger.
        /// </summary>
        /// <value>
        /// The logger.
        /// </value>
        public ILogger? Logger { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to collect line info during parsing.
        /// </summary>
        /// <value>
        /// <c>true</c> if line info should be collected during parsing; otherwise, <c>false</c>.
        /// </value>
        public bool CollectLineInfo { get; set; }

        /// <summary>
        /// Gets or sets the maximum nesting depth of arrays and objects.
        /// Input that is nested deeper causes a <see cref="ParserException"/>.
        /// The default is 64.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
        public int MaxDepth
        {
            get => maxDepth;
            set
            {
                ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
                maxDepth = value;
            }
        }

        /// <summary>
        /// Parse the specified JSON text.
        /// </summary>
        /// <param name='text'>
        /// The JSON text to parse.
        /// </param>
        public object? Parse(string text)
        {
            if (text == null)
                throw new ParserException("Parse error: input is null.", 0, 0);

            var reader = new JsonReader(text) { MaxDepth = MaxDepth, CollectLineInfo = CollectLineInfo, Logger = Logger };
            var value = reader.ReadValue();
            reader.ReadEndOfInput();

            return value;
        }
    }
}
