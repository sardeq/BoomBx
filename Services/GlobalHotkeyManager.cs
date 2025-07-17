using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;
using SharpHook;
using SharpHook.Data;
using SharpHook.Native;

namespace BoomBx.Services
{
    public class GlobalHotkeyManager : IDisposable
    {
        private readonly TaskPoolGlobalHook _hook = new();
        private readonly Dictionary<Hotkey, Action> _actions = new();
        private bool _disposed;
        private readonly HashSet<KeyCode> _pressedKeys = new();


        public GlobalHotkeyManager()
        {
            _hook.KeyPressed += OnKeyPressed;
            _hook.KeyReleased += OnKeyReleased;
            _hook.RunAsync();
        }

        public void RegisterHotkey(KeyGesture gesture, Action action)
        {
            var modifiers = ConvertModifiers(gesture.KeyModifiers);
            var key = ConvertKey(gesture.Key);
            var hotkey = new Hotkey(modifiers, key);

            _actions[hotkey] = action;
        }

        public void UnregisterAll()
        {
            _actions.Clear();
        }
        
        private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
        {
            _pressedKeys.Add(e.Data.KeyCode);
            TryTriggerHotkey(e.Data.KeyCode);
        }

        private void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
        {
            _pressedKeys.Remove(e.Data.KeyCode);
        }

        private void TryTriggerHotkey(KeyCode key)
        {
            var modifiers = 0;

            if (_pressedKeys.Contains(KeyCode.VcLeftControl) || _pressedKeys.Contains(KeyCode.VcRightControl))
                modifiers |= 0x0004;
            if (_pressedKeys.Contains(KeyCode.VcLeftShift) || _pressedKeys.Contains(KeyCode.VcRightShift))
                modifiers |= 0x0002;
            if (_pressedKeys.Contains(KeyCode.VcLeftAlt) || _pressedKeys.Contains(KeyCode.VcRightAlt))
                modifiers |= 0x0008;
            if (_pressedKeys.Contains(KeyCode.VcLeftMeta) || _pressedKeys.Contains(KeyCode.VcRightMeta))
                modifiers |= 0x0001;

            var hotkey = new Hotkey((ushort)modifiers, key);

            if (_actions.TryGetValue(hotkey, out var action))
                action();
        }





        private static ushort ConvertModifiers(KeyModifiers modifiers)
        {
            ushort result = 0;
            if (modifiers.HasFlag(KeyModifiers.Alt)) result |= 0x0008;    //  Alt
            if (modifiers.HasFlag(KeyModifiers.Control)) result |= 0x0004; // Ctrl
            if (modifiers.HasFlag(KeyModifiers.Shift)) result |= 0x0002;   // Shift
            if (modifiers.HasFlag(KeyModifiers.Meta)) result |= 0x0001;    // Meta (Win key)
            return result;
        }


