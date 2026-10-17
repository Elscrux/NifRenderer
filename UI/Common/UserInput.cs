using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using NifRenderer.Extensions;
using NifRenderer.Utils;
using System;
using System.Runtime.InteropServices;

namespace NifRenderer.UI.Common
{
    public static class UserInput
    {
        public static KeyboardState CurrentKeyState;

        public static KeyboardState PreviousKeyState;

        public static MouseState CurrentMouseState;

        public static MouseState PreviousMouseState;

        public static bool HasFocus = true;

        public static Action FocusLost;

        public static bool UsesExternalInput;

        public static void SetExternalFocus(bool focused)
        {
            UsesExternalInput = true;

            if (HasFocus && !focused)
                FocusLost?.Invoke();

            HasFocus = focused;

            if (!focused)
            {
                CurrentKeyState = default;
                PreviousKeyState = default;
                CurrentMouseState = new MouseState(
                    CurrentMouseState.X,
                    CurrentMouseState.Y,
                    CurrentMouseState.ScrollWheelValue,
                    ButtonState.Released,
                    ButtonState.Released,
                    ButtonState.Released,
                    ButtonState.Released,
                    ButtonState.Released);
                PreviousMouseState = CurrentMouseState;
            }
        }

        public static void SetExternalKeyboardState(Keys[] keys)
        {
            UsesExternalInput = true;
            CurrentKeyState = new KeyboardState(keys);
        }

        public static void SetExternalMouseState(Vector2 position, ButtonState left, ButtonState right, ButtonState middle, int wheel)
        {
            UsesExternalInput = true;
            CurrentMouseState = new MouseState(
                (int)MathF.Round(position.X),
                (int)MathF.Round(position.Y),
                wheel,
                left,
                right,
                middle,
                ButtonState.Released,
                ButtonState.Released);
        }

        public static void GetState(bool focused)
        {
            if (UsesExternalInput)
            {
                PreviousKeyState = CurrentKeyState;
                PreviousMouseState = CurrentMouseState;
                return;
            }

            PreviousKeyState = CurrentKeyState;
            PreviousMouseState = CurrentMouseState;

            if (focused)
            {
                CurrentKeyState = Keyboard.GetState();
                CurrentMouseState = Mouse.GetState();
            }
            else
            {
                CurrentKeyState = default;

                PreviousKeyState = default;

                var hardware = Mouse.GetState();

                CurrentMouseState = new MouseState(
                    hardware.X,
                    hardware.Y,
                    hardware.ScrollWheelValue,
                    ButtonState.Released,
                    ButtonState.Released,
                    ButtonState.Released,
                    ButtonState.Released,
                    ButtonState.Released);

                PreviousMouseState = CurrentMouseState;
            }

            if (HasFocus && !focused)
            {
                HasFocus = false;

                FocusLost?.Invoke();
            }
            else if (focused)
            {
                HasFocus = true;
            }
        }

        public static void Update(bool focused)
        {
            GetState(focused);

            if (!focused)
                return;

            var mousePos = GetMousePos();

            var posDelta = GetDeltaPosValue();

            if (posDelta != Vector2.Zero)
                UIManager.Instance?.DispatchMouseMove(mousePos, posDelta);

            var wheelDelta = GetDeltaWheelValue();

            if (wheelDelta != 0)
                UIManager.Instance?.DispatchMouseWheel(mousePos, wheelDelta);

            if (MouseLeftPressed)
                UIManager.Instance?.DispatchMouseLeftDown(mousePos);

            if (MouseLeftReleased)
                UIManager.Instance?.DispatchMouseLeftUp(mousePos);

            if (MouseRightPressed)
                UIManager.Instance?.DispatchMouseRightDown(mousePos);

            if (MouseRightReleased)
                UIManager.Instance?.DispatchMouseRightUp(mousePos);

            foreach (Keys key in KeyList)
            {
                if (PreviousKeyState.IsKeyUp(key) && CurrentKeyState.IsKeyDown(key))
                    UIManager.Instance?.DispatchKeyDown(key);
                else if (PreviousKeyState.IsKeyDown(key) && CurrentKeyState.IsKeyUp(key))
                    UIManager.Instance?.DispatchKeyUp(key);
            }
        }

