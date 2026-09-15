using System.Drawing.Drawing2D;

namespace DKImageSimpleUpscaler;

internal readonly record struct ViewState(double ZoomFactor, double CenterX, double CenterY);

internal sealed class ZoomPanViewer : Control
{
    private Image? _image;
    private double _zoomFactor = 1.0;
    private double _centerX = 0.5;
    private double _centerY = 0.5;
    private bool _dragging;
    private Point _lastMouse;
    private bool _suppressViewChanged;

    internal event EventHandler? ViewChanged;

    internal Image? Image
    {
        get => _image;
        set
        {
            _image = value;
            Invalidate();
        }
    }

    internal ViewState ViewState => new(_zoomFactor, _centerX, _centerY);
    internal int ZoomPercent => (int)Math.Round(_zoomFactor * 100.0);

    internal ZoomPanViewer()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(32, 32, 32);
        Cursor = Cursors.Hand;
        TabStop = true;
        SetStyle(ControlStyles.Selectable, true);
    }

    internal void Fit(bool notify = true)
    {
        _zoomFactor = 1.0;
        _centerX = 0.5;
        _centerY = 0.5;
        Invalidate();
        if (notify) RaiseViewChanged();
    }

    internal void ApplyViewState(ViewState state)
    {
        _suppressViewChanged = true;
        try
        {
            _zoomFactor = Math.Clamp(state.ZoomFactor, 0.10, 32.0);
            _centerX = Math.Clamp(state.CenterX, 0.0, 1.0);
            _centerY = Math.Clamp(state.CenterY, 0.0, 1.0);
            ClampCenter();
            Invalidate();
        }
        finally
        {
            _suppressViewChanged = false;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_image is null || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;

        double scale = GetDisplayScale();
        double width = _image.Width * scale;
        double height = _image.Height * scale;
        double x = ClientSize.Width / 2.0 - _centerX * width;
        double y = ClientSize.Height / 2.0 - _centerY * height;

        e.Graphics.InterpolationMode = _zoomFactor > 1.0
            ? InterpolationMode.NearestNeighbor
            : InterpolationMode.HighQualityBicubic;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        e.Graphics.CompositingQuality = CompositingQuality.HighQuality;
        e.Graphics.DrawImage(_image, RectangleF.FromLTRB(
            (float)x, (float)y, (float)(x + width), (float)(y + height)));
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_image is null) return;
        Focus();

        double oldScale = GetDisplayScale();
        if (oldScale <= 0) return;

        double imageX = _centerX + (e.X - ClientSize.Width / 2.0) / (_image.Width * oldScale);
        double imageY = _centerY + (e.Y - ClientSize.Height / 2.0) / (_image.Height * oldScale);

        double step = e.Delta > 0 ? 1.15 : 1.0 / 1.15;
        _zoomFactor = Math.Clamp(_zoomFactor * step, 0.10, 32.0);

        double newScale = GetDisplayScale();
        _centerX = imageX - (e.X - ClientSize.Width / 2.0) / (_image.Width * newScale);
        _centerY = imageY - (e.Y - ClientSize.Height / 2.0) / (_image.Height * newScale);
        ClampCenter();
        Invalidate();
        RaiseViewChanged();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || _image is null) return;
        Focus();
        _dragging = true;
        _lastMouse = e.Location;
        Capture = true;
        Cursor = Cursors.SizeAll;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging || _image is null) return;

        double scale = GetDisplayScale();
        if (scale <= 0) return;
        int dx = e.X - _lastMouse.X;
        int dy = e.Y - _lastMouse.Y;
        _lastMouse = e.Location;

        _centerX -= dx / (_image.Width * scale);
        _centerY -= dy / (_image.Height * scale);
        ClampCenter();
        Invalidate();
        RaiseViewChanged();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        _dragging = false;
        Capture = false;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button == MouseButtons.Left && _image is not null) Fit();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ClampCenter();
        Invalidate();
    }

    private double GetFitScale()
    {
        if (_image is null || _image.Width <= 0 || _image.Height <= 0 || ClientSize.Width <= 0 || ClientSize.Height <= 0)
            return 1.0;
        return Math.Min(ClientSize.Width / (double)_image.Width, ClientSize.Height / (double)_image.Height);
    }

    private double GetDisplayScale() => GetFitScale() * _zoomFactor;

    private void ClampCenter()
    {
        if (_image is null || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        double scale = GetDisplayScale();
        double displayWidth = _image.Width * scale;
        double displayHeight = _image.Height * scale;

        if (displayWidth <= ClientSize.Width)
            _centerX = 0.5;
        else
        {
            double halfVisible = ClientSize.Width / (2.0 * displayWidth);
            _centerX = Math.Clamp(_centerX, halfVisible, 1.0 - halfVisible);
        }

        if (displayHeight <= ClientSize.Height)
            _centerY = 0.5;
        else
        {
            double halfVisible = ClientSize.Height / (2.0 * displayHeight);
            _centerY = Math.Clamp(_centerY, halfVisible, 1.0 - halfVisible);
        }
    }

    private void RaiseViewChanged()
    {
        if (!_suppressViewChanged) ViewChanged?.Invoke(this, EventArgs.Empty);
    }
}
