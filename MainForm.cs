using System.Drawing.Imaging;

namespace DKImageSimpleUpscaler;

internal sealed class MainForm : Form
{
    private readonly PictureBox _originalBox = new();
    private readonly PictureBox _resultBox = new();
    private readonly ComboBox _modeCombo = new();
    private readonly ComboBox _sizeCombo = new();
    private readonly ComboBox _methodCombo = new();
    private readonly ComboBox _tileCombo = new();
    private readonly NumericUpDown _aiStrength = new();
    private readonly NumericUpDown _sharpen = new();
    private readonly Label _status = new();
    private readonly Label _engineStatus = new();
    private readonly Button _processButton = new();
    private readonly Button _saveButton = new();
    private readonly Button _engineButton = new();

    private Bitmap? _original;
    private Bitmap? _result;
    private string? _sourcePath;

    public MainForm()
    {
        Text = "DK Image Simple Upscaler";
        MinimumSize = new Size(1100, 680);
        Size = new Size(1400, 860);
        StartPosition = FormStartPosition.CenterScreen;
        AllowDrop = true;
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;

        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        BuildUi();
        UpdateModeUi();
        UpdateEngineStatus();
    }

    private void BuildUi()
    {
        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 92,
            Padding = new Padding(10),
            WrapContents = true,
            AutoSize = false
        };

        var openButton = new Button { Text = "이미지 열기", AutoSize = true };
        openButton.Click += (_, _) => OpenImage();

        _modeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _modeCombo.Width = 135;
        _modeCombo.Items.AddRange(["Text/UI Safe", "AI General", "AI Anime"]);
        _modeCombo.SelectedIndex = 0;
        _modeCombo.SelectedIndexChanged += (_, _) => UpdateModeUi();

        _sizeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _sizeCombo.Width = 90;
        _sizeCombo.Items.AddRange(["2×", "3×", "4×", "FHD", "QHD", "4K"]);
        _sizeCombo.SelectedIndex = 0;

        _methodCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _methodCombo.Width = 110;
        _methodCombo.Items.AddRange(["Lanczos 3", "Bicubic", "Nearest"]);
        _methodCombo.SelectedIndex = 0;

        _tileCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _tileCombo.Width = 80;
        _tileCombo.Items.AddRange(["Auto", "128", "256", "512"]);
        _tileCombo.SelectedIndex = 0;

        _aiStrength.Minimum = 0;
        _aiStrength.Maximum = 100;
        _aiStrength.Value = 85;
        _aiStrength.Width = 60;

        _sharpen.Minimum = 0;
        _sharpen.Maximum = 100;
        _sharpen.Value = 15;
        _sharpen.Width = 60;

        _processButton.Text = "업스케일";
        _processButton.AutoSize = true;
        _processButton.Click += async (_, _) => await ProcessAsync();

        _saveButton.Text = "결과 저장";
        _saveButton.AutoSize = true;
        _saveButton.Enabled = false;
        _saveButton.Click += (_, _) => SaveResult();

        _engineButton.Text = "AI 엔진 설치";
        _engineButton.AutoSize = true;
        _engineButton.Click += async (_, _) => await InstallEngineAsync();

        _engineStatus.AutoSize = true;
        _engineStatus.Margin = new Padding(7, 7, 3, 0);

