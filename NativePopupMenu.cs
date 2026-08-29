namespace HonorPCHelper;

internal sealed class NativePopupMenu : IDisposable
{
    // Идентификаторы пунктов должны укладываться в 16 бит: WM_MENUSELECT
    // отдаёт их младшим словом WPARAM. Счётчик локальный для меню, поэтому
    // при многократном открытии трея он не переполняется.
    private const int FirstItemId = 1000;
    private const int LastItemId = 0xEFFF;

    /// <summary>Общее состояние корневого меню и всех его подменю.</summary>
    private sealed class MenuState
    {
        internal readonly Dictionary<int, Action> Callbacks = [];
        internal readonly Dictionary<int, string> Tooltips = [];
        internal readonly Dictionary<(IntPtr Menu, int Index), string> SubMenuTooltips = [];
        internal int NextId = FirstItemId;
    }

    // Меню отслеживается только в UI-потоке, где создаётся и обрабатывается.
    [ThreadStatic] private static NativePopupMenu? _activeMenu;

    internal IntPtr Handle { get; }
    private readonly MenuState _state;
    private readonly bool _ownsHandle;

    internal NativePopupMenu() : this(new MenuState(), ownsHandle: true)
    {
    }

    private NativePopupMenu(MenuState state, bool ownsHandle)
    {
        Handle = NativeMethods.CreatePopupMenu();
        _state = state;
        _ownsHandle = ownsHandle;
    }

    internal NativePopupMenu AddSubMenu(string text, string? tooltip = null)
    {
        var index = NativeMethods.GetMenuItemCount(Handle);
        var sub = new NativePopupMenu(_state, ownsHandle: false);
        NativeMethods.AppendMenuW(Handle, NativeMethods.MfPopup | NativeMethods.MfString | NativeMethods.MfEnabled,
            sub.Handle, text);
        if (tooltip is not null)
            _state.SubMenuTooltips[(Handle, index)] = tooltip;
        return sub;
    }

    internal int AddItem(string text, Action? onClick, bool enabled = true, bool @checked = false, string? tooltip = null)
    {
        var id = _state.NextId++;
        if (_state.NextId > LastItemId)
            _state.NextId = FirstItemId;
        if (onClick != null)
            _state.Callbacks[id] = onClick;
        if (tooltip != null)
            _state.Tooltips[id] = tooltip;
        var flags = NativeMethods.MfString;
        if (enabled)
            flags |= NativeMethods.MfEnabled;
        else
            flags |= NativeMethods.MfGrayed | NativeMethods.MfDisabled;
        if (@checked)
            flags |= NativeMethods.MfChecked;
        NativeMethods.AppendMenuW(Handle, flags, id, text);
        return id;
    }

    internal void AddSeparator()
    {
        NativeMethods.AppendMenuW(Handle, NativeMethods.MfSeparator, 0, null);
    }

    internal int Show(IntPtr owner)
    {
        NativeMethods.SetForegroundWindow(owner);
        var pos = Control.MousePosition;
        _activeMenu = this;
        try
        {
            return NativeMethods.TrackPopupMenuEx(
                Handle,
                NativeMethods.TpmReturnCmd | NativeMethods.TpmLeftAlign | NativeMethods.TpmTopAlign | NativeMethods.TpmRightButton,
                pos.X, pos.Y,
                owner,
                IntPtr.Zero);
        }
        finally
        {
            if (_activeMenu == this)
                _activeMenu = null;
        }
    }

    internal Action? GetCallback(int commandId)
        => _state.Callbacks.GetValueOrDefault(commandId);

    internal static string? GetTooltip(int commandId)
        => _activeMenu is { } menu && menu._state.Tooltips.TryGetValue(commandId, out var text) ? text : null;

    internal static string? GetSubMenuTooltip(IntPtr menuHandle, int itemIndex)
        => _activeMenu is { } menu && menu._state.SubMenuTooltips.TryGetValue((menuHandle, itemIndex), out var text)
            ? text
            : null;

    public void Dispose()
    {
        // DestroyMenu рекурсивно уничтожает вложенные подменю.
        if (_ownsHandle && Handle != IntPtr.Zero)
            NativeMethods.DestroyMenu(Handle);
    }
}
