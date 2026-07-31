namespace HonorPCHelper;

/// <summary>
/// Небольшое окно захвата: показывает подсказку "Введите новое сочетание клавиш"
/// и запоминает первое нажатие с модификатором. Esc - отмена, Del - отключить.
/// </summary>
internal sealed class HotkeyCaptureForm : Form
{
    private const int VkLeftWin = 0x5B;
    private const int VkRightWin = 0x5C;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;

    private readonly Label _valueLabel;
    private readonly Label _hintLabel;
    private readonly string _defaultHint;

    internal HotkeyBinding Result { get; private set; }

    internal HotkeyCaptureForm(HotkeyAction action, HotkeyBinding current)
    {
        Result = current;
        _defaultHint = L.T("Esc - отмена, Del - отключить сочетание",
            "Esc - cancel, Del - disable the shortcut");

        Text = "Honor PC Helper";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.Manual;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.Font;
        // Размер подбирается по тексту: длина строк зависит от языка и масштаба DPI.
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(20, 14, 20, 14);
        BackColor = SystemColors.Window;

        var actionLabel = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.None,
            ForeColor = SystemColors.GrayText,
            Text = current.IsEmpty
                ? HotkeyManager.Describe(action)
                : $"{current}: {HotkeyManager.Describe(action)}"
        };
        _valueLabel = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.None,
            Margin = new Padding(3, 10, 3, 10),
            Font = new Font(Font.FontFamily, Font.Size + 3.5f, FontStyle.Bold),
            Text = L.T("Введите новое сочетание клавиш", "Press the new key combination")
        };
        _hintLabel = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.None,
            ForeColor = SystemColors.GrayText,
            Text = _defaultHint
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 3
        };
        layout.Controls.Add(actionLabel, 0, 0);
        layout.Controls.Add(_valueLabel, 0, 1);
        layout.Controls.Add(_hintLabel, 0, 2);
        Controls.Add(layout);
    }

    protected override void OnLoad(EventArgs eventArgs)
    {
        base.OnLoad(eventArgs);
        // Окно не должно ужиматься, когда подсказка сменится на короткий текст.
        MinimumSize = Size;
        PositionNearCursor();
    }

    protected override void OnShown(EventArgs eventArgs)
    {
        base.OnShown(eventArgs);
        Activate();
    }

    protected override void OnKeyUp(KeyEventArgs eventArgs)
    {
        base.OnKeyUp(eventArgs);
        if (IsModifier(eventArgs.KeyCode))
            ShowPendingModifiers();
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (message.Msg is not (WmKeyDown or WmSysKeyDown))
            return base.ProcessCmdKey(ref message, keyData);

        var key = keyData & Keys.KeyCode;
        switch (key)
        {
            case Keys.Escape:
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            case Keys.Delete or Keys.Back:
                Result = HotkeyBinding.None;
                DialogResult = DialogResult.OK;
                Close();
                return true;
        }

        if (IsModifier(key))
        {
            ShowPendingModifiers();
            return true;
        }

        var modifiers = CurrentModifiers();
        if ((modifiers & (HotkeyBinding.ModControl | HotkeyBinding.ModAlt | HotkeyBinding.ModWin)) == 0)
        {
            _valueLabel.Text = HotkeyBinding.FormatModifiers(modifiers) + FormatKey(key);
            _hintLabel.Text = L.T("Нужен модификатор: Ctrl, Alt или Win",
                "A modifier is required: Ctrl, Alt or Win");
            return true;
        }

        Result = new HotkeyBinding(modifiers, (uint)key);
        DialogResult = DialogResult.OK;
        Close();
        return true;
    }

    private static string FormatKey(Keys key) => new HotkeyBinding(0, (uint)key).ToString();

    private void ShowPendingModifiers()
    {
        var modifiers = CurrentModifiers();
        _valueLabel.Text = modifiers == 0
            ? L.T("Введите новое сочетание клавиш", "Press the new key combination")
            : HotkeyBinding.FormatModifiers(modifiers) + "...";
        _hintLabel.Text = _defaultHint;
    }

    // Win не входит в Keys.Modifiers, поэтому читается отдельно.
    private static uint CurrentModifiers()
    {
        var modifiers = 0u;
        var pressed = ModifierKeys;
        if ((pressed & Keys.Control) != 0)
            modifiers |= HotkeyBinding.ModControl;
        if ((pressed & Keys.Alt) != 0)
            modifiers |= HotkeyBinding.ModAlt;
        if ((pressed & Keys.Shift) != 0)
            modifiers |= HotkeyBinding.ModShift;
        if (IsDown(VkLeftWin) || IsDown(VkRightWin))
            modifiers |= HotkeyBinding.ModWin;
        return modifiers;
    }

    private static bool IsDown(int virtualKey) => (NativeMethods.GetKeyState(virtualKey) & 0x8000) != 0;

    private static bool IsModifier(Keys key) => key
        is Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey
        or Keys.ControlKey or Keys.LControlKey or Keys.RControlKey
        or Keys.Menu or Keys.LMenu or Keys.RMenu
        or Keys.LWin or Keys.RWin;

    private void PositionNearCursor()
    {
        var cursor = Cursor.Position;
        var workingArea = Screen.FromPoint(cursor).WorkingArea;
        var x = Math.Clamp(cursor.X - Width / 2, workingArea.Left, workingArea.Right - Width);
        var y = Math.Clamp(cursor.Y - Height - 12, workingArea.Top, workingArea.Bottom - Height);
        Location = new Point(x, y);
    }
}
