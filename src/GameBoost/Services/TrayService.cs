using GameBoost.Infrastructure;
using Drawing = System.Drawing;
using Drawing2D = System.Drawing.Drawing2D;
using WinForms = System.Windows.Forms;

namespace GameBoost.Services;

/// <summary>Symbol im Infobereich (System-Tray) mit Kontextmenü.</summary>
public sealed class TrayService : IDisposable
{
    private WinForms.NotifyIcon? _icon;
    private WinForms.ContextMenuStrip? _menu;
    private WinForms.ToolStripMenuItem? _openItem, _gameModeItem, _crosshairItem, _nextPresetItem, _exitItem;
    private Drawing.Icon? _idleIcon, _activeIcon;
    private IntPtr _idleHandle, _activeHandle;
    private bool _gameModeActive, _crosshairVisible;
    private string? _profileName;

    public event EventHandler? OpenRequested;
    public event EventHandler? ToggleGameModeRequested;
    public event EventHandler? ToggleCrosshairRequested;
    public event EventHandler? NextPresetRequested;
    public event EventHandler? ExitRequested;

    public void Initialize()
    {
        _idleIcon = CreateIcon(active: false, out _idleHandle);
        _activeIcon = CreateIcon(active: true, out _activeHandle);

        _menu = new WinForms.ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            ShowImageMargin = false,
            ShowCheckMargin = true,
            Font = new Drawing.Font("Segoe UI", 9.5f)
        };

        _openItem = new WinForms.ToolStripMenuItem("", null, (_, _) => Raise(OpenRequested)) { Font = new Drawing.Font("Segoe UI", 9.5f, Drawing.FontStyle.Bold) };
        _gameModeItem = new WinForms.ToolStripMenuItem("", null, (_, _) => Raise(ToggleGameModeRequested));
        _crosshairItem = new WinForms.ToolStripMenuItem("", null, (_, _) => Raise(ToggleCrosshairRequested));
        _nextPresetItem = new WinForms.ToolStripMenuItem("", null, (_, _) => Raise(NextPresetRequested));
        _exitItem = new WinForms.ToolStripMenuItem("", null, (_, _) => Raise(ExitRequested));

        _menu.Items.Add(_openItem);
        _menu.Items.Add(new WinForms.ToolStripSeparator());
        _menu.Items.Add(_gameModeItem);
        _menu.Items.Add(_crosshairItem);
        _menu.Items.Add(_nextPresetItem);
        _menu.Items.Add(new WinForms.ToolStripSeparator());
        _menu.Items.Add(_exitItem);

