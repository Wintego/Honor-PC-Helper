using System.Globalization;
using System.Management;
using System.Drawing.Drawing2D;
using Microsoft.Win32;

namespace HonorPCHelper;

internal sealed class DriverManagerForm : Form
{
    private sealed record DriverRow(DriverComponent Component, Label Title, Label Date, LinkLabel Version);
    private sealed record UpdateCheck(DriverCheckResult Drivers, ApplicationUpdateCheck? Application);
    private static readonly Lock UpdateCheckLock = new();
    private static Task<UpdateCheck>? _firstUpdateCheckTask;
    private const string SymbolUpdated = "•";
    private const string SymbolNew = "●";
    private readonly DriverUpdateService _service = new();
    private readonly ApplicationUpdateService _applicationUpdateService = new();
    private readonly Panel _content = new();
    private readonly TableLayoutPanel _biosTable = CreateUpdateTable();
    private readonly TableLayoutPanel _driversTable = CreateUpdateTable();
    private readonly Label _updatesLabel = new();
    private readonly Button _refreshButton = new();
    private readonly TableLayoutPanel _legend = new();
    private readonly ToolTip _toolTip = new();
    private readonly ProgressBar _progress = new();
    private readonly TextBox _serialNumber = new();
    private readonly Font _linkFont;
    private readonly Font _newLinkFont;
    private readonly bool _dark = IsDarkTheme();
    private readonly Color _eco = Color.FromArgb(0, 184, 148);
    private readonly Color _turbo = Color.FromArgb(255, 45, 45);
    private readonly Color _background;
    private readonly Color _foreground;
    private readonly Color _muted;
    private CancellationTokenSource? _scanCancellation;
    private Task<IReadOnlyList<DriverComponent>>? _initialComponentsTask;
    private IReadOnlyList<DriverComponent> _components = [];
    private readonly Dictionary<int, DriverRow> _driverRows = [];
    private LinkLabel? _applicationVersion;

    internal DriverManagerForm(Task<IReadOnlyList<DriverComponent>>? initialComponentsTask = null)
    {
        _initialComponentsTask = initialComponentsTask;
        _background = _dark ? Color.FromArgb(28, 28, 28) : Color.White;
        _foreground = _dark ? Color.WhiteSmoke : Color.FromArgb(30, 30, 30);
        _muted = _dark ? Color.FromArgb(135, 135, 135) : Color.Gray;
        _linkFont = new Font("Segoe UI", 9F, FontStyle.Underline);
        _newLinkFont = new Font("Segoe UI", 9F, FontStyle.Bold | FontStyle.Underline);

        Text = L.T("BIOS и обновления драйверов", "BIOS and Driver Updates", "BIOS 和驱动程序更新");
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1050, 700);
        Size = new Size(1280, 1050);
        Opacity = 0;
        AutoScaleMode = AutoScaleMode.Dpi;
        ShowIcon = false;
        Font = new Font("Segoe UI", 9F);
        BackColor = _background;
        ForeColor = _foreground;

        var biosTitle = CreateTitle("BIOS", true);
        var biosPanel = CreatePanel(_biosTable);
        var driversTitle = CreateTitle(L.T("Драйверы и программы", "Drivers and Software", "驱动程序和软件"), false);
        var driversPanel = CreatePanel(_driversTable);
        ConfigureLegend();

        _content.Dock = DockStyle.Fill;
        _content.AutoScroll = false;
        _content.BackColor = _background;
        _content.Controls.Add(driversPanel);
        _content.Controls.Add(driversTitle);
        _content.Controls.Add(biosPanel);
        _content.Controls.Add(biosTitle);

        _progress.Dock = DockStyle.Bottom;
        _progress.Height = 3;
        _progress.Style = ProgressBarStyle.Marquee;
        _progress.Visible = false;
        Controls.Add(_content);
        Controls.Add(_progress);
        Controls.Add(_legend);

