using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Input;
using Avalonia.Threading;

namespace BoomBx.Services
{
    public class HotkeyManager : IDisposable
    {
        private const int WmHotkey = 0x0312;
        private readonly nint _windowHandle;
        private int _hotkeyIdCounter;
        private readonly Dictionary<int, (KeyGesture gesture, Action action)> _registeredHotkeys = new();
        private readonly HwndSource _source;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(nint hWnd, int id);

        public HotkeyManager(nint windowHandle)
        {
            _windowHandle = windowHandle;
            _source = HwndSource.Create(windowHandle, OnHwndMessage);
        }

        private bool OnHwndMessage(nint hwnd, int msg, nint wparam, nint lparam)
        {
            if (msg == WmHotkey)
            {
                var id = wparam.ToInt32();
                if (_registeredHotkeys.TryGetValue(id, out var hotkey))
                {
                    // Always dispatch the action to the UI thread to ensure state consistency
                    Dispatcher.UIThread.Post(hotkey.action);
                    return true;
                }
            }
            return false;
        }

        public bool RegisterHotkey(KeyGesture gesture, Action action)
        {
            if (gesture == null) throw new ArgumentNullException(nameof(gesture));

            var modifiers = (uint)KeyModifiersToWin32(gesture.KeyModifiers);
            var vk = (uint)KeyInterop.VirtualKeyFromKey(gesture.Key);
            var id = Interlocked.Increment(ref _hotkeyIdCounter);

            if (!RegisterHotKey(_windowHandle, id, modifiers, vk))
            {
                Console.WriteLine($"Failed to register hotkey: {gesture}");
                return false;
            }

            _registeredHotkeys[id] = (gesture, action);
            return true;
        }

        public void UnregisterAll()
        {
            foreach (var id in _registeredHotkeys.Keys)
            {
                UnregisterHotKey(_windowHandle, id);
            }
            _registeredHotkeys.Clear();
        }

        private static Modifiers KeyModifiersToWin32(KeyModifiers modifiers)
        {
            var winModifiers = Modifiers.NoRepeat;
            if (modifiers.HasFlag(KeyModifiers.Alt)) winModifiers |= Modifiers.Alt;
            if (modifiers.HasFlag(KeyModifiers.Control)) winModifiers |= Modifiers.Control;
            if (modifiers.HasFlag(KeyModifiers.Shift)) winModifiers |= Modifiers.Shift;
            if (modifiers.HasFlag(KeyModifiers.Meta)) winModifiers |= Modifiers.Win;
            return winModifiers;
        }

        public void Dispose()
        {
            _source.Dispose();
            UnregisterAll();
            GC.SuppressFinalize(this);
        }

        [Flags]
        private enum Modifiers : uint
        {
            Alt = 1,
            Control = 2,
            Shift = 4,
            Win = 8,
            NoRepeat = 0x4000
        }

        private static class KeyInterop
        {
            public static int VirtualKeyFromKey(Key key) => (int)key;
        }

        private class HwndSource : IDisposable
        {
            private delegate nint HwndProc(nint hwnd, int msg, nint wparam, nint lparam);
            private readonly HwndProc _newWndProc;
            private readonly nint _oldWndProc;
            private readonly nint _hwnd;

            [DllImport("user32.dll", SetLastError = true)]
            private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

            [DllImport("user32.dll")]
            private static extern nint CallWindowProc(nint lpPrevWndFunc, nint hWnd, int msg, nint wParam, nint lParam);

            private const int GwlWndproc = -4;

            private HwndSource(nint hwnd, Func<nint, int, nint, nint, bool> wndProc)
            {
                _hwnd = hwnd;
                _newWndProc = (h, m, w, l) => wndProc(h, m, w, l) ? 0 : CallWindowProc(_oldWndProc, h, m, w, l);
                _oldWndProc = SetWindowLongPtr(hwnd, GwlWndproc, Marshal.GetFunctionPointerForDelegate(_newWndProc));
            }

            public static HwndSource Create(nint hwnd, Func<nint, int, nint, nint, bool> wndProc)
            {
                return new HwndSource(hwnd, wndProc);
            }
            
            public void Dispose()
            {
                SetWindowLongPtr(_hwnd, GwlWndproc, _oldWndProc);
            }
        }
    }
}