        private static KeyCode ConvertKey(Key key)
        {
            return key switch
            {
                Key.F1 => KeyCode.VcF1,
                Key.F2 => KeyCode.VcF2,
                Key.F3 => KeyCode.VcF3,
                Key.F4 => KeyCode.VcF4,
                Key.F5 => KeyCode.VcF5,
                Key.F6 => KeyCode.VcF6,
                Key.F7 => KeyCode.VcF7,
                Key.F8 => KeyCode.VcF8,
                Key.F9 => KeyCode.VcF9,
                Key.F10 => KeyCode.VcF10,
                Key.F11 => KeyCode.VcF11,
                Key.F12 => KeyCode.VcF12,
                Key.F13 => KeyCode.VcF13,
                Key.F14 => KeyCode.VcF14,
                Key.F15 => KeyCode.VcF15,
                Key.F16 => KeyCode.VcF16,
                Key.F17 => KeyCode.VcF17,
                Key.F18 => KeyCode.VcF18,
                Key.F19 => KeyCode.VcF19,
                Key.F20 => KeyCode.VcF20,
                Key.F21 => KeyCode.VcF21,
                Key.F22 => KeyCode.VcF22,
                Key.F23 => KeyCode.VcF23,
                Key.F24 => KeyCode.VcF24,
                Key.Left => KeyCode.VcLeft,
                Key.Right => KeyCode.VcRight,
                Key.Up => KeyCode.VcUp,
                Key.Down => KeyCode.VcDown,
                Key.Space => KeyCode.VcSpace,
                Key.Tab => KeyCode.VcTab,
                Key.Escape => KeyCode.VcEscape,
                Key.Enter => KeyCode.VcEnter,
                Key.Back => KeyCode.VcBackspace,
                Key.Delete => KeyCode.VcDelete,
                Key.Insert => KeyCode.VcInsert,
                Key.Home => KeyCode.VcHome,
                Key.End => KeyCode.VcEnd,
                Key.PageUp => KeyCode.VcPageUp,
                Key.PageDown => KeyCode.VcPageDown,
                Key.A => KeyCode.VcA,
                Key.B => KeyCode.VcB,
                Key.C => KeyCode.VcC,
                Key.D => KeyCode.VcD,
                Key.E => KeyCode.VcE,
                Key.F => KeyCode.VcF,
                Key.G => KeyCode.VcG,
                Key.H => KeyCode.VcH,
                Key.I => KeyCode.VcI,
                Key.J => KeyCode.VcJ,
                Key.K => KeyCode.VcK,
                Key.L => KeyCode.VcL,
                Key.M => KeyCode.VcM,
                Key.N => KeyCode.VcN,
                Key.O => KeyCode.VcO,
                Key.P => KeyCode.VcP,
                Key.Q => KeyCode.VcQ,
                Key.R => KeyCode.VcR,
                Key.S => KeyCode.VcS,
                Key.T => KeyCode.VcT,
                Key.U => KeyCode.VcU,
                Key.V => KeyCode.VcV,
                Key.W => KeyCode.VcW,
                Key.X => KeyCode.VcX,
                Key.Y => KeyCode.VcY,
                Key.Z => KeyCode.VcZ,
                Key.D0 => KeyCode.Vc0,
                Key.D1 => KeyCode.Vc1,
                Key.D2 => KeyCode.Vc2,
                Key.D3 => KeyCode.Vc3,
                Key.D4 => KeyCode.Vc4,
                Key.D5 => KeyCode.Vc5,
                Key.D6 => KeyCode.Vc6,
                Key.D7 => KeyCode.Vc7,
                Key.D8 => KeyCode.Vc8,
                Key.D9 => KeyCode.Vc9,
                Key.NumPad0 => KeyCode.VcNumPad0,
                Key.NumPad1 => KeyCode.VcNumPad1,
                Key.NumPad2 => KeyCode.VcNumPad2,
                Key.NumPad3 => KeyCode.VcNumPad3,
                Key.NumPad4 => KeyCode.VcNumPad4,
                Key.NumPad5 => KeyCode.VcNumPad5,
                Key.NumPad6 => KeyCode.VcNumPad6,
                Key.NumPad7 => KeyCode.VcNumPad7,
                Key.NumPad8 => KeyCode.VcNumPad8,
                Key.NumPad9 => KeyCode.VcNumPad9,
                Key.Multiply => KeyCode.VcNumPadMultiply,
                Key.Add => KeyCode.VcNumPadAdd,
                Key.Subtract => KeyCode.VcNumPadSubtract,
                Key.Decimal => KeyCode.VcNumPadDecimal,
                Key.Divide => KeyCode.VcNumPadDivide,
                Key.OemSemicolon => KeyCode.VcSemicolon,
                Key.OemComma => KeyCode.VcComma,
                Key.OemPeriod => KeyCode.VcPeriod,
                Key.OemQuestion => KeyCode.VcSlash,
                Key.OemTilde => KeyCode.VcBackQuote,
                Key.OemOpenBrackets => KeyCode.VcOpenBracket,
                Key.OemCloseBrackets => KeyCode.VcCloseBracket,
                Key.OemPipe => KeyCode.VcBackslash,
                Key.OemQuotes => KeyCode.VcQuote,
                Key.OemMinus => KeyCode.VcMinus,
                Key.OemPlus => KeyCode.VcEquals,
                _ => KeyCode.VcUndefined
            };
        }

        public void Dispose()
        {
            if (_disposed) return;
            
            _hook.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        private readonly record struct Hotkey(ushort Modifiers, KeyCode Key);
    }
}