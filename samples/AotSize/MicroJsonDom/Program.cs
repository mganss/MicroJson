// Parses JSON into generic objects with JsonParser, modifies it, and writes it back with JsonWriter.
using MicroJson;

var person = (IDictionary<string, object?>)new JsonParser().Parse(args[0])!;
person["Age"] = (int)person["Age"]! + 1;

var writer = new JsonWriter();
writer.WriteValue(person);
Console.WriteLine(writer.ToString());
