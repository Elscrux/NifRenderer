using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using NifRenderer.UI;
using NifRenderer.UI.Common;
using NifRenderer.UI.Components.Dockers;
namespace MyGame;

public sealed class MonoGameControl : Control {
	private static readonly IBrush DefaultBackground = Brushes.Crimson;

	public static readonly AttachedProperty<Game?> GameProperty = AvaloniaProperty.RegisterAttached<MonoGameControl, Game?>(nameof(Game), typeof(MonoGameControl));

	private readonly PresentationParameters _presentationParameters = new() {
		BackBufferWidth = 1,
		BackBufferHeight = 1,
		BackBufferFormat = SurfaceFormat.Color,
		DepthStencilFormat = DepthFormat.Depth24,
		PresentationInterval = PresentInterval.Immediate,
		IsFullScreen = false
	};

	private byte[] _buffer = [];
	private WriteableBitmap? _bitmap;
	private bool _isInitialized;

	public MonoGameControl() {
		Focusable = true;
		DragDrop.SetAllowDrop(this, true);
		DragDrop.AddDragOverHandler(this, OnDragOver);
		DragDrop.AddDropHandler(this, OnDrop);
		AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
		AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel);
		AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
		AddHandler(PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Tunnel);
		AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
		AddHandler(KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel);
		AddHandler(TextInputEvent, OnTextInput, RoutingStrategies.Tunnel);
		GotFocus += (_, _) => UserInput.SetExternalFocus(true);
		LostFocus += (_, _) => UserInput.SetExternalFocus(false);
	}

	private static void OnDragOver(object? sender, DragEventArgs args) {
		args.DragEffects = args.DataTransfer.Contains(DataFormat.File)
			? DragDropEffects.Copy
			: DragDropEffects.None;
		args.Handled = true;
	}

	private static void OnDrop(object? sender, DragEventArgs args) {
		if (!args.DataTransfer.Contains(DataFormat.File)) {
			args.Handled = true;
			return;
		}

		var files = args.DataTransfer.TryGetFiles();
		if (files is not null) {
			foreach (var file in files) {
				if (file.Path.IsFile) {
					NifDocker.Instance.Load(file.Path.LocalPath);
				}
			}
		}

		args.Handled = true;
	}

	public Game? Game {
		get;
		set {
			if (field == value) {
				return;
			}

			field = value;

			if (_isInitialized) {
				Initialize();
			}
		}
	}

	public override void Render(DrawingContext context) {
		if (Game?.GraphicsDevice is not {} device
		 || Bounds is { Width: < 1, Height: < 1 }
		 || _bitmap is null
		 || !HandleDeviceReset(device)) {
			context.DrawRectangle(DefaultBackground, null, new Rect(Bounds.Size));
			return;
		}

		// Run the game frame to update the back buffer
		RunFrame(Game);
		// Capture the back buffer into the bitmap
		CaptureFrame(device, _bitmap);
		// Draw the bitmap onto the control
		context.DrawImage(_bitmap, new Rect(_bitmap.Size), Bounds);
	}

	protected override Size ArrangeOverride(Size finalSize) {
		finalSize = base.ArrangeOverride(finalSize);
		if (finalSize != _bitmap?.Size && Game?.GraphicsDevice is {} device) {
			ResetDevice(device, finalSize);
		}

		return finalSize;
	}

	protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
		base.OnAttachedToVisualTree(e);
		Start();
	}

	private bool HandleDeviceReset(GraphicsDevice device) {
		if (device.GraphicsDeviceStatus == GraphicsDeviceStatus.NotReset) {
			ResetDevice(device, Bounds.Size);
		}

		return device.GraphicsDeviceStatus == GraphicsDeviceStatus.Normal;
	}

	private void Initialize() {
		TrySetWindowHandle();

		if (Game is not {} game) {
			return;
		}

		if (game.GraphicsDevice is {} device) {
			ResetDevice(device, Bounds.Size);
		}

		RunFrame(game);
	}

	private void Start() {
		if (_isInitialized) {
			return;
		}

		Initialize();
		_isInitialized = true;
	}

	private void ResetDevice(GraphicsDevice device, Size newSize) {
		var counter = 0;
		while (_presentationParameters.DeviceWindowHandle == IntPtr.Zero) {
			TrySetWindowHandle();
			counter++;

			if (counter > 3) {
				break;
			}
		}

		_presentationParameters.BackBufferWidth = Math.Max(1, (int) Math.Ceiling(newSize.Width));
		_presentationParameters.BackBufferHeight = Math.Max(1, (int) Math.Ceiling(newSize.Height));

		device.Viewport = new Viewport(0, 0, _presentationParameters.BackBufferWidth, _presentationParameters.BackBufferHeight);
		device.Reset(_presentationParameters);

		_bitmap?.Dispose();
		_bitmap = new WriteableBitmap(
			new PixelSize(device.Viewport.Width, device.Viewport.Height),
			new Vector(96, 96),
			PixelFormat.Rgba8888,
			AlphaFormat.Opaque);
	}

	private void TrySetWindowHandle() {
		if (TopLevel.GetTopLevel(this) is {} topLevel && topLevel.TryGetPlatformHandle()?.Handle is {} handle) {
			_presentationParameters.DeviceWindowHandle = handle;
		}
	}

	private void RunFrame(Game game) {
		try {
			game.RunOneFrame();
		} catch (Exception e) {
			Console.WriteLine(e);
		} finally {
			Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
		}
	}

	private void CaptureFrame(GraphicsDevice device, WriteableBitmap bitmap) {
		using var bitmapLock = bitmap.Lock();
		var bitmapSize = bitmapLock.RowBytes * bitmapLock.Size.Height;
		if (_buffer.Length < bitmapSize) {
			Array.Resize(ref _buffer, bitmapSize);
		}

		device.GetBackBufferData(_buffer, 0, bitmapSize);
		Marshal.Copy(_buffer, 0, bitmapLock.Address, bitmapSize);
	}

	private void OnPointerPressed(object? sender, PointerEventArgs args) {
		Focus();
		var position = ToGamePosition(args);
		UpdateMouse(args, position);
		var kind = args.GetCurrentPoint(this).Properties.PointerUpdateKind;

		if (kind == PointerUpdateKind.LeftButtonPressed) {
			UIManager.Instance?.DispatchMouseLeftDown(position);
		} else if (kind == PointerUpdateKind.RightButtonPressed) {
			UIManager.Instance?.DispatchMouseRightDown(position);
		}

		args.Pointer.Capture(this);
		args.Handled = true;
	}

	private void OnPointerReleased(object? sender, PointerEventArgs args) {
		var position = ToGamePosition(args);
		UpdateMouse(args, position);
		var kind = args.GetCurrentPoint(this).Properties.PointerUpdateKind;

		if (kind == PointerUpdateKind.LeftButtonReleased) {
			UIManager.Instance?.DispatchMouseLeftUp(position);
		} else if (kind == PointerUpdateKind.RightButtonReleased) {
			UIManager.Instance?.DispatchMouseRightUp(position);
		}

		args.Pointer.Capture(null);
		args.Handled = true;
	}

	private void OnPointerMoved(object? sender, PointerEventArgs args) {
		var position = ToGamePosition(args);
		var previous = UserInput.GetMousePos();
		UpdateMouse(args, position);

		if (position != previous) {
			UIManager.Instance?.DispatchMouseMove(position, position - previous);
		}

		args.Handled = true;
	}

	private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs args) {
		var position = ToGamePosition(args);
		var wheel = (int) Math.Round(args.Delta.Y * 120d);
		var current = UserInput.CurrentMouseState.ScrollWheelValue + wheel;

		UserInput.SetExternalMouseState(
			position,
			UserInput.CurrentMouseState.LeftButton,
			UserInput.CurrentMouseState.RightButton,
			UserInput.CurrentMouseState.MiddleButton,
			current);
		UIManager.Instance?.DispatchMouseWheel(position, wheel);
		args.Handled = true;
	}

	private Vector2 ToGamePosition(PointerEventArgs args) {
		var point = args.GetPosition(this);
		var viewport = Game?.GraphicsDevice.Viewport ?? default;

		return new Vector2(
			(float) (point.X * viewport.Width / Math.Max(1d, Bounds.Width)),
			(float) (point.Y * viewport.Height / Math.Max(1d, Bounds.Height)));
	}

	private void UpdateMouse(PointerEventArgs args, Vector2 position) {
		var properties = args.GetCurrentPoint(this).Properties;

		UserInput.SetExternalMouseState(
			position,
			properties.IsLeftButtonPressed ? ButtonState.Pressed : ButtonState.Released,
			properties.IsRightButtonPressed ? ButtonState.Pressed : ButtonState.Released,
			properties.IsMiddleButtonPressed ? ButtonState.Pressed : ButtonState.Released,
			UserInput.CurrentMouseState.ScrollWheelValue);
	}

	private static void OnKeyDown(object? sender, KeyEventArgs args) {
		var key = ToXnaKey(args.Key);
		var keys = UserInput.CurrentKeyState.GetPressedKeys().Append(key).Distinct().ToArray();
		UserInput.SetExternalKeyboardState(keys);
		UIManager.Instance?.DispatchKeyDown(key);
		args.Handled = true;
	}

	private static void OnKeyUp(object? sender, KeyEventArgs args) {
		var key = ToXnaKey(args.Key);
		var keys = UserInput.CurrentKeyState.GetPressedKeys().Where(current => current != key).ToArray();
		UserInput.SetExternalKeyboardState(keys);
		UIManager.Instance?.DispatchKeyUp(key);
		args.Handled = true;
	}

	private static void OnTextInput(object? sender, Avalonia.Input.TextInputEventArgs args) {
		foreach (var character in args.Text ?? string.Empty)
			UIManager.Instance?.DispatchTextInput(character);

		args.Handled = true;
	}

	private static Keys ToXnaKey(Key key) =>
		key switch {
			Key.LeftCtrl => Keys.LeftControl,
			Key.RightCtrl => Keys.RightControl,
			Key.LeftAlt => Keys.LeftAlt,
			Key.RightAlt => Keys.RightAlt,
			Key.LeftShift => Keys.LeftShift,
			Key.RightShift => Keys.RightShift,
			_ => Enum.TryParse<Keys>(key.ToString(), out var result) ? result : Keys.None
		};
}
