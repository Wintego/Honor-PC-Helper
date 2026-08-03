namespace HonorPCHelper;

internal enum HotkeyAction
{
    MinimizeWindow = 1,
    PlayPause = 2,
    NextTrack = 3,
    PreviousTrack = 4
}

/// <summary>
/// Глобальные сочетания клавиш: значения по умолчанию, пользовательские
/// переопределения из реестра, регистрация в системе и выполнение действий.
/// Идентификатор хоткея равен значению <see cref="HotkeyAction"/>.
/// </summary>
internal sealed class HotkeyManager : IDisposable
{
    private const uint VkC = 0x43;
    private const uint VkM = 0x4D;
    private const uint VkX = 0x58;
    private const uint VkZ = 0x5A;
    private const byte VkMediaNextTrack = 0xB0;
    private const byte VkMediaPreviousTrack = 0xB1;
    private const byte VkMediaPlayPause = 0xB3;
    private const uint KeyEventKeyUp = 0x0002;

    internal static readonly HotkeyAction[] Actions =
    [
        HotkeyAction.MinimizeWindow,
        HotkeyAction.PlayPause,
        HotkeyAction.NextTrack,
        HotkeyAction.PreviousTrack
    ];

    private readonly IntPtr _windowHandle;
    private readonly Action<string> _warn;
    private readonly Dictionary<HotkeyAction, HotkeyBinding> _bindings = [];
    private readonly HashSet<HotkeyAction> _registered = [];

    internal HotkeyManager(IntPtr windowHandle, Action<string> warn)
    {
        _windowHandle = windowHandle;
        _warn = warn;
        foreach (var action in Actions)
            _bindings[action] = LoadBinding(action);
    }

    internal bool Enabled => AppConfig.Current.HotkeysEnabled;

    internal HotkeyBinding GetBinding(HotkeyAction action)
        => _bindings.TryGetValue(action, out var binding) ? binding : DefaultBinding(action);

    internal bool HasCustomBindings()
        => Actions.Any(action => GetBinding(action) != DefaultBinding(action));

    internal static HotkeyBinding DefaultBinding(HotkeyAction action) => action switch
    {
        HotkeyAction.MinimizeWindow => new HotkeyBinding(HotkeyBinding.ModAlt, VkM),
        HotkeyAction.PlayPause => new HotkeyBinding(HotkeyBinding.ModAlt, VkX),
        HotkeyAction.NextTrack => new HotkeyBinding(HotkeyBinding.ModAlt, VkC),
        HotkeyAction.PreviousTrack => new HotkeyBinding(HotkeyBinding.ModAlt, VkZ),
        _ => HotkeyBinding.None
    };

    internal static string Describe(HotkeyAction action) => action switch
    {
        HotkeyAction.MinimizeWindow => L.T("свернуть окно под курсором", "minimize window under cursor", "最小化光标所在窗口"),
        HotkeyAction.PlayPause => L.T("воспроизведение/пауза", "play/pause", "播放/暂停"),
        HotkeyAction.NextTrack => L.T("следующий трек", "next track", "下一曲"),
        HotkeyAction.PreviousTrack => L.T("предыдущий трек", "previous track", "上一曲"),
        _ => string.Empty
    };

    /// <summary>Текст пункта меню: "Alt+X: воспроизведение/пауза".</summary>
    internal string MenuText(HotkeyAction action)
    {
        var binding = GetBinding(action);
        if (binding.IsEmpty)
            return L.T($"Отключено: {Describe(action)}", $"Disabled: {Describe(action)}", $"已停用：{Describe(action)}");

        return _registered.Contains(action)
            ? $"{binding}: {Describe(action)}"
            : L.T($"{binding}: {Describe(action)} - занято", $"{binding}: {Describe(action)} - in use",
                $"{binding}：{Describe(action)} - 已被占用");
    }

