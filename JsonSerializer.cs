// 
// JsonSerializer.cs
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
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace MicroJson
{
    /// <summary>
    /// Serializes and deserializes JSON.
    /// </summary>
    public class JsonSerializer
    {
        /// <summary>
        /// Deserializes JSON text into an object of the specified type.
        /// </summary>
        /// <returns>
        /// An object of the specified type deserialized from JSON.
        /// </returns>
        /// <param name='text'>
        /// The JSON text.
        /// </param>
        /// <typeparam name='T'>
        /// The type to deserialize into.
        /// </typeparam>
        public static T DeserializeObject<T>(string text)
        {
            return new JsonSerializer().Deserialize<T>(text);
        }
  
        /// <summary>
        /// Serializes an object into JSON text.
        /// </summary>
        /// <returns>
        /// The JSON text that represents the object.
        /// </returns>
        /// <param name='obj'>
        /// The object to serialize.
        /// </param>
        public static string SerializeObject(object obj)
        {
            return new JsonSerializer().Serialize(obj);
        }
  
        /// <summary>
        /// Deserializes JSON text into an object of the specified type.
        /// </summary>
        /// <returns>
        /// An object of the specified type deserialized from JSON.
        /// </returns>
        /// <param name='text'>
        /// The JSON text.
        /// </param>
        /// <typeparam name='T'>
        /// The type to deserialize into.
        /// </typeparam>
        public T Deserialize<T>(string text)
        {
            return Deserialize<T>(text, new JsonParser());
        }

        /// <summary>
        /// Deserializes JSON text into an object of the specified type.
        /// </summary>
        /// <returns>
        /// An object of the specified type deserialized from JSON.
        /// </returns>
        /// <param name='text'>
        /// The JSON text.
        /// </param>
        /// <param name='parser'>
        /// The parser to use.
        /// </param>
        /// <typeparam name='T'>
        /// The type to deserialize into.
        /// </typeparam>
        public T Deserialize<T>(string text, JsonParser parser)
        {
            if (parser == null)
            {
                throw new ArgumentException("An invalid argument was specified.", "parser");
            }

            var o = parser.Parse(text);
            return (T)Deserialize(o, typeof(T));
        }
        
        /// <summary>
        /// Deserializes generic POCO object into an object of the specified type.
        /// </summary>
        /// <returns>
        /// An object of the specified type deserialized from POCO.
        /// </returns>
        /// <param name='value'>
        /// The POCO.
        /// </param>
        /// <typeparam name='T'>
        /// The type to deserialize into.
        /// </typeparam>
        public T Deserialize<T>(object value)
        {
            return (T)Deserialize(value, typeof(T));
        }

        static Regex DateTimeRegex = new Regex(@"^/Date\((-?\d+)\)/$");

        private object Deserialize(object from, Type type)
        {
            if (from == null) return null;

            var dict = from as IEnumerable<KeyValuePair<string, object>>;
            if (dict != null)
            {
                var to = Activator.CreateInstance(type);
                DeserializeDictionary(dict, to);
                return to;
            }

            var list = from as IList;
            if (list != null)
            {
                var to = Activator.CreateInstance(type);
                DeserializeList(list, (IList)to);
                return to;
            }

            if (typeof(IList).IsAssignableFrom(type))
            {
                var to = (IList)Activator.CreateInstance(type);
                var itemType = to.GetType().GetProperty("Item").PropertyType;
                to.Add(Deserialize(from, itemType));
                return to;
            }

            if (type.IsEnum)
            {
                return Enum.Parse(type, from.ToString(), true);
            }

            if (type == typeof(DateTime))
            {
                var date = from as string;
                if (date != null)
                {
                    Match dateTimeMatch = DateTimeRegex.Match(date);
                    if (dateTimeMatch.Success)
                    {
                        var ticks = long.Parse(dateTimeMatch.Groups[1].Value, NumberFormatInfo.InvariantInfo);
                        var epochTicks = (ticks * 10000) + 621355968000000000;
                        return new DateTime(epochTicks, DateTimeKind.Utc).ToLocalTime();
                    }
                }
            }

            if (!type.IsAssignableFrom(from.GetType()))
            {
                // Nullable handling
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
                {
                    type = type.GetGenericArguments()[0];
                }

                return Convert.ChangeType(from, type, CultureInfo.InvariantCulture);
            }

            return from;
        }

        private void DeserializeList(IList from, IList to)
        {
            var itemType = to.GetType().GetProperty("Item").PropertyType;
            foreach (var item in from)
            {
                to.Add(Deserialize(item, itemType));
            }
        }

        private void DeserializeDictionary(IEnumerable<KeyValuePair<string, object>> from, object to)
        {
            var type = to.GetType();

            var dict = to as IDictionary;
            if (dict != null)
            {
                var valType = typeof(object);
                while (type != typeof(object))
                {
                    if (type.IsGenericType)
                    {
                        valType = type.GetGenericArguments()[1];
                        break;
                    }

                    type = type.BaseType;
                }

                foreach (var pair in from)
                {
                    dict[pair.Key] = Deserialize(pair.Value, valType);
                }
            }
            else
            {
                foreach (var pair in from)
                {
                    var member = GetMember(type, pair.Key);
                    if (member != null)
                    {
                        member.Set(to, Deserialize(pair.Value, member.Type));
                    }
                }
            }
        }

        class SetterMember
        {
            public Type Type { get; set; }
            public Action<object, object> Set { get; set; }
        }

        private Dictionary<string, SetterMember> MemberCache = new Dictionary<string, SetterMember>();

        private SetterMember GetMember(Type type, string name)
        {
            SetterMember member;
            var key = name + type.GetHashCode();
            if (!MemberCache.TryGetValue(key, out member))
            {
                var members = type.GetMember(name, MemberTypes.Field | MemberTypes.Property,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase | BindingFlags.SetField | BindingFlags.SetProperty);
                if (members != null && members.Length > 0)
                {
                    var m = members[0];
                    if (m.MemberType == MemberTypes.Field)
                    {
                        member = new SetterMember
                        {
                            Type = ((FieldInfo)m).FieldType,
                            Set = (o, v) => ((FieldInfo)m).SetValue(o, v)
                        };
                    }
                    else
                    {
                        var prop = (PropertyInfo)m;
                        if (prop.CanWrite)
                        {
                            member = new SetterMember
                            {
                                Type = prop.PropertyType,
                                Set = (o, v) => ((PropertyInfo)m).SetValue(o, v, null)
                            };
                        }
                    }

                    MemberCache[key] = member;
                }
                else
                {
                    MemberCache[key] = null;
                }
            }

            return member;
        }
  
        /// <summary>
        /// Serialize the specified object into JSON.
        /// </summary>
        /// <param name='obj'>
        /// The object to serialize.
        /// </param>
        public string Serialize(object obj)
        {
            if (obj == null) return "null";

            var list = obj as IList;
            if (list != null && !(obj is IEnumerable<KeyValuePair<string, object>>))
            {
                var sb = new StringBuilder("[");
                if (list.Count > 0)
                {
                    sb.Append(string.Join(",", list.Cast<object>().Select(i => Serialize(i)).ToArray()));
                }
                sb.Append("]");
                return sb.ToString();
            }

            var str = obj as string;
            if (str != null)
            {
                return @"""" + EscapeString(obj.ToString()) + @"""";
            }

            if (obj is int)
            {
                return obj.ToString();
            }

            var b = obj as bool?;
            if (b.HasValue)
            {
                return b.Value ? "true" : "false";
            }

            if (obj is decimal || obj is double || obj is float)
            {
                return ((IFormattable)obj).ToString("G", NumberFormatInfo.InvariantInfo);
            }

            if (obj is Enum)
            {
                return @"""" + EscapeString(obj.ToString()) + @"""";
            }

            if (obj.GetType().IsPrimitive)
            {
                return (string)Convert.ChangeType(obj, typeof(string), CultureInfo.InvariantCulture);
            }

            if (obj is DateTime)
            {
                return SerializeDateTime(obj);
            }

            return SerializeComplexType(obj);
        }

        private static string SerializeDateTime(object o)
        {
            var d = (DateTime)o;
            var ticks = (d.ToUniversalTime().Ticks - 621355968000000000) / 10000;
            return @"""\/Date(" + ticks + @")\/""";
        }

        private static string EscapeString(string src)
        {
            var sb = new StringBuilder();

            foreach (var c in src)
            {
                if (c == '"' || c == '\\')
                {
                    sb.Append('\\');
                    sb.Append(c);
                }
                else if ((int)c < 0x20) // control character
                {
                    var u = (int)c;
                    switch (u)
                    {
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
                            sb.Append("\\u");
                            sb.Append(u.ToString("X4", NumberFormatInfo.InvariantInfo));
                            break;
                    }
                }
                else
                    sb.Append(c);
            }

            return sb.ToString();
        }

        private string SerializeComplexType(object o)
        {
            var s = new StringBuilder("{");

            if (o is IDictionary || o is IEnumerable<KeyValuePair<string,object>>)
            {
                SerializeDictionary(o, s);
            }
            else
            {
                SerializeProperties(o, s);
            }

            s.Append("}");

            return s.ToString();
        }

        private void SerializeProperties(object o, StringBuilder s)
        {
            var type = o.GetType();
            var members = GetMembers(type);

            foreach (var member in members)
            {
                object val = member.Get(o);

                if (val != null && (member.DefaultValue == null || !val.Equals(member.DefaultValue)))
                {
                    var v = Serialize(val);
                    s.Append(@"""");
                    s.Append(member.Name);
                    s.Append(@""":");
                    s.Append(v);
                    s.Append(",");
                }
            }
            
            if (s.Length > 0 && s[s.Length - 1] == ',') s.Remove(s.Length - 1, 1);
        }

        private void SerializeDictionary(object o, StringBuilder s)
		{
			IEnumerable<KeyValuePair<string, object>> kvps;
			var dict = o as IDictionary;
			if (dict != null)
				kvps = dict.Keys.Cast<object>().Select(k => new KeyValuePair<string, object>(k.ToString(), dict[k]));
			else
				kvps = (IEnumerable<KeyValuePair<string, object>>)o;
			
			// work around MonoTouch Full-AOT issue
			var kvpList = kvps.ToList();
			kvpList.Sort((e1, e2) => string.Compare(e1.Key, e2.Key, StringComparison.InvariantCultureIgnoreCase));
			
			foreach (var kvp in kvpList)
			{
				s.Append(@"""");
				s.Append(kvp.Key);
				s.Append(@""":");
				s.Append(Serialize(kvp.Value));
				s.Append(",");
			}

			if (s.Length > 0 && s[s.Length - 1] == ',')
				s.Remove(s.Length - 1, 1);
		}

        class GetterMember
        {
            public string Name { get; set; }
            public Func<object, object> Get { get; set; }
            public object DefaultValue { get; set; }
        }

        private Dictionary<Type, GetterMember[]> MembersCache = new Dictionary<Type, GetterMember[]>();

        private GetterMember[] GetMembers(Type type)
        {
            GetterMember[] members;
            if (!MembersCache.TryGetValue(type, out members))
            {
                members = type.GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)
                    .Where(m => (m.MemberType == MemberTypes.Property && ((PropertyInfo)m).CanWrite)
                        || m.MemberType == MemberTypes.Field)
                    .OrderBy(m => m.Name)
                    .Select(m => BuildGetterMember(m))
                    .ToArray();
                MembersCache[type] = members;
            }

            return members;
        }

        private static GetterMember BuildGetterMember(MemberInfo m)
        {
            var defaultAttribute = m.GetCustomAttributes(typeof(DefaultValueAttribute), true).FirstOrDefault() as DefaultValueAttribute;
            return new GetterMember
            {
                Name = m.Name,
                Get = m.MemberType == MemberTypes.Property ? (Func<object,object>)(o => ((PropertyInfo)m).GetValue(o, null))
                    : (o => ((FieldInfo)m).GetValue(o)),
                DefaultValue = defaultAttribute != null ? defaultAttribute.Value : GetDefaultValueForMember(m)
            };
        }

        private static object GetDefaultValueForMember(MemberInfo m)
        {
            var type = m.MemberType == MemberTypes.Property ? ((PropertyInfo)m).PropertyType : ((FieldInfo)m).FieldType;
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }
    }
}
