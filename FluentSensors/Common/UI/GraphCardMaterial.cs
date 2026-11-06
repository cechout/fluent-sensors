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
        public static bool ResolveBlur(bool? blur, GraphCardMaterial? material) =>
            blur ?? (material is GraphCardMaterial.Blurred or GraphCardMaterial.Mica);

        public static GraphCardMaterial ResolveSaved(GraphCardMaterial? material, bool legacyTransparent) =>
            material is GraphCardMaterial saved && System.Enum.IsDefined(saved)
                ? saved is GraphCardMaterial.Mica or GraphCardMaterial.Blurred ? GraphCardMaterial.Transparent : saved
                : legacyTransparent ? GraphCardMaterial.Transparent : GraphCardMaterial.Calculated;
    }
}