    // Регистрирует каждое сочетание отдельно: занятое другим приложением
    // не мешает остальным и не приводит к выходу.
    internal void RegisterAll()
    {
        if (!Enabled)
            return;

        List<string>? failed = null;
        foreach (var action in Actions)
        {
            var binding = GetBinding(action);
            if (binding.IsEmpty || _registered.Contains(action))
                continue;

            if (NativeMethods.RegisterHotKey(_windowHandle, (int)action, binding.RegistrationModifiers, binding.VirtualKey))
                _registered.Add(action);
            else
                (failed ??= []).Add(binding.ToString());
        }

        if (failed is null)
            return;

        var list = string.Join(", ", failed);
        AppLog.Error($"Hotkeys already in use: {list}");
        _warn(L.T($"Сочетания заняты другим приложением и отключены: {list}",
            $"Shortcuts are taken by another application and disabled: {list}",
            $"以下快捷键已被其他程序占用并停用：{list}"));
    }

    internal void UnregisterAll()
    {
        foreach (var action in _registered)
            NativeMethods.UnregisterHotKey(_windowHandle, (int)action);
        _registered.Clear();
    }

    /// <summary>
    /// Показывает окно захвата и применяет новое сочетание. На время ввода
    /// снимает регистрацию: иначе система перехватывает нажатия сама.
    /// </summary>
    internal void Rebind(HotkeyAction action)
    {
        UnregisterAll();
        try
        {
            using var capture = new HotkeyCaptureForm(action, GetBinding(action));
            if (capture.ShowDialog() != DialogResult.OK)
                return;

            var binding = capture.Result;
            var conflict = Actions.FirstOrDefault(
                other => other != action && !binding.IsEmpty && GetBinding(other) == binding);
            if (conflict != default)
            {
                MessageBox.Show(
                    L.T($"Сочетание {binding} уже назначено на действие \"{Describe(conflict)}\".",
                        $"The shortcut {binding} is already assigned to \"{Describe(conflict)}\".",
                        $"快捷键 {binding} 已分配给操作“{Describe(conflict)}”。"),
                    "Honor PC Helper", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Save(action, binding);
        }
        catch (Exception exception)
        {
            AppLog.Error("Hotkey rebind failed", exception);
        }
        finally
        {
            RegisterAll();
        }
    }

    internal void ResetToDefaults()
    {
        UnregisterAll();
        foreach (var action in Actions)
        {
            _bindings[action] = DefaultBinding(action);
            HardwareSettings.SetHotkey(action.ToString(), null);
        }
        RegisterAll();
    }

    internal void Dispatch(int hotkeyId)
    {
        switch ((HotkeyAction)hotkeyId)
        {
            case HotkeyAction.MinimizeWindow:
                MinimizeWindowUnderCursor();
                break;
            case HotkeyAction.PlayPause:
                SendMediaKey(VkMediaPlayPause);
                break;
            case HotkeyAction.NextTrack:
                SendMediaKey(VkMediaNextTrack);
                break;
            case HotkeyAction.PreviousTrack:
                SendMediaKey(VkMediaPreviousTrack);
                break;
        }
    }

    public void Dispose() => UnregisterAll();

    private void Save(HotkeyAction action, HotkeyBinding binding)
    {
        _bindings[action] = binding;
        HardwareSettings.SetHotkey(
            action.ToString(),
            binding == DefaultBinding(action) ? null : binding.ToString());
    }

    private static HotkeyBinding LoadBinding(HotkeyAction action)
    {
        var stored = HardwareSettings.GetHotkey(action.ToString());
        if (stored is null)
            return DefaultBinding(action);
        if (HotkeyBinding.TryParse(stored, out var binding))
            return binding;

        AppLog.Error($"Could not parse stored hotkey for {action}: {stored}");
        return DefaultBinding(action);
    }

    private static void MinimizeWindowUnderCursor()
    {
        if (!NativeMethods.GetCursorPos(out var point))
            return;

        var target = NativeMethods.WindowFromPoint(point);
        if (target == IntPtr.Zero)
            return;

        var root = NativeMethods.GetAncestor(target, NativeMethods.GetAncestorFlags.GetRoot);
        if (root != IntPtr.Zero)
            target = root;

        if (target != NativeMethods.GetDesktopWindow() && target != NativeMethods.GetShellWindow())
            NativeMethods.ShowWindow(target, NativeMethods.ShowWindowCommands.Minimize);
    }

    private static void SendMediaKey(byte virtualKey)
    {
        NativeMethods.KeybdEvent(virtualKey, 0, 0, UIntPtr.Zero);
        NativeMethods.KeybdEvent(virtualKey, 0, KeyEventKeyUp, UIntPtr.Zero);
    }
}
