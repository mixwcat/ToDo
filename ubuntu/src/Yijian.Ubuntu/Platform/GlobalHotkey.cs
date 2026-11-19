using System.Runtime.InteropServices;
using Avalonia.Input;
using Avalonia.Threading;

namespace Yijian.Ubuntu.Platform;

internal sealed class GlobalHotkey : IDisposable {
    const int KeyPress = 2;
    const long KeyPressMask = 1L << 0;
    const int GrabModeAsync = 1;
    const uint ShiftMask = 1 << 0;
    const uint LockMask = 1 << 1;
    const uint ControlMask = 1 << 2;
    const uint Mod1Mask = 1 << 3;
    const uint Mod2Mask = 1 << 4;
    const uint Mod4Mask = 1 << 6;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int XErrorHandler(IntPtr display, IntPtr errorEvent);

    [StructLayout(LayoutKind.Explicit, Size = 192)]
    struct XEvent {
        [FieldOffset(0)] public int type;
        [FieldOffset(84)] public uint state;
        [FieldOffset(88)] public uint keycode;
    }

    [DllImport("libX11.so.6")] static extern IntPtr XOpenDisplay(IntPtr display);
    [DllImport("libX11.so.6")] static extern int XCloseDisplay(IntPtr display);
    [DllImport("libX11.so.6")] static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport("libX11.so.6")] static extern IntPtr XStringToKeysym(string str);
    [DllImport("libX11.so.6")] static extern byte XKeysymToKeycode(IntPtr display, IntPtr keysym);
    [DllImport("libX11.so.6")] static extern int XGrabKey(IntPtr display, int keycode, uint modifiers, IntPtr grabWindow, int ownerEvents, int pointerMode, int keyboardMode);
    [DllImport("libX11.so.6")] static extern int XUngrabKey(IntPtr display, int keycode, uint modifiers, IntPtr grabWindow);
    [DllImport("libX11.so.6")] static extern int XSelectInput(IntPtr display, IntPtr window, long eventMask);
    [DllImport("libX11.so.6")] static extern int XPending(IntPtr display);
    [DllImport("libX11.so.6")] static extern int XNextEvent(IntPtr display, out XEvent xevent);
    [DllImport("libX11.so.6")] static extern int XSync(IntPtr display, bool discard);
    [DllImport("libX11.so.6")] static extern IntPtr XSetErrorHandler(XErrorHandler handler);

    static readonly XErrorHandler IgnoreXError = (_, _) => 0;

    readonly Action callback;
    readonly object sync = new();
    readonly CancellationTokenSource cts = new();
    readonly Thread? worker;
    string currentShortcut;
    volatile bool rebindRequested;
    DateTime lastTriggered = DateTime.MinValue;

    public GlobalHotkey(string shortcut, Action onTriggered) {
        currentShortcut = string.IsNullOrWhiteSpace(shortcut) ? "F2" : shortcut.Trim();
        callback = onTriggered;
        if (OperatingSystem.IsLinux()) {
            worker = new Thread(Loop) { IsBackground = true, Name = "YijianGlobalHotkey" };
            worker.Start();
            _ = TrySyncGnomeShortcutAsync(currentShortcut);
        }
    }

    public void Update(string shortcut) {
        var next = string.IsNullOrWhiteSpace(shortcut) ? "F2" : shortcut.Trim();
        lock (sync) {
            if (string.Equals(currentShortcut, next, StringComparison.OrdinalIgnoreCase)) return;
            currentShortcut = next;
            rebindRequested = true;
        }
        if (OperatingSystem.IsLinux()) {
            _ = TrySyncGnomeShortcutAsync(next);
        }
    }

    public void NotifyTriggered() {
        var now = DateTime.UtcNow;
        lock (sync) {
            if ((now - lastTriggered).TotalMilliseconds < 250) return;
            lastTriggered = now;
        }
        Dispatcher.UIThread.Post(callback);
    }

    void Loop() {
        IntPtr display = IntPtr.Zero;
        try {
            XSetErrorHandler(IgnoreXError);
            display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero) return;
            var root = XDefaultRootWindow(display);
            XSelectInput(display, root, KeyPressMask);

            int grabbedKeycode = 0;
            uint grabbedMods = 0;

            void BindCurrent() {
                if (grabbedKeycode != 0) {
                    foreach (var extra in new[] { 0u, LockMask, Mod2Mask, LockMask | Mod2Mask }) {
                        XUngrabKey(display, grabbedKeycode, grabbedMods | extra, root);
                    }
                    grabbedKeycode = 0;
                }
                string shortcut;
                lock (sync) {
                    shortcut = currentShortcut;
                    rebindRequested = false;
                }
                if (TryParseX11Key(display, shortcut, out var kc, out var mods) && kc != 0) {
                    grabbedKeycode = kc;
                    grabbedMods = mods;
                    foreach (var extra in new[] { 0u, LockMask, Mod2Mask, LockMask | Mod2Mask }) {
                        XGrabKey(display, kc, mods | extra, root, 1, GrabModeAsync, GrabModeAsync);
                    }
                    XSync(display, false);
                }
            }

            BindCurrent();

            while (!cts.IsCancellationRequested) {
                if (rebindRequested) {
                    BindCurrent();
                }
                while (XPending(display) > 0) {
                    XNextEvent(display, out var ev);
                    if (ev.type == KeyPress && grabbedKeycode != 0 && ev.keycode == (uint)grabbedKeycode) {
                        var relevantMods = ev.state & (ShiftMask | ControlMask | Mod1Mask | Mod4Mask);
                        if (relevantMods == grabbedMods) {
                            NotifyTriggered();
                        }
                    }
                }
                Thread.Sleep(35);
            }

            if (grabbedKeycode != 0) {
                foreach (var extra in new[] { 0u, LockMask, Mod2Mask, LockMask | Mod2Mask }) {
                    XUngrabKey(display, grabbedKeycode, grabbedMods | extra, root);
                }
                XSync(display, false);
            }
        } catch {
            // X11 not available or display closed; fail silently
        } finally {
            if (display != IntPtr.Zero) {
                try { XCloseDisplay(display); } catch { }
            }
        }
    }

    static bool TryParseX11Key(IntPtr display, string shortcut, out int keycode, out uint modifiers) {
        keycode = 0;
        modifiers = 0;
        try {
            var gesture = KeyGesture.Parse(shortcut);
            if (gesture.KeyModifiers.HasFlag(KeyModifiers.Shift)) modifiers |= ShiftMask;
            if (gesture.KeyModifiers.HasFlag(KeyModifiers.Control)) modifiers |= ControlMask;
            if (gesture.KeyModifiers.HasFlag(KeyModifiers.Alt)) modifiers |= Mod1Mask;
            if (gesture.KeyModifiers.HasFlag(KeyModifiers.Meta)) modifiers |= Mod4Mask;

            var symName = MapKeyToX11KeysymName(gesture.Key);
            if (string.IsNullOrEmpty(symName)) return false;
            var keysym = XStringToKeysym(symName);
            if (keysym == IntPtr.Zero) return false;
            keycode = XKeysymToKeycode(display, keysym);
            return keycode != 0;
        } catch {
            var keysym = XStringToKeysym("F2");
            if (keysym == IntPtr.Zero) return false;
            keycode = XKeysymToKeycode(display, keysym);
            return keycode != 0;
        }
    }

    static string? MapKeyToX11KeysymName(Key key) {
        if (key >= Key.F1 && key <= Key.F24) return key.ToString();
        if (key >= Key.A && key <= Key.Z) return key.ToString().ToLowerInvariant();
        if (key >= Key.D0 && key <= Key.D9) return ((int)(key - Key.D0)).ToString();
        return key switch {
            Key.Space => "space",
            Key.Tab => "Tab",
            Key.Return => "Return",
            Key.Back => "BackSpace",
            Key.Delete => "Delete",
            Key.Insert => "Insert",
            Key.Home => "Home",
            Key.End => "End",
            Key.PageUp => "Prior",
            Key.PageDown => "Next",
            Key.Up => "Up",
            Key.Down => "Down",
            Key.Left => "Left",
            Key.Right => "Right",
            Key.OemMinus => "minus",
            Key.OemPlus => "equal",
            _ => key.ToString()
        };
    }

    static async Task TrySyncGnomeShortcutAsync(string shortcut) {
        try {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return;
            var gnomeBinding = ToGnomeAccelerator(shortcut);
            if (string.IsNullOrEmpty(gnomeBinding)) return;

            const string entryPath = "/org/gnome/settings-daemon/plugins/media-keys/custom-keybindings/yijian-todo-note/";
            const string schema = "org.gnome.settings-daemon.plugins.media-keys";
            const string itemSchema = $"org.gnome.settings-daemon.plugins.media-keys.custom-keybinding:{entryPath}";

            var current = await DesktopTools.RunAsync("gsettings", ["get", schema, "custom-keybindings"], timeoutSeconds: 2);
            if (current.Code != 0) return;

            var listText = current.Output.Trim();
            if (!listText.Contains(entryPath, StringComparison.Ordinal)) {
                string updated;
                if (listText == "@as []" || listText == "[]") {
                    updated = $"['{entryPath}']";
                } else if (listText.EndsWith(']')) {
                    updated = listText[..^1] + $", '{entryPath}']";
                } else {
                    return;
                }
                await DesktopTools.RunAsync("gsettings", ["set", schema, "custom-keybindings", updated], timeoutSeconds: 2);
            }

            var cmd = $"\"{exePath}\" --data-dir \"{App.DataRoot}\" --open-note";
            await DesktopTools.RunAsync("gsettings", ["set", itemSchema, "name", "Yijian Todo Sticky Note"], timeoutSeconds: 2);
            await DesktopTools.RunAsync("gsettings", ["set", itemSchema, "command", cmd], timeoutSeconds: 2);
            await DesktopTools.RunAsync("gsettings", ["set", itemSchema, "binding", gnomeBinding], timeoutSeconds: 2);
        } catch {
            // Ignore when not on GNOME or when dconf is unavailable
        }
    }

    static string? ToGnomeAccelerator(string shortcut) {
        try {
            var gesture = KeyGesture.Parse(shortcut);
            var sb = new System.Text.StringBuilder();
            if (gesture.KeyModifiers.HasFlag(KeyModifiers.Control)) sb.Append("<Control>");
            if (gesture.KeyModifiers.HasFlag(KeyModifiers.Alt)) sb.Append("<Alt>");
            if (gesture.KeyModifiers.HasFlag(KeyModifiers.Shift)) sb.Append("<Shift>");
            if (gesture.KeyModifiers.HasFlag(KeyModifiers.Meta)) sb.Append("<Super>");
            var keyName = MapKeyToX11KeysymName(gesture.Key);
            if (string.IsNullOrEmpty(keyName)) return null;
            sb.Append(keyName);
            return sb.ToString();
        } catch {
            return "F2";
        }
    }

    public void Dispose() {
        cts.Cancel();
    }
}
