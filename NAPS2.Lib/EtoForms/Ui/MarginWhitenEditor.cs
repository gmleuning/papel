using Eto.Forms;
using NAPS2.EtoForms.Layout;
using NAPS2.EtoForms.Widgets;
using NAPS2.Papel;

namespace NAPS2.EtoForms.Ui;

/// <summary>
/// Paints the page margins white (like repdf -Margin), in millimetres.
/// </summary>
public class MarginWhitenEditor : UnaryImageEditor
{
    // Values are tenths of a millimetre so the slider can do 0.5 mm steps
    private static readonly SliderWithTextBox.Constraints MmConstraints =
        new SliderWithTextBox.DecimalConstraints(0, 40, 5, 1);

    private readonly SliderWithTextBox _all = new(MmConstraints);
    private readonly SliderWithTextBox _left = new(MmConstraints);
    private readonly SliderWithTextBox _right = new(MmConstraints);
    private readonly SliderWithTextBox _top = new(MmConstraints);
    private readonly SliderWithTextBox _bottom = new(MmConstraints);
    private readonly CheckBox _separate = C.CheckBox("Set each side separately");
    private readonly LayoutVisibility _separateVis = new(false);
    private float _dpi = 300;

    public MarginWhitenEditor(Naps2Config config, UiImageList imageList,
        ThumbnailController thumbnailController, IIconProvider iconProvider) :
        base(config, imageList, thumbnailController)
    {
        IconName = "transform_crop";
        Title = "Whiten margins";

        _all.DecimalValue = (decimal) PapelSettings.Current.MarginMm;
        _all.ValueChanged += () =>
        {
            if (_separate.IsChecked()) return;
            _left.DecimalValue = _right.DecimalValue = _top.DecimalValue = _bottom.DecimalValue = _all.DecimalValue;
        };
        _left.DecimalValue = _right.DecimalValue = _top.DecimalValue = _bottom.DecimalValue = _all.DecimalValue;
        _separate.CheckedChanged += (_, _) =>
        {
            _separateVis.IsVisible = _separate.IsChecked();
            _all.Enabled = !_separate.IsChecked();
            UpdatePreviewBox();
        };
        Sliders = [_all, _left, _right, _top, _bottom];
    }

    protected override void OnFullSizeImageRendered(IMemoryImage realImage)
    {
        _dpi = realImage.HorizontalResolution > 10 ? realImage.HorizontalResolution : 300;
    }

    private int ToPx(SliderWithTextBox slider) => (int) Math.Round((double) slider.DecimalValue / 25.4 * _dpi);

    protected override List<Transform> Transforms =>
    [
        new MarginWhitenTransform(ToPx(_left), ToPx(_right), ToPx(_top), ToPx(_bottom),
            RealImageWidth > 0 ? RealImageWidth : null, RealImageHeight > 0 ? RealImageHeight : null)
    ];

    protected override LayoutElement CreateControls()
    {
        return L.Column(
            C.Label("Margin (mm)"),
            _all,
            _separate,
            L.Column(
                C.Label("Left"), _left,
                C.Label("Right"), _right,
                C.Label("Top"), _top,
                C.Label("Bottom"), _bottom
            ).Visible(_separateVis),
            ApplyToSelectedControl
        );
    }

    protected override void Apply()
    {
        PapelSettings.Current.MarginMm = (double) _all.DecimalValue;
        PapelSettings.Current.Save();
        base.Apply();
    }

    protected override void Revert()
    {
        _separate.Checked = false;
        _all.DecimalValue = 3;
    }
}
