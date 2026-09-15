using System.Drawing.Imaging;

namespace DKImageSimpleUpscaler;

internal sealed class MainForm : Form
{
    private sealed record GpuChoice(int? Id, string Label, string? DeviceName = null)
    {
        public override string ToString() => Label;
    }

    private readonly ZoomPanViewer _originalView = new();
    private readonly ZoomPanViewer _resultView = new();
    private readonly Label _originalZoomLabel = new();
    private readonly Label _resultZoomLabel = new();
    private readonly ComboBox _modeCombo = new();
    private readonly ComboBox _sizeCombo = new();
    private readonly ComboBox _methodCombo = new();
    private readonly ComboBox _tileCombo = new();
    private readonly ComboBox _gpuCombo = new();
    private readonly NumericUpDown _aiStrength = new();
    private readonly NumericUpDown _sharpen = new();
    private readonly Label _status = new();
    private readonly Label _engineStatus = new();
    private readonly Button _processButton = new();
    private readonly Button _saveButton = new();
    private readonly Button _engineButton = new();
    private readonly AppSettings _settings = AppSettings.Load();

    private Bitmap? _original;
    private Bitmap? _result;
    private string? _sourcePath;
    private bool _loadingGpuList;

    public MainForm()
    {
        Text = "DK Image Simple Upscaler";
        MinimumSize = new Size(820, 600);
        Size = new Size(1400, 860);
        StartPosition = FormStartPosition.CenterScreen;
        AllowDrop = true;
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;

        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        BuildUi();
        WirePreviewSync();
        UpdateModeUi();
        UpdateEngineStatus();
    }

    private void BuildUi()
    {
        var openButton = new Button { Text = "이미지 열기", AutoSize = true };
        openButton.Click += (_, _) => OpenImage();

        ConfigureCombo(_modeCombo, 138, ["Text/UI Safe", "AI General", "AI Anime"]);
        _modeCombo.SelectedIndexChanged += (_, _) => UpdateModeUi();

        ConfigureCombo(_sizeCombo, 92, ["2×", "3×", "4×", "FHD", "QHD", "4K"]);
        ConfigureCombo(_methodCombo, 112, ["Lanczos 3", "Bicubic", "Nearest"]);
        ConfigureCombo(_tileCombo, 78, ["Auto", "128", "256", "512"]);

        _gpuCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _gpuCombo.Width = 260;
        _gpuCombo.Items.Add(new GpuChoice(null, "Auto (Real-ESRGAN 기본)"));
        _gpuCombo.SelectedIndex = 0;
        _gpuCombo.SelectedIndexChanged += (_, _) => SaveGpuSelection();

        ConfigureNumeric(_aiStrength, 0, 100, 85, 64);
        ConfigureNumeric(_sharpen, 0, 100, 15, 64);

        _processButton.Text = "업스케일";
        _processButton.AutoSize = true;
        _processButton.Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold);
        _processButton.Click += async (_, _) => await ProcessAsync();

        _saveButton.Text = "결과 저장";
        _saveButton.AutoSize = true;
        _saveButton.Enabled = false;
        _saveButton.Click += (_, _) => SaveResult();

        _engineButton.Text = "AI 엔진 설치";
        _engineButton.AutoSize = true;
        _engineButton.Click += async (_, _) => await InstallEngineAsync();

        _engineStatus.AutoSize = true;
        _engineStatus.TextAlign = ContentAlignment.MiddleLeft;
        _engineStatus.Margin = new Padding(8, 8, 4, 0);

        var primaryRow = CreateToolbarRow();
        primaryRow.Controls.Add(openButton);
        primaryRow.Controls.Add(CreateSeparator());
        AddField(primaryRow, "모드", _modeCombo);
        AddField(primaryRow, "크기", _sizeCombo);
        primaryRow.Controls.Add(_processButton);
        primaryRow.Controls.Add(_saveButton);