        private static readonly Keys[] KeyList = (Keys[])Enum.GetValues(typeof(Keys));

        public static bool IsPressed(Keys key) => PreviousKeyState.IsKeyDown(key) && CurrentKeyState.IsKeyUp(key);

        public static bool IsKeyDown(Keys key) => CurrentKeyState.IsKeyDown(key);

        public static bool IsKeyUp(Keys key) => CurrentKeyState.IsKeyUp(key);

        public static bool IsJustPress(Keys key) => PreviousKeyState.IsKeyUp(key) && CurrentKeyState.IsKeyDown(key);

        public static bool MouseLeftPressed => CurrentMouseState.MouseLeft() && !PreviousMouseState.MouseLeft();

        public static bool MouseLeftReleased => !CurrentMouseState.MouseLeft() && PreviousMouseState.MouseLeft();

        public static bool MouseRightPressed => CurrentMouseState.MouseRight() && !PreviousMouseState.MouseRight();

        public static bool MouseRightReleased => !CurrentMouseState.MouseRight() && PreviousMouseState.MouseRight();

        public static Vector2 GetDeltaPosValue() => CurrentMouseState.Position() - PreviousMouseState.Position();

        public static int GetDeltaWheelValue() => CurrentMouseState.ScrollWheelValue - PreviousMouseState.ScrollWheelValue;

        public static Vector2 GetMousePos() => new(CurrentMouseState.X, CurrentMouseState.Y);

        public static Rectangle GetMouseRectangle() => RectangleUtils.FromVector2(GetMousePos(), new(1, 1));

        public static bool Shift => IsKeyDown(Keys.LeftShift) || IsKeyDown(Keys.RightShift);

        public static bool Ctrl => IsKeyDown(Keys.LeftControl) || IsKeyDown(Keys.RightControl);

        public static bool Alt => IsKeyDown(Keys.LeftAlt) || IsKeyDown(Keys.RightAlt);

        public static bool Esc => IsKeyDown(Keys.Escape);
    }

    public struct InputEventArgs
    {
        public InputType Type;

        public Vector2 MousePosition;

        public Vector2 MouseDelta;

        public ButtonState MouseButtonState;

        public int WheelDelta;

        public Keys Key;

        public char Character;

        public bool Shift;

        public bool Ctrl;

        public bool Alt;
    }

    public enum InputResult
    {
        Continue,
        Consumed
    }

    public enum InputType
    {
        MouseLeftDown,
        MouseLeftUp,
        MouseRightDown,
        MouseRightUp,
        MouseMove,
        MouseWheel,
        KeyDown,
        KeyUp,
        TextInput
    }

    public class Clipboard
    {
        private static uint CF_TEXT = 1;

        private static uint CF_BITMAP = 2;

        public static void SetClipboardText(string text)
        {
            OpenClipboard(nint.Zero);

            EmptyClipboard();

            SetClipboardData(CF_TEXT, Marshal.StringToCoTaskMemUTF8(text));

            CloseClipboard();
        }

        public static string GetClipboardText()
        {
            OpenClipboard(nint.Zero);

            var data = GetClipboardData(CF_TEXT);

            var text = Marshal.PtrToStringUTF8(data);

            CloseClipboard();

            return text;
        }

        public static ClipboardDataFormat GetDataFormat()
        {
            if (IsClipboardFormatAvailable(CF_TEXT))
                return ClipboardDataFormat.Text;

            if (IsClipboardFormatAvailable(CF_BITMAP))
                return ClipboardDataFormat.Bitmap;

            return ClipboardDataFormat.Unsupport;
        }

        [DllImport("user32.dll")]
        private extern static bool EmptyClipboard();

        [DllImport("user32.dll")]
        private extern static bool OpenClipboard(nint hWndNewOwner);

        [DllImport("user32.dll")]
        private extern static bool CloseClipboard();

        [DllImport("user32.dll")]
        private extern static nint GetClipboardData(uint uFormat);

        [DllImport("user32.dll")]
        private extern static nint SetClipboardData(uint uFormat, nint hMem);

        [DllImport("user32.dll")]
        private extern static bool IsClipboardFormatAvailable(uint uFormat);
    }

    public enum ClipboardDataFormat
    {
        Text,

        Bitmap,

        Unsupport
    }
}