        _icon = new WinForms.NotifyIcon
        {
            Icon = _idleIcon,
            ContextMenuStrip = _menu,
            Visible = true,
            Text = "GameBoost"
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left) Raise(OpenRequested);
        };

        RefreshTexts();
    }

    public void UpdateState(bool gameModeActive, bool crosshairVisible, string? profileName)
    {
        _gameModeActive = gameModeActive;
        _crosshairVisible = crosshairVisible;
        _profileName = profileName;
        RefreshTexts();
    }

    public void RefreshTexts()
    {
        try
        {
            if (_icon == null) return;
            _openItem!.Text = Loc.T("Tray.Open");
            _gameModeItem!.Text = Loc.T("Tray.GameMode");
            _gameModeItem.Checked = _gameModeActive;
            _crosshairItem!.Text = Loc.T("Tray.Crosshair");
            _crosshairItem.Checked = _crosshairVisible;
            _nextPresetItem!.Text = Loc.T("Tray.NextPreset");
            _exitItem!.Text = Loc.T("Tray.Exit");

            _icon.Icon = _gameModeActive ? _activeIcon : _idleIcon;
            var text = _gameModeActive ? Loc.F("Tray.Tooltip.Active", _profileName ?? "") : Loc.T("Tray.Tooltip.Idle");
            _icon.Text = text.Length > 120 ? text[..120] : text;
        }
        catch (Exception ex)
        {
            Logger.Warn("Tray konnte nicht aktualisiert werden", ex);
        }
    }

    public void ShowBalloon(string title, string text)
    {
        try
        {
            _icon?.ShowBalloonTip(4000, title, text, WinForms.ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            Logger.Warn("Tray-Hinweis konnte nicht angezeigt werden", ex);
        }
    }

    private static void Raise(EventHandler? handler)
    {
        try
        {
            handler?.Invoke(null, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Logger.Error("Fehler bei Tray-Aktion", ex);
        }
    }

    /// <summary>Zeichnet das Tray-Symbol (Fadenkreuz; grün = Gaming-Modus aktiv).</summary>
    private static Drawing.Icon CreateIcon(bool active, out IntPtr handle)
    {
        using var bmp = new Drawing.Bitmap(32, 32, Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Drawing.Color.Transparent);

            using (var path = RoundedRect(new Drawing.RectangleF(1, 1, 30, 30), 8))
            using (var bg = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 16, 21, 33)))
            {
                g.FillPath(bg, path);
            }

            var accent = active ? Drawing.Color.FromArgb(0, 240, 168) : Drawing.Color.FromArgb(0, 194, 255);
            using var pen = new Drawing.Pen(accent, 2.6f);
            g.DrawEllipse(pen, 8.5f, 8.5f, 15f, 15f);
            g.DrawLine(pen, 16f, 4f, 16f, 11f);
            g.DrawLine(pen, 16f, 21f, 16f, 28f);
            g.DrawLine(pen, 4f, 16f, 11f, 16f);
            g.DrawLine(pen, 21f, 16f, 28f, 16f);
            using var dot = new Drawing.SolidBrush(accent);
            g.FillEllipse(dot, 14.25f, 14.25f, 3.5f, 3.5f);
        }

        handle = bmp.GetHicon();
        return Drawing.Icon.FromHandle(handle);
    }

    private static Drawing2D.GraphicsPath RoundedRect(Drawing.RectangleF r, float radius)
    {
        var d = radius * 2;
        var path = new Drawing2D.GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public void Dispose()
    {
        try
        {
            if (_icon != null)
            {
                _icon.Visible = false;
                _icon.Dispose();
            }
            _menu?.Dispose();
            _idleIcon?.Dispose();
            _activeIcon?.Dispose();
            if (_idleHandle != IntPtr.Zero) NativeMethods.DestroyIcon(_idleHandle);
            if (_activeHandle != IntPtr.Zero) NativeMethods.DestroyIcon(_activeHandle);
        }
        catch (Exception ex)
        {
            Logger.Warn("Tray konnte nicht freigegeben werden", ex);
        }
        _icon = null;
    }

    /// <summary>Dunkles Design für das Tray-Kontextmenü.</summary>
    private sealed class DarkMenuRenderer : WinForms.ToolStripProfessionalRenderer
    {
        private static readonly Drawing.Color Text = Drawing.Color.FromArgb(232, 236, 244);
        private static readonly Drawing.Color Accent = Drawing.Color.FromArgb(0, 240, 168);

        public DarkMenuRenderer() : base(new DarkColors())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderItemText(WinForms.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Text : Drawing.Color.Gray;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(WinForms.ToolStripItemImageRenderEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;
            var r = e.ImageRectangle;
            using var pen = new Drawing.Pen(Accent, 2f);
            g.DrawLines(pen, new[]
            {
                new Drawing.PointF(r.Left + r.Width * 0.18f, r.Top + r.Height * 0.52f),
                new Drawing.PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.75f),
                new Drawing.PointF(r.Left + r.Width * 0.85f, r.Top + r.Height * 0.25f)
            });
        }
    }

    private sealed class DarkColors : WinForms.ProfessionalColorTable
    {
        private static readonly Drawing.Color Bg = Drawing.Color.FromArgb(20, 26, 38);
        private static readonly Drawing.Color Hover = Drawing.Color.FromArgb(35, 44, 64);
        private static readonly Drawing.Color Border = Drawing.Color.FromArgb(47, 58, 80);

        public override Drawing.Color ToolStripDropDownBackground => Bg;
        public override Drawing.Color ImageMarginGradientBegin => Bg;
        public override Drawing.Color ImageMarginGradientMiddle => Bg;
        public override Drawing.Color ImageMarginGradientEnd => Bg;
        public override Drawing.Color MenuBorder => Border;
        public override Drawing.Color MenuItemBorder => Hover;
        public override Drawing.Color MenuItemSelected => Hover;
        public override Drawing.Color MenuItemSelectedGradientBegin => Hover;
        public override Drawing.Color MenuItemSelectedGradientEnd => Hover;
        public override Drawing.Color MenuItemPressedGradientBegin => Hover;
        public override Drawing.Color MenuItemPressedGradientEnd => Hover;
        public override Drawing.Color SeparatorDark => Border;
        public override Drawing.Color SeparatorLight => Bg;
        public override Drawing.Color CheckBackground => Bg;
        public override Drawing.Color CheckSelectedBackground => Hover;
        public override Drawing.Color CheckPressedBackground => Hover;
    }
}