        _refreshButton.Click += async (_, _) => await ScanAsync(force: true);
        Shown += (_, _) =>
        {
            Opacity = 1;
            BeginInvoke(async () => await InitializeAsync());
        };
        FormClosed += (_, _) =>
        {
            _scanCancellation?.Cancel();
            _scanCancellation?.Dispose();
            _linkFont.Dispose();
            _newLinkFont.Dispose();
            _toolTip.Dispose();
        };
    }

    private Panel CreateTitle(string title, bool includeRefresh)
    {
        var panel = new Panel { Dock = DockStyle.Top, Height = includeRefresh ? 60 : 52, BackColor = _background };
        var iconPanel = new Panel
        {
            Size = new Size(46, 46),
            Location = new Point(12, includeRefresh ? 7 : 3),
            BackColor = _background
        };
        iconPanel.Paint += (_, e) => DrawTitleIcon(e.Graphics, iconPanel.ClientRectangle, includeRefresh);
        panel.Controls.Add(iconPanel);
        panel.Controls.Add(new Label
        {
            Text = title,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(68, includeRefresh ? 20 : 14)
        });
        if (!includeRefresh) return panel;

        _updatesLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _updatesLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _updatesLabel.TextAlign = ContentAlignment.MiddleRight;
        _updatesLabel.Size = new Size(360, 36);
        _updatesLabel.ForeColor = _eco;
        _updatesLabel.AutoEllipsis = false;
        _refreshButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _refreshButton.Text = string.Empty;
        _refreshButton.TextAlign = ContentAlignment.MiddleCenter;
        _refreshButton.Padding = Padding.Empty;
        _refreshButton.FlatStyle = FlatStyle.Flat;
        _refreshButton.FlatAppearance.BorderSize = 0;
        _refreshButton.BackColor = _dark ? Color.FromArgb(45, 45, 45) : Color.FromArgb(235, 235, 235);
        _refreshButton.ForeColor = _foreground;
        _refreshButton.Size = new Size(52, 46);
        _refreshButton.Paint += (_, e) => DrawRefreshIcon(e.Graphics, _refreshButton.ClientRectangle,
            _refreshButton.Enabled ? _foreground : _muted);
        _toolTip.SetToolTip(_refreshButton, L.T("Обновить список", "Refresh list", "刷新列表"));
        panel.Controls.Add(_updatesLabel);
        panel.Controls.Add(_refreshButton);
        void Align()
        {
            _refreshButton.Location = new Point(panel.ClientSize.Width - _refreshButton.Width - 12, 4);
            _updatesLabel.Location = new Point(_refreshButton.Left - _updatesLabel.Width - 10, 9);
        }
        panel.Resize += (_, _) => Align();
        Align();
        return panel;
    }

    private Panel CreatePanel(TableLayoutPanel table)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(14, 4, 14, 12),
            BackColor = _background
        };
        table.BackColor = _background;
        table.ForeColor = _foreground;
        panel.Controls.Add(table);
        return panel;
    }

    private void ConfigureLegend()
    {
        _legend.Dock = DockStyle.Bottom;
        var legendRowHeight = Math.Max(50, Font.Height + 24);
        _legend.Height = legendRowHeight * 2 + 28;
        _legend.Padding = new Padding(14, 8, 14, 14);
        _legend.ColumnCount = 4;
        _legend.RowCount = 2;
        _legend.BackColor = _background;
        _legend.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16));
        _legend.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        _legend.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        _legend.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        _legend.RowStyles.Add(new RowStyle(SizeType.Absolute, legendRowHeight));
        _legend.RowStyles.Add(new RowStyle(SizeType.Absolute, legendRowHeight));
        _legend.Controls.Add(LegendLabel(L.T("Серийный номер", "Serial", "序列号"), true), 0, 0);
        _serialNumber.Text = "...";
        _serialNumber.ReadOnly = true;
        _serialNumber.Dock = DockStyle.Fill;
        _serialNumber.BorderStyle = BorderStyle.FixedSingle;
        _serialNumber.BackColor = _background;
        _serialNumber.ForeColor = _foreground;
        _serialNumber.Margin = new Padding(4);
        _legend.Controls.Add(_serialNumber, 1, 0);
        _legend.Controls.Add(LegendLabel(L.T("Легенда", "Legend", "图例"), true), 0, 1);
        _legend.Controls.Add(LegendLabel(L.T("Версия не определена", "Can't check local version", "无法检查本地版本"), false, _muted), 1, 1);
        _legend.Controls.Add(LegendLabel(L.T("Актуально", "Updated", "已更新"), false, _eco), 2, 1);
        _legend.Controls.Add(LegendLabel(L.T("Доступно обновление", "Update Available", "有可用更新"), false, _turbo), 3, 1);
    }

    private Label LegendLabel(string text, bool bold, Color? backColor = null) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        AutoEllipsis = true,
        TextAlign = ContentAlignment.MiddleLeft,
        Font = new Font("Segoe UI", 8.5F, bold ? FontStyle.Bold : FontStyle.Regular),
        BackColor = backColor ?? _background,
        ForeColor = backColor is null ? _foreground : Color.White,
        Padding = new Padding(5, 0, 5, 0),
        Margin = new Padding(4)
    };

    private async Task ScanAsync(bool force)
    {
        _scanCancellation?.Cancel();
        _scanCancellation?.Dispose();
        _scanCancellation = new CancellationTokenSource();
        var cancellation = _scanCancellation.Token;
        SetBusy(true);
        _updatesLabel.Text = L.T("Проверка…", "Checking…", "正在检查…");
        _updatesLabel.ForeColor = _eco;
        try
        {
            var checkTask = GetUpdateCheckTask(force, _components);
            var check = await checkTask.WaitAsync(cancellation);
            cancellation.ThrowIfCancellationRequested();
            var result = check.Drivers;
            var applicationCheck = check.Application;
            if (!result.IsComplete)
            {
                _updatesLabel.Text = L.T("Не удалось получить список драйверов", "Could not retrieve driver list", "无法获取驱动程序列表");
                _updatesLabel.ForeColor = _turbo;
                return;
            }
            _components = result.Components;
            ApplyUpdates(result.Updates, applicationCheck);

            var count = result.Updates.Count(update => update.IsUpdate
                    && _driverRows.ContainsKey(update.Component.Id))
                + (applicationCheck?.Update is null ? 0 : 1);
            _updatesLabel.Text = count == 0
                ? L.T("Новых обновлений нет", "No new updates", "没有新更新")
                : L.T($"Новых обновлений: {count}", $"New updates: {count}", $"新更新：{count}");
            _updatesLabel.ForeColor = count == 0 ? _eco : _turbo;
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            AppLog.Error("Driver update check failed", exception);
            _updatesLabel.Text = L.T("Ошибка проверки", "Check failed", "检查失败");
            _updatesLabel.ForeColor = _turbo;
        }
        finally { SetBusy(false); }
    }

    private async Task InitializeAsync()
    {
        _ = LoadSerialNumberAsync();
        await LoadInitialListAsync();
        if (!IsDisposed)
            await ScanAsync(force: false);
    }

    private Task<UpdateCheck> GetUpdateCheckTask(
        bool force,
        IReadOnlyList<DriverComponent> components)
    {
        lock (UpdateCheckLock)
        {
            if (!force && _firstUpdateCheckTask is not null)
                return _firstUpdateCheckTask;
            _firstUpdateCheckTask = RunUpdateCheckAsync(components);
            return _firstUpdateCheckTask;
        }
    }

    private async Task<UpdateCheck> RunUpdateCheckAsync(IReadOnlyList<DriverComponent> components)
    {
        var driverTask = Task.Run(() => _service.CheckAsync(components));
        var applicationTask = Task.Run(() => _applicationUpdateService.CheckAsync());
        ApplicationUpdateCheck? application = null;
        try { application = await applicationTask; }
        catch (Exception exception) { AppLog.Error("Application update check failed", exception); }
        return new UpdateCheck(await driverTask, application);
    }

    private async Task LoadInitialListAsync()
    {
        var initialTask = Interlocked.Exchange(ref _initialComponentsTask, null);
        try
        {
            _components = initialTask is null
                ? await _service.BuildDeviceListAsync()
                : await initialTask;
        }
        catch (Exception exception)
        {
            AppLog.Error("Initial driver device inventory failed; retrying", exception);
            _components = await _service.BuildDeviceListAsync();
        }
        RenderRows(_components, [], null);
        _updatesLabel.Text = L.T("Проверка…", "Checking…", "正在检查…");
        _updatesLabel.ForeColor = _eco;
    }

    private async Task LoadSerialNumberAsync()
    {
        try
        {
            var serialNumber = await Task.Run(ReadSerialNumber);
            if (!IsDisposed)
                _serialNumber.Text = serialNumber;
        }
        catch (Exception exception)
        {
            AppLog.Error("BIOS serial number lookup failed", exception);
            if (!IsDisposed)
                _serialNumber.Text = "—";
        }
    }

    private void RenderRows(
        IReadOnlyList<DriverComponent> components,
        IReadOnlyList<DriverUpdate> updates,
        ApplicationUpdateCheck? applicationCheck)
    {
        SuspendLayout();
        try
        {
            ClearTable(_biosTable);
            ClearTable(_driversTable);
            _driverRows.Clear();
            _applicationVersion = null;
            var updatesById = updates.GroupBy(update => update.Component.Id)
                .ToDictionary(group => group.Key, group => group.First());
            var bios = components.FirstOrDefault(component => component.Id == 23);
            if (bios is not null) AddRow(_biosTable, bios, updatesById.GetValueOrDefault(23));
            AddApplicationRow(_driversTable, applicationCheck);
            foreach (var component in components
                         .Where(c => c.Id > 0 && c.Id != 23 && c.Id is not 87 and not 88)
                         .OrderBy(Category).ThenBy(c => c.DisplayName))
                AddRow(_driversTable, component, updatesById.GetValueOrDefault(component.Id));
            Text = $"{L.T("BIOS и обновления драйверов", "BIOS and Driver Updates", "BIOS 和驱动程序更新")}: {ReadModel()} {bios?.CurrentVersion}";
            FitWindowToRows();
        }
        finally { ResumeLayout(true); }
    }

    private void AddRow(TableLayoutPanel table, DriverComponent component, DriverUpdate? update)
    {
        var row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, Math.Max(40, Font.Height + 16)));
        table.Controls.Add(Cell(Category(component)), 0, row);
        var title = Cell(update?.PackageTitle ?? component.DeviceName ?? ComponentTitle(component));
        var date = Cell(FormatDate(update?.ReleaseDate));
        table.Controls.Add(title, 1, row);
        table.Controls.Add(date, 2, row);
        var version = new LinkLabel
        {
            Text = DisplayVersion(update, component),
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(2, 3, 3, 2),
            Font = _linkFont,
            LinkColor = component.CurrentVersion == "0"
                ? _muted
                : update?.IsUpdate == true ? _turbo : _eco,
            ActiveLinkColor = component.CurrentVersion == "0"
                ? _muted
                : update?.IsUpdate == true ? _turbo : _eco,
            Cursor = update is null ? Cursors.Default : Cursors.Hand,
            LinkBehavior = update is null ? LinkBehavior.NeverUnderline : LinkBehavior.AlwaysUnderline
        };
        version.Tag = update;
        if (update is not null)
            _toolTip.SetToolTip(version, L.T(
                $"Нажмите, чтобы скачать драйвер версии {update.Version}.",
                $"Click to download driver version {update.Version}.",
                $"点击下载驱动程序版本 {update.Version}。"));
        version.LinkClicked += async (_, _) =>
        {
            if (version.Tag is DriverUpdate driverUpdate)
                await DownloadDriverAsync(driverUpdate, version);
        };
        table.Controls.Add(version, 4, row);
        _driverRows[component.Id] = new DriverRow(component, title, date, version);
    }

    private void AddApplicationRow(TableLayoutPanel table, ApplicationUpdateCheck? check)
    {
        var row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, Math.Max(40, Font.Height + 16)));
        table.Controls.Add(Cell(L.T("Программа", "Software", "软件")), 0, row);
        table.Controls.Add(Cell("Honor PC Helper"), 1, row);
        table.Controls.Add(Cell(string.Empty), 2, row);

        var update = check?.Update;
        var displayedVersion = update?.Version ?? check?.LatestVersion ?? _applicationUpdateService.CurrentVersion;
        var version = new LinkLabel
        {
            Text = displayedVersion.ToString(3),
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(2, 3, 3, 2),
            Font = _linkFont,
            LinkColor = update is null ? _eco : _turbo,
            ActiveLinkColor = update is null ? _eco : _turbo,
            Cursor = update is null ? Cursors.Default : Cursors.Hand,
            LinkBehavior = update is null ? LinkBehavior.NeverUnderline : LinkBehavior.AlwaysUnderline
        };
        version.Tag = update;
        version.LinkClicked += async (_, _) =>
        {
            if (version.Tag is ApplicationUpdate applicationUpdate)
                await DownloadApplicationUpdateAsync(applicationUpdate, version);
        };
        table.Controls.Add(version, 4, row);
        _applicationVersion = version;
    }

    private void ApplyUpdates(
        IReadOnlyList<DriverUpdate> updates,
        ApplicationUpdateCheck? applicationCheck)
    {
        _biosTable.SuspendLayout();
        _driversTable.SuspendLayout();
        try
        {
            foreach (var update in updates)
            {
                if (!_driverRows.TryGetValue(update.Component.Id, out var row)) continue;
                row.Title.Text = update.PackageTitle ?? row.Component.DeviceName ?? ComponentTitle(row.Component);
                row.Date.Text = FormatDate(update.ReleaseDate);
                row.Version.Text = DisplayVersion(update, row.Component);
                row.Version.LinkColor = row.Component.CurrentVersion == "0"
                    ? _muted
                    : update.IsUpdate ? _turbo : _eco;
                row.Version.ActiveLinkColor = row.Version.LinkColor;
                row.Version.Cursor = Cursors.Hand;
                row.Version.LinkBehavior = LinkBehavior.AlwaysUnderline;
                row.Version.Tag = update;
                var installedVersion = row.Component.CurrentVersion == "0" ? "—" : row.Component.CurrentVersion;
                _toolTip.SetToolTip(row.Version,
                    L.T($"Доступно: {update.Version}\nУстановлено: {installedVersion}",
                        $"Available: {update.Version}\nInstalled: {installedVersion}",
                        $"可用版本：{update.Version}\n已安装：{installedVersion}"));
            }

            if (_applicationVersion is null) return;
            var applicationUpdate = applicationCheck?.Update;
            var displayedVersion = applicationUpdate?.Version ?? applicationCheck?.LatestVersion
                ?? _applicationUpdateService.CurrentVersion;
            _applicationVersion.Text = displayedVersion.ToString(3);
            _applicationVersion.LinkColor = applicationUpdate is null ? _eco : _turbo;
            _applicationVersion.ActiveLinkColor = _applicationVersion.LinkColor;
            _applicationVersion.Cursor = applicationUpdate is null ? Cursors.Default : Cursors.Hand;
            _applicationVersion.LinkBehavior = applicationUpdate is null
                ? LinkBehavior.NeverUnderline : LinkBehavior.AlwaysUnderline;
            _applicationVersion.Tag = applicationUpdate;
            _toolTip.SetToolTip(_applicationVersion, applicationUpdate is null
                ? string.Empty
                : L.T(
                    $"Нажмите, чтобы скачать и установить Honor PC Helper {applicationUpdate.Version}.",
                    $"Click to download and install Honor PC Helper {applicationUpdate.Version}.",
                    $"点击下载并安装 Honor PC Helper {applicationUpdate.Version}。"));
        }
        finally
        {
            _driversTable.ResumeLayout(true);
            _biosTable.ResumeLayout(true);
        }
    }

    private async Task DownloadApplicationUpdateAsync(ApplicationUpdate update, LinkLabel link)
    {
        var versionText = link.Text;
        link.Enabled = false;
        link.Text = L.T("Загрузка... 0%", "Downloading... 0%", "正在下载... 0%");
        _progress.Style = ProgressBarStyle.Continuous;
        _progress.Value = 0;
        _progress.Visible = true;
        try
        {
            await _applicationUpdateService.DownloadAndRestartAsync(update, new Progress<int>(percent =>
            {
                percent = Math.Clamp(percent, _progress.Minimum, _progress.Maximum);
                _progress.Value = percent;
                link.Text = percent < 100
                    ? L.T($"Загрузка... {percent}%", $"Downloading... {percent}%", $"正在下载... {percent}%")
                    : L.T("Установка...", "Installing...", "正在安装...");
            }));
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            AppLog.Error("Application update download failed", exception);
            if (!IsDisposed)
                MessageBox.Show(this, exception.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            // После удачного обновления форма уже закрывается вместе с приложением.
            if (!IsDisposed)
            {
                _progress.Visible = false;
                _progress.Style = ProgressBarStyle.Marquee;
                link.Text = versionText;
                link.Enabled = true;
            }
        }
    }

    private async Task DownloadDriverAsync(DriverUpdate update, LinkLabel link)
    {
        var versionText = link.Text;
        link.Enabled = false;
        link.Text = L.T("Загрузка... 0%", "Downloading... 0%", "正在下载... 0%");
        _progress.Style = ProgressBarStyle.Continuous;
        _progress.Value = 0;
        _progress.Visible = true;
        try
        {
            await _service.DownloadAsync(update, new Progress<int>(percent =>
            {
                percent = Math.Clamp(percent, _progress.Minimum, _progress.Maximum);
                _progress.Value = percent;
                link.Text = percent < 100
                    ? L.T($"Загрузка... {percent}%", $"Downloading... {percent}%", $"正在下载... {percent}%")
                    : L.T("Проверка файла...", "Verifying file...", "正在验证文件...");
            }));
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            AppLog.Error("Driver download failed", exception);
            MessageBox.Show(this, exception.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _progress.Visible = false;
            _progress.Style = ProgressBarStyle.Marquee;
            link.Text = versionText;
            link.Enabled = true;
        }
    }

    private void SetBusy(bool busy)
    {
        _refreshButton.Enabled = !busy;
        _progress.Visible = busy;
    }

    private void DrawTitleIcon(Graphics graphics, Rectangle bounds, bool bios)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(_foreground, Math.Max(2F, bounds.Width / 18F))
        {
            StartCap = LineCap.Square,
            EndCap = LineCap.Square
        };
        var size = Math.Min(bounds.Width, bounds.Height);
        var left = bounds.Left + (bounds.Width - size) / 2F;
        var top = bounds.Top + (bounds.Height - size) / 2F;

        if (bios)
        {
            var body = new RectangleF(left + size * .28F, top + size * .28F, size * .44F, size * .44F);
            graphics.DrawRectangle(pen, body.X, body.Y, body.Width, body.Height);
            graphics.DrawRectangle(pen, left + size * .4F, top + size * .4F, size * .2F, size * .2F);
            for (var index = 0; index < 3; index++)
            {
                var offset = size * (.34F + index * .16F);
                graphics.DrawLine(pen, left + offset, top + size * .14F, left + offset, body.Top);
                graphics.DrawLine(pen, left + offset, body.Bottom, left + offset, top + size * .86F);
                graphics.DrawLine(pen, left + size * .14F, top + offset, body.Left, top + offset);
                graphics.DrawLine(pen, body.Right, top + offset, left + size * .86F, top + offset);
            }
            return;
        }

        var outer = new RectangleF(left + size * .2F, top + size * .2F, size * .6F, size * .6F);
        var inner = new RectangleF(left + size * .34F, top + size * .34F, size * .32F, size * .32F);
        graphics.DrawRectangle(pen, outer.X, outer.Y, outer.Width, outer.Height);
        graphics.DrawRectangle(pen, inner.X, inner.Y, inner.Width, inner.Height);
    }

    private static void DrawRefreshIcon(Graphics graphics, Rectangle bounds, Color color)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var size = Math.Min(bounds.Width, bounds.Height) * .48F;
        var left = bounds.Left + (bounds.Width - size) / 2F;
        var top = bounds.Top + (bounds.Height - size) / 2F;
        using var pen = new Pen(color, Math.Max(2F, size / 9F))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        graphics.DrawArc(pen, left, top, size, size, 35, 285);
        var tip = new PointF(left + size * .96F, top + size * .4F);
        graphics.DrawLine(pen, tip, new PointF(left + size * .72F, top + size * .27F));
        graphics.DrawLine(pen, tip, new PointF(left + size * .82F, top + size * .64F));
    }

    private Label Cell(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        AutoEllipsis = true,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = _foreground,
        Padding = new Padding(5, 3, 3, 2),
        Margin = Padding.Empty
    };

    private static TableLayoutPanel CreateUpdateTable()
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 5,
            RowCount = 0,
            Margin = Padding.Empty
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 15));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 39));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 0));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        return table;
    }

    private void FitWindowToRows()
    {
        var desiredClientHeight = _biosTable.PreferredSize.Height + _driversTable.PreferredSize.Height
            + 60 + 52 + _legend.Height + _progress.Height + 34;
        ClientSize = new Size(1260, Math.Max(680, desiredClientHeight));
        var area = Screen.FromControl(this).WorkingArea;
        Location = new Point(
            area.Left + (area.Width - Width) / 2,
            area.Top + (area.Height - Height) / 2);
    }

    private static void ClearTable(TableLayoutPanel table)
    {
        while (table.Controls.Count > 0) table.Controls[0].Dispose();
        table.RowCount = 0;
        table.RowStyles.Clear();
    }

    private static string Category(DriverComponent component) => component.Id switch
    {
        1 or 2 or 4 or 6 or 12 or 55 or 56 or 65 or 73 or 74 => L.T("Чипсет", "Chipset", "芯片组"),
        3 or 41 or 78 or 87 => L.T("Графика", "Graphics", "显卡"),
        14 => L.T("Аудио", "Audio", "音频"),
        15 => L.T("Сеть", "Networking", "网络"),
        16 => "Bluetooth",
        18 => L.T("Сканер отпечатка", "Fingerprint", "指纹识别"),
        52 => "NFC",
        76 => L.T("Камера", "Camera", "摄像头"),
        88 => L.T("Программы и утилиты", "Software and utilities", "软件和实用工具"),
        23 => "BIOS",
        _ => component.DisplayName
    };

    private static string ComponentTitle(DriverComponent component) => component.Id switch
    {
        1 => L.T("Чипсет", "Chipset", "芯片组"),
        2 => "Intel Management Engine",
        3 => L.T("Графика", "Graphics", "显卡"),
        4 => "Serial IO",
        6 => L.T("Платформенная служба", "Platform framework", "平台框架"),
        12 => L.T("Сторожевой таймер", "Watchdog", "看门狗"),
        14 => L.T("Аудио", "Audio", "音频"),
        15 => "Wi-Fi",
        16 => "Bluetooth",
        18 => L.T("Сканер отпечатка", "Fingerprint", "指纹识别"),
        23 => "BIOS",
        41 => L.T("Монитор", "Monitor", "显示器"),
        52 => "NFC",
        55 => "Intel PPM",
        56 => "Intel TXT",
        65 => "Intel Smart Sound",
        73 => "Intel PMT",
        74 => "Intel NPU",
        76 => L.T("Камера", "Camera", "摄像头"),
        78 => "Windows Studio Effects",
        87 => L.T("Виртуальный дисплей", "Virtual Display", "虚拟显示器"),
        88 => L.T("Виртуальное HID-устройство", "Virtual HID", "虚拟 HID 设备"),
        _ => component.DisplayName
    };

    private static string FormatDate(string? value)
        => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date)
            ? date.ToString("d", CultureInfo.CurrentCulture)
            : string.Empty;

    private static string DisplayVersion(DriverUpdate? update, DriverComponent component)
        => update is null || !update.IsUpdate && component.CurrentVersion != "0"
            ? component.CurrentVersion == "0" ? "—" : component.CurrentVersion
            : update.Version;

    private static string ReadModel() => Convert.ToString(Registry.GetValue(
        @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\BIOS", "SystemProductName", "HONOR")) ?? "HONOR";

    private static string ReadSerialNumber()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BIOS");
            using var results = searcher.Get();
            return results.Cast<ManagementObject>().Select(i => Convert.ToString(i["SerialNumber"]))
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "—";
        }
        catch { return "—"; }
    }

    private static bool IsDarkTheme() => Convert.ToInt32(Registry.GetValue(
        @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
        "AppsUseLightTheme", 1)) == 0;
}
