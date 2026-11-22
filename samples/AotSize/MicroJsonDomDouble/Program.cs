// The same program as MicroJsonDom, with the MicroJson.UseDecimal feature switch turned off (numbers that are not integers become double).
using MicroJson;

var person = (IDictionary<string, object?>)new JsonParser().Parse(args[0])!;
person["Age"] = (int)person["Age"]! + 1;

var writer = new JsonWriter();
writer.WriteValue(person);
Console.WriteLine(writer.ToString());