        var optionRow = CreateToolbarRow();
        AddField(optionRow, "방식", _methodCombo);
        AddField(optionRow, "샤픈", _sharpen);
        optionRow.Controls.Add(CreateSeparator());
        AddField(optionRow, "AI 강도", _aiStrength);
        AddField(optionRow, "GPU", _gpuCombo);
        AddField(optionRow, "Tile", _tileCombo);
        optionRow.Controls.Add(_engineButton);
        optionRow.Controls.Add(_engineStatus);

        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(8, 7, 8, 5),
            Margin = Padding.Empty
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        toolbar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        toolbar.Controls.Add(primaryRow, 0, 0);
        toolbar.Controls.Add(optionRow, 0, 1);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 6
        };

        Shown += async (_, _) =>
        {
            ConfigureSplitterAfterLayout(split);
            await RefreshGpuListAsync();
        };
        split.Resize += (_, _) =>
        {
            if (split.Width <= 0) return;
            int minimumTotal = split.Panel1MinSize + split.Panel2MinSize + split.SplitterWidth;
            if (!split.IsSplitterFixed && split.Width > minimumTotal)
            {
                double ratio = split.SplitterDistance / (double)Math.Max(1, split.Width - split.SplitterWidth);
                if (ratio < 0.25 || ratio > 0.75) CenterSplitter(split);
            }
        };

        split.Panel1.Controls.Add(BuildPreviewPanel("원본", _originalView, _originalZoomLabel));
        split.Panel2.Controls.Add(BuildPreviewPanel("결과", _resultView, _resultZoomLabel));

        _status.Dock = DockStyle.Bottom;
        _status.Height = 30;
        _status.Padding = new Padding(10, 5, 10, 0);
        _status.AutoEllipsis = true;
        _status.Text = "이미지를 열거나 창에 드래그하세요. 휠: 줌 · 드래그: 이동 · 더블클릭: Fit";

        Controls.Add(split);
        Controls.Add(_status);
        Controls.Add(toolbar);
    }

    private static FlowLayoutPanel CreateToolbarRow() => new()
    {
        Dock = DockStyle.Fill,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        FlowDirection = FlowDirection.LeftToRight,
        WrapContents = true,
        Margin = Padding.Empty,
        Padding = new Padding(0, 2, 0, 2)
    };

    private static void ConfigureCombo(ComboBox combo, int width, object[] items)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.Width = width;
        combo.Margin = new Padding(3, 3, 8, 3);
        combo.Items.AddRange(items);
        combo.SelectedIndex = 0;
    }

    private static void ConfigureNumeric(NumericUpDown numeric, decimal min, decimal max, decimal value, int width)
    {
        numeric.Minimum = min;
        numeric.Maximum = max;
        numeric.Value = value;
        numeric.Width = width;
        numeric.Margin = new Padding(3, 3, 8, 3);
    }

    private static void AddField(FlowLayoutPanel row, string labelText, Control control)
    {
        row.Controls.Add(new Label
        {
            Text = labelText,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(7, 8, 1, 0)
        });
        row.Controls.Add(control);
    }

    private static Control CreateSeparator() => new Label
    {
        Text = "│",
        AutoSize = true,
        ForeColor = SystemColors.ControlDark,
        Margin = new Padding(4, 8, 4, 0)
    };

    private static void ConfigureSplitterAfterLayout(SplitContainer split)
    {
        int available = split.Width - split.SplitterWidth;
        if (available <= 0) return;

        const int desiredMin = 260;
        int safeMin = Math.Min(desiredMin, Math.Max(0, (available - 2) / 2));
        split.Panel1MinSize = safeMin;
        split.Panel2MinSize = safeMin;
        CenterSplitter(split);
    }

    private static void CenterSplitter(SplitContainer split)
    {
        int available = split.Width - split.SplitterWidth;
        if (available <= 0) return;

        int min = split.Panel1MinSize;
        int max = available - split.Panel2MinSize;
        if (max < min) return;

        int target = Math.Clamp(available / 2, min, max);
        if (target >= min && target <= max)
            split.SplitterDistance = target;
    }

    private void WirePreviewSync()
    {
        _originalView.ViewChanged += (_, _) => SyncView(_originalView, _resultView);
        _resultView.ViewChanged += (_, _) => SyncView(_resultView, _originalView);
        UpdateZoomLabels();
    }

    private void SyncView(ZoomPanViewer source, ZoomPanViewer target)
    {
        target.ApplyViewState(source.ViewState);
        UpdateZoomLabels();
    }

    private void UpdateZoomLabels()
    {
        _originalZoomLabel.Text = $"{_originalView.ZoomPercent}%";
        _resultZoomLabel.Text = $"{_resultView.ZoomPercent}%";
    }

    private static Control BuildPreviewPanel(string title, ZoomPanViewer viewer, Label zoomLabel)
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        var header = new Panel { Dock = DockStyle.Top, Height = 30 };
        var label = new Label
        {
            Text = title,
            Dock = DockStyle.Left,
            AutoSize = false,
            Width = 120,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold)
        };
        zoomLabel.Dock = DockStyle.Right;
        zoomLabel.Width = 80;
        zoomLabel.TextAlign = ContentAlignment.MiddleRight;
        zoomLabel.ForeColor = SystemColors.GrayText;
        header.Controls.Add(label);
        header.Controls.Add(zoomLabel);

        viewer.Dock = DockStyle.Fill;
        panel.Controls.Add(viewer);
        panel.Controls.Add(header);
        return panel;
    }

    private void UpdateModeUi()
    {
        bool aiMode = _modeCombo.SelectedIndex > 0;
        _methodCombo.Enabled = !aiMode;
        _aiStrength.Enabled = aiMode;
        _tileCombo.Enabled = aiMode;
        _gpuCombo.Enabled = aiMode && AiEngineManager.IsInstalled && _gpuCombo.Items.Count > 0;
        _engineButton.Visible = aiMode || !AiEngineManager.IsInstalled;
        _sharpen.Value = aiMode ? 5 : 15;
    }

    private void UpdateEngineStatus()
    {
        bool installed = AiEngineManager.IsInstalled;
        _engineStatus.Text = installed ? "AI: 설치됨" : "AI: 미설치";
        _engineStatus.ForeColor = installed ? Color.DarkGreen : Color.DimGray;
        _engineButton.Text = installed ? "AI 엔진 재설치" : "AI 엔진 설치";
        _gpuCombo.Enabled = installed && _modeCombo.SelectedIndex > 0;
    }

    private async Task RefreshGpuListAsync()
    {
        _loadingGpuList = true;
        try
        {
            _gpuCombo.Items.Clear();
            _gpuCombo.Items.Add(new GpuChoice(null, "Auto (Real-ESRGAN 기본)"));
            _gpuCombo.SelectedIndex = 0;

            if (!AiEngineManager.IsInstalled)
            {
                _gpuCombo.Enabled = false;
                return;
            }

            _gpuCombo.Enabled = false;
            _status.Text = "Vulkan GPU 검색 중…";
            IReadOnlyList<GpuDeviceInfo> gpus = await AiEngineManager.DetectGpusAsync();
            foreach (var gpu in gpus)
                _gpuCombo.Items.Add(new GpuChoice(gpu.Id, gpu.ToString(), gpu.Name));

            int restoreIndex = 0;
            if (_settings.GpuId.HasValue)
            {
                for (int i = 1; i < _gpuCombo.Items.Count; i++)
                {
                    if (_gpuCombo.Items[i] is GpuChoice choice && choice.Id == _settings.GpuId &&
                        (string.IsNullOrWhiteSpace(_settings.GpuName) || choice.DeviceName == _settings.GpuName))
                    {
                        restoreIndex = i;
                        break;
                    }
                }
            }
            _gpuCombo.SelectedIndex = restoreIndex;
            _status.Text = gpus.Count > 0
                ? $"GPU {gpus.Count}개 감지 · 선택: {_gpuCombo.SelectedItem}"
                : "Vulkan GPU를 자동 감지하지 못했습니다. Auto로 실행합니다.";
        }
        catch (Exception ex)
        {
            _gpuCombo.SelectedIndex = 0;
            _status.Text = $"GPU 검색 실패 · Auto 사용 · {ex.Message}";
        }
        finally
        {
            _loadingGpuList = false;
            _gpuCombo.Enabled = AiEngineManager.IsInstalled && _modeCombo.SelectedIndex > 0;
        }
    }

    private void SaveGpuSelection()
    {
        if (_loadingGpuList || _gpuCombo.SelectedItem is not GpuChoice choice) return;
        _settings.GpuId = choice.Id;
        _settings.GpuName = choice.DeviceName;
        try { _settings.Save(); } catch { }
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
            await RefreshGpuListAsync();
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
            _originalView.Image = _original;
            _resultView.Image = null;
            _originalView.Fit(false);
            _resultView.Fit(false);
            UpdateZoomLabels();
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
        var gpuChoice = _gpuCombo.SelectedItem as GpuChoice ?? new GpuChoice(null, "Auto (Real-ESRGAN 기본)");
        string gpuText = gpuChoice.Id.HasValue ? gpuChoice.DeviceName ?? $"GPU {gpuChoice.Id}" : "Auto";

        SetBusy(true, aiMode
            ? $"처리 중… {_original.Width}×{_original.Height} → {width}×{height} · GPU: {gpuText}"
            : $"처리 중… {_original.Width}×{_original.Height} → {width}×{height}");
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
                var progress = new Progress<string>(text => _status.Text = $"{text} · GPU: {gpuText}");

                using var ai4x = await AiEngineManager.UpscaleAsync(source, model, tile, gpuChoice.Id, progress);
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
            _resultView.Image = _result;
            _resultView.ApplyViewState(_originalView.ViewState);
            UpdateZoomLabels();
            _saveButton.Enabled = true;
            string modeText = _modeCombo.SelectedItem?.ToString() ?? "Text/UI Safe";
            _status.Text = aiMode
                ? $"완료 · {width}×{height} · {modeText} · GPU: {gpuText} · 샤픈 {sharpen}"
                : $"완료 · {width}×{height} · {modeText} · 샤픈 {sharpen}";
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
        _status.Text = $"저장 완료 · {dialog.FileName}";
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
        _gpuCombo.Enabled = !busy && _modeCombo.SelectedIndex > 0 && AiEngineManager.IsInstalled;
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
