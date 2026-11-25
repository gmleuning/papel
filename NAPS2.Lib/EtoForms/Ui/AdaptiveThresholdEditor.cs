using Eto.Forms;
using NAPS2.EtoForms.Layout;
using NAPS2.EtoForms.Widgets;
using NAPS2.Papel;

namespace NAPS2.EtoForms.Ui;

/// <summary>
/// "Clean B/W": background flattening + adaptive threshold, the same algorithm as the pictopdf/repdf scripts.
/// </summary>
public class AdaptiveThresholdEditor : UnaryImageEditor
{
    private readonly SliderWithTextBox _cSlider = new(new SliderWithTextBox.IntConstraints(0, 60, 5));
    private readonly SliderWithTextBox _blockSlider = new(new SliderWithTextBox.IntConstraints(3, 251, 20));
    private readonly CheckBox _flatten = C.CheckBox("Flatten background (shadows, yellowed paper)");

    public AdaptiveThresholdEditor(Naps2Config config, UiImageList imageList,
        ThumbnailController thumbnailController, IIconProvider iconProvider) :
        base(config, imageList, thumbnailController)
    {
        IconName = "contrast_high";
        Title = "Clean black & white";

        var settings = PapelSettings.Current;
        _cSlider.IntValue = settings.ThresholdC;
        _blockSlider.IntValue = settings.ThresholdBlock;
        _flatten.Checked = settings.ThresholdFlatten;
        _flatten.CheckedChanged += (_, _) => UpdatePreviewBox();

        AttachDpiDependency(scale =>
            _cSlider.Icon = iconProvider.GetIcon("contrast_high", IconScenario.Inline16, scale));
        Sliders = [_cSlider, _blockSlider];
    }

    protected override List<Transform> Transforms =>
    [
        new AdaptiveThresholdTransform(_cSlider.IntValue, _blockSlider.IntValue, _flatten.IsChecked(),
            RealImageWidth > 0 ? RealImageWidth : null)
    ];

    protected override LayoutElement CreateControls()
    {
        return L.Column(
            C.Label("Strength (C) - higher = whiter page, thinner text"),
            _cSlider,
            C.Label("Block size (px) - raise for bold or large type, lower for fine print"),
            _blockSlider,
            _flatten,
            ApplyToSelectedControl
        );
    }

    protected override void Apply()
    {
        var settings = PapelSettings.Current;
        settings.ThresholdC = _cSlider.IntValue;
        settings.ThresholdBlock = _blockSlider.IntValue;
        settings.ThresholdFlatten = _flatten.IsChecked();
        settings.Save();
        base.Apply();
    }

    protected override void Revert()
    {
        _cSlider.IntValue = 15;
        _blockSlider.IntValue = 61;
        _flatten.Checked = true;
    }
}