        top.Controls.Add(openButton);
        top.Controls.Add(MakeLabel("모드"));
        top.Controls.Add(_modeCombo);
        top.Controls.Add(MakeLabel("크기"));
        top.Controls.Add(_sizeCombo);
        top.Controls.Add(MakeLabel("방식"));
        top.Controls.Add(_methodCombo);
        top.Controls.Add(MakeLabel("AI 강도"));
        top.Controls.Add(_aiStrength);
        top.Controls.Add(MakeLabel("Tile"));
        top.Controls.Add(_tileCombo);
        top.Controls.Add(MakeLabel("샤픈"));
        top.Controls.Add(_sharpen);
        top.Controls.Add(_processButton);
        top.Controls.Add(_saveButton);
        top.Controls.Add(_engineButton);
        top.Controls.Add(_engineStatus);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical
        };

        Shown += (_, _) =>
        {
            if (split.Width > split.SplitterWidth + 2)
                split.SplitterDistance = (split.Width - split.SplitterWidth) / 2;
        };

        split.Panel1.Controls.Add(BuildPreviewPanel("원본", _originalBox));
        split.Panel2.Controls.Add(BuildPreviewPanel("결과", _resultBox));

        _status.Dock = DockStyle.Bottom;
        _status.Height = 30;
        _status.Padding = new Padding(10, 5, 10, 0);
        _status.Text = "이미지를 열거나 창에 드래그하세요.";

        Controls.Add(split);
        Controls.Add(_status);
        Controls.Add(top);
    }

    private static Label MakeLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(10, 7, 3, 0)
    };

    private static Control BuildPreviewPanel(string title, PictureBox picture)
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        var label = new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 28,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold)
        };
        picture.Dock = DockStyle.Fill;
        picture.SizeMode = PictureBoxSizeMode.Zoom;
        picture.BackColor = Color.FromArgb(32, 32, 32);
        panel.Controls.Add(picture);
        panel.Controls.Add(label);
        return panel;
    }

    private void UpdateModeUi()
    {
        bool aiMode = _modeCombo.SelectedIndex > 0;
        _methodCombo.Enabled = !aiMode;
        _aiStrength.Enabled = aiMode;
        _tileCombo.Enabled = aiMode;
        _engineButton.Visible = aiMode || !AiEngineManager.IsInstalled;
        _sharpen.Value = aiMode ? 5 : 15;
    }

    private void UpdateEngineStatus()
    {
        bool installed = AiEngineManager.IsInstalled;
        _engineStatus.Text = installed ? "AI: 설치됨" : "AI: 미설치";
        _engineStatus.ForeColor = installed ? Color.DarkGreen : Color.DimGray;
        _engineButton.Text = installed ? "AI 엔진 재설치" : "AI 엔진 설치";
    }

    private async Task InstallEngineAsync()
    {
        if (AiEngineManager.IsInstalled)
        {
            var answer = MessageBox.Show(this, "AI 엔진을 다시 설치하시겠습니까?", "AI 엔진", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;
        }

        SetBusy(true, "AI 엔진 설치 준비 중…");
        try
        {
            var progress = new Progress<string>(text => _status.Text = text);
            await AiEngineManager.InstallAsync(progress);
            UpdateEngineStatus();
            _status.Text = "AI 엔진 설치 완료";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.ToString(), "AI 엔진 설치 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _status.Text = "AI 엔진 설치에 실패했습니다.";
        }
        finally
        {
            SetBusy(false, null);
        }
    }

    private void OpenImage()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "이미지|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|모든 파일|*.*"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) LoadImage(dialog.FileName);
    }

    private void LoadImage(string path)
    {
        try
        {
            using var temp = new Bitmap(path);
            var bitmap = new Bitmap(temp.Width, temp.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bitmap)) g.DrawImageUnscaled(temp, 0, 0);

            _original?.Dispose();
            _result?.Dispose();
            _original = bitmap;
            _result = null;
            _sourcePath = path;
            _originalBox.Image = _original;
            _resultBox.Image = null;
            _saveButton.Enabled = false;
            _status.Text = $"{Path.GetFileName(path)}  ·  {_original.Width}×{_original.Height}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "이미지 열기 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task ProcessAsync()
    {
        if (_original is null)
        {
            OpenImage();
            if (_original is null) return;
        }

        bool aiMode = _modeCombo.SelectedIndex > 0;
        if (aiMode && !AiEngineManager.IsInstalled)
        {
            var answer = MessageBox.Show(this,
                "AI 모드를 처음 사용하려면 Real-ESRGAN 엔진(약 45MB)을 설치해야 합니다. 지금 설치하시겠습니까?",
                "AI 엔진 필요", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (answer != DialogResult.Yes) return;
            await InstallEngineAsync();
            if (!AiEngineManager.IsInstalled) return;
        }

        var (width, height) = GetTargetSize(_original.Width, _original.Height, _sizeCombo.SelectedItem?.ToString() ?? "2×");
        int sharpen = (int)_sharpen.Value;

        SetBusy(true, $"처리 중… {_original.Width}×{_original.Height} → {width}×{height}");
        try
        {
            using var source = (Bitmap)_original.Clone();
            Bitmap processed;

            if (!aiMode)
            {
                var method = _methodCombo.SelectedIndex switch
                {
                    1 => ResizeMethod.Bicubic,
                    2 => ResizeMethod.NearestNeighbor,
                    _ => ResizeMethod.Lanczos3
                };
                processed = await Task.Run(() => ImageProcessor.Resize(source, width, height, method));
            }
            else
            {
                var model = _modeCombo.SelectedIndex == 2 ? AiModel.Anime : AiModel.General;
                int tile = _tileCombo.SelectedIndex switch { 1 => 128, 2 => 256, 3 => 512, _ => 0 };
                int aiStrength = (int)_aiStrength.Value;
                var progress = new Progress<string>(text => _status.Text = text);

                using var ai4x = await AiEngineManager.UpscaleAsync(source, model, tile, progress);
                using var aiTarget = ai4x.Width == width && ai4x.Height == height
                    ? (Bitmap)ai4x.Clone()
                    : ImageProcessor.Resize(ai4x, width, height, ResizeMethod.Lanczos3);

                if (aiStrength >= 100)
                {
                    processed = (Bitmap)aiTarget.Clone();
                }
                else
                {
                    using var safeTarget = ImageProcessor.Resize(source, width, height, ResizeMethod.Lanczos3);
                    processed = ImageProcessor.Blend(safeTarget, aiTarget, aiStrength);
                }
            }

            ImageProcessor.SharpenInPlace(processed, sharpen);
            _result?.Dispose();
            _result = processed;
            _resultBox.Image = _result;
            _saveButton.Enabled = true;
            string modeText = _modeCombo.SelectedItem?.ToString() ?? "Text/UI Safe";
            _status.Text = $"완료  ·  {width}×{height}  ·  {modeText}  ·  샤픈 {sharpen}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.ToString(), "업스케일 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _status.Text = "처리에 실패했습니다.";
        }
        finally
        {
            SetBusy(false, null);
        }
    }

    private static (int Width, int Height) GetTargetSize(int sourceWidth, int sourceHeight, string option)
    {
        if (option.EndsWith('×'))
        {
            int factor = int.Parse(option[..^1]);
            return (sourceWidth * factor, sourceHeight * factor);
        }

        (int maxWidth, int maxHeight) = option switch
        {
            "FHD" => (1920, 1080),
            "QHD" => (2560, 1440),
            "4K" => (3840, 2160),
            _ => (sourceWidth * 2, sourceHeight * 2)
        };

        double scale = Math.Min(maxWidth / (double)sourceWidth, maxHeight / (double)sourceHeight);
        return (Math.Max(1, (int)Math.Round(sourceWidth * scale)),
                Math.Max(1, (int)Math.Round(sourceHeight * scale)));
    }

    private void SaveResult()
    {
        if (_result is null) return;
        string baseName = _sourcePath is null ? "upscaled" : Path.GetFileNameWithoutExtension(_sourcePath) + "_upscaled";
        using var dialog = new SaveFileDialog
        {
            FileName = baseName + ".png",
            Filter = "PNG|*.png|JPEG|*.jpg;*.jpeg"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        string ext = Path.GetExtension(dialog.FileName).ToLowerInvariant();
        if (ext is ".jpg" or ".jpeg") SaveJpeg(_result, dialog.FileName, 95L);
        else _result.Save(dialog.FileName, ImageFormat.Png);
        _status.Text = $"저장 완료  ·  {dialog.FileName}";
    }

    private static void SaveJpeg(Bitmap bitmap, string path, long quality)
    {
        var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
        bitmap.Save(path, codec, parameters);
    }

    private void SetBusy(bool busy, string? text)
    {
        _processButton.Enabled = !busy;
        _saveButton.Enabled = !busy && _result is not null;
        _sizeCombo.Enabled = !busy;
        _modeCombo.Enabled = !busy;
        _methodCombo.Enabled = !busy && _modeCombo.SelectedIndex == 0;
        _tileCombo.Enabled = !busy && _modeCombo.SelectedIndex > 0;
        _aiStrength.Enabled = !busy && _modeCombo.SelectedIndex > 0;
        _sharpen.Enabled = !busy;
        _engineButton.Enabled = !busy;
        UseWaitCursor = busy;
        if (text is not null) _status.Text = text;
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            LoadImage(files[0]);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _original?.Dispose();
            _result?.Dispose();
        }
        base.Dispose(disposing);
    }
}
