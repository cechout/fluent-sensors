namespace FluentSensors.Common.UI
{
    public enum GraphCardMaterial
    {
        Calculated,
        Transparent,
        Acrylic,
        Mica
    }

    public static class GraphCardMaterials
    {
        public static GraphCardMaterial ResolveSaved(GraphCardMaterial? material, bool legacyTransparent) =>
            material is GraphCardMaterial saved && System.Enum.IsDefined(saved)
                ? saved
                : legacyTransparent ? GraphCardMaterial.Transparent : GraphCardMaterial.Calculated;
    }
}
