namespace FluentSensors.Common.UI
{
    public enum GraphCardMaterial
    {
        Calculated,
        Transparent,
        Acrylic,
        Mica,
        Blurred
    }

    public static class GraphCardMaterials
    {
        public static GraphCardMaterial ResolveSaved(GraphCardMaterial? material, bool legacyTransparent) =>
            material is GraphCardMaterial saved && System.Enum.IsDefined(saved)
                ? saved == GraphCardMaterial.Mica ? GraphCardMaterial.Blurred : saved
                : legacyTransparent ? GraphCardMaterial.Transparent : GraphCardMaterial.Calculated;
    }
}
