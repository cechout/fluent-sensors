using System.Text.Json;
using System.Text.Json.Serialization;
using FluentSensors.Common.UI;

int checks = 0;
void Check(GraphCardMaterial expected, GraphCardMaterial actual)
{
    if (expected != actual) throw new Exception($"Expected {expected}, got {actual}");
    checks++;
}
Check(GraphCardMaterial.Transparent, GraphCardMaterials.ResolveSaved(null, true));
Check(GraphCardMaterial.Calculated, GraphCardMaterials.ResolveSaved(null, false));
Check(GraphCardMaterial.Transparent, GraphCardMaterials.ResolveSaved((GraphCardMaterial)999, true));
Check(GraphCardMaterial.Calculated, GraphCardMaterials.ResolveSaved((GraphCardMaterial)(-1), false));
var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
foreach (var material in Enum.GetValues<GraphCardMaterial>())
{
    Check(material == GraphCardMaterial.Mica ? GraphCardMaterial.Blurred : material, GraphCardMaterials.ResolveSaved(material, true));
    Check(material == GraphCardMaterial.Mica ? GraphCardMaterial.Blurred : material, GraphCardMaterials.ResolveSaved(material, false));
    var json = JsonSerializer.Serialize<GraphCardMaterial?>(material, options);
    Check(material == GraphCardMaterial.Mica ? GraphCardMaterial.Blurred : material, GraphCardMaterials.ResolveSaved(JsonSerializer.Deserialize<GraphCardMaterial?>(json, options), true));
}
Console.WriteLine($"Passed {checks} graph material migration and persistence checks.");

foreach (var (name, type, expected) in new[] {
    ("Download Speed", "Throughput", "\u2193"), ("Upload Speed", "Throughput", "\u2191"),
    ("Download", "Throughput", "\u2193"), ("Upload", "Throughput", "\u2191"),
    ("Download Speed", "Data", "Download Speed"), ("CPU", "Load", "CPU"),
    ("Custom name", "Throughput", "Custom name") })
{
    if (FluentSensors.Common.Sensors.NetworkDirectionLabel.Resolve(name, type) != expected)
        throw new Exception("Incorrect direction label: " + name);
}
Console.WriteLine("Passed 7 direction label checks; custom names and unrelated sensor types preserved.");
