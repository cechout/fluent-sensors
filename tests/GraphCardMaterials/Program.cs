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
    Check(material, GraphCardMaterials.ResolveSaved(material, true));
    Check(material, GraphCardMaterials.ResolveSaved(material, false));
    var json = JsonSerializer.Serialize<GraphCardMaterial?>(material, options);
    Check(material, GraphCardMaterials.ResolveSaved(JsonSerializer.Deserialize<GraphCardMaterial?>(json, options), true));
}
Console.WriteLine($"Passed {checks} graph material migration and persistence checks.");
