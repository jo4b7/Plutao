using System.Drawing.Drawing2D;

namespace Plutao;

public sealed class DarkProgressBar : Control
{
    private double _value;

    public double Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(value, 0, 100);
            Invalidate();
        }
    }

    public string DisplayText { get; set; } = "0%";

    public DarkProgressBar()
    {
        DoubleBuffered = true;
        Height = 28;
        MinimumSize = new Size(160, 24);
        BackColor = Color.FromArgb(26, 26, 26);
        ForeColor = Color.White;
        Font = new Font("Segoe UI Semibold", 9F);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = ClientRectangle;
        if (rect.Width <= 0 || rect.Height <= 0) return;

        using var bg = new SolidBrush(Color.FromArgb(32, 32, 32));
        using var border = new Pen(Color.FromArgb(72, 72, 72));
        using var fill = new SolidBrush(Color.FromArgb(111, 71, 255));

        e.Graphics.FillRectangle(bg, rect);

        var fillWidth = (int)Math.Round((rect.Width - 2) * (_value / 100.0));
        if (fillWidth > 0)
            e.Graphics.FillRectangle(fill, new Rectangle(1, 1, fillWidth, Math.Max(1, rect.Height - 2)));

        e.Graphics.DrawRectangle(border, 0, 0, Math.Max(0, rect.Width - 1), Math.Max(0, rect.Height - 1));

        var text = string.IsNullOrWhiteSpace(DisplayText) ? $"{_value:0}%" : DisplayText;
        TextRenderer.DrawText(
            e.Graphics,
            text,
            Font,
            rect,
            ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}
