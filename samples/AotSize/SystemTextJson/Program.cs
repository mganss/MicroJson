// The same program as MicroJsonTyped, using System.Text.Json source generation for comparison.
using System.Text.Json;
using System.Text.Json.Serialization;

var person = JsonSerializer.Deserialize(args[0], AppJson.Default.Person)!;
person.Age++;
Console.WriteLine(JsonSerializer.Serialize(person, AppJson.Default.Person));

public class Person
{
    public string Name { get; set; } = "";
    public int Age { get; set; }
    public List<string> Tags { get; set; } = [];
}

[JsonSerializable(typeof(Person))]
partial class AppJson : JsonSerializerContext { }
