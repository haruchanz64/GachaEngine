using GachaEngine.Data;
using GachaEngine.Editor.UI;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace GachaEngine.Editor.Inspectors
{
    /// <summary>
    /// UI Toolkit custom Inspector for <see cref="GachaBannerAsset"/> adding one-click preset buttons and a launcher for <see cref="GachaSimulatorWindow"/>.
    /// </summary>
    [CustomEditor(typeof(GachaBannerAsset))]
    public sealed class GachaBannerInspector : UnityEditor.Editor
    {
        /// <summary>
        /// Builds the default Inspector fields followed by the preset template actions and Banner Simulator launch button.
        /// </summary>
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            GachaSimulatorWindow.AttachStyles(root);

            InspectorElement.FillDefaultInspector(root, serializedObject, this);

            var card = new VisualElement();
            card.AddToClassList("ge-card");
            card.style.marginTop = 8f;

            var title = new Label("GachaEngine Presets & Simulator");
            title.AddToClassList("ge-section-title");
            card.Add(title);

            var banner = (GachaBannerAsset)target;

            var presetRow = new VisualElement();
            presetRow.AddToClassList("ge-row");
            presetRow.Add(new Button(() => ApplyPreset(banner, b => b.ApplyLimitedCharacterPreset())) { text = "Limited 50/50" });
            presetRow.Add(new Button(() => ApplyPreset(banner, b => b.ApplyFatePathWeaponPreset())) { text = "Fate Path" });
            presetRow.Add(new Button(() => ApplyPreset(banner, b => b.ApplyStepUpPreset())) { text = "Step-Up" });
            card.Add(presetRow);

            var openBtn = new Button(() => GachaSimulatorWindow.OpenWithBanner(banner))
            {
                text = "Open in Banner Simulator"
            };
            openBtn.style.height = 26f;
            card.Add(openBtn);

            root.Add(card);
            return root;
        }

        /// <summary>
        /// Records an Undo snapshot, applies a preset mutator to <paramref name="banner"/>, and marks the asset dirty.
        /// </summary>
        private static void ApplyPreset(GachaBannerAsset banner, System.Action<GachaBannerAsset> apply)
        {
            Undo.RecordObject(banner, "Apply Gacha Banner Preset");
            apply(banner);
            EditorUtility.SetDirty(banner);
        }
    }
}
