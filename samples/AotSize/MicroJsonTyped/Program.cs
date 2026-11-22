// Deserializes JSON into a POCO with generated code, modifies it, and serializes it back.
using MicroJson;

var person = AppJson.Deserialize<Person>(args[0]);
person.Age++;
Console.WriteLine(AppJson.Serialize(person));

public class Person
{
    public string Name { get; set; } = "";
    public int Age { get; set; }
    public List<string> Tags { get; set; } = [];
}

[MicroJsonSerializable(typeof(Person))]
partial class AppJson { }
