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
    Check(material is GraphCardMaterial.Mica or GraphCardMaterial.Blurred ? GraphCardMaterial.Transparent : material, GraphCardMaterials.ResolveSaved(material, true));
    Check(material is GraphCardMaterial.Mica or GraphCardMaterial.Blurred ? GraphCardMaterial.Transparent : material, GraphCardMaterials.ResolveSaved(material, false));
    var json = JsonSerializer.Serialize<GraphCardMaterial?>(material, options);
    Check(material is GraphCardMaterial.Mica or GraphCardMaterial.Blurred ? GraphCardMaterial.Transparent : material, GraphCardMaterials.ResolveSaved(JsonSerializer.Deserialize<GraphCardMaterial?>(json, options), true));
}
Console.WriteLine($"Passed {checks} graph material migration and persistence checks.");

foreach (var (name, type, expected) in new[] {
    ("Download Speed", "Throughput", "\u2B07"), ("Upload Speed", "Throughput", "\u2B06"),
    ("Download", "Throughput", "\u2B07"), ("Upload", "Throughput", "\u2B06"),
    ("Download Speed", "Data", "Download Speed"), ("CPU", "Load", "CPU"),
    ("Custom name", "Throughput", "Custom name") })
{
    if (FluentSensors.Common.Sensors.NetworkDirectionLabel.Resolve(name, type) != expected)
        throw new Exception("Incorrect direction label: " + name);
}
Console.WriteLine("Passed 7 direction label checks; custom names and unrelated sensor types preserved.");

foreach (var material in Enum.GetValues<GraphCardMaterial>())
{
    if (GraphCardMaterials.ResolveBlur(null, material) != (material is GraphCardMaterial.Mica or GraphCardMaterial.Blurred)) throw new Exception("Legacy blur migration");
    if (GraphCardMaterials.ResolveBlur(false, material)) throw new Exception("Explicit off must win");
    if (!GraphCardMaterials.ResolveBlur(true, material)) throw new Exception("Explicit on must win");
}
if (GraphCardMaterials.ResolveBlur(null, null)) throw new Exception("New settings must default off");
Console.WriteLine("Passed 16 independent blur/migration checks.");
