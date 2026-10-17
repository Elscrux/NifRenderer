using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using NifRenderer.Extensions;
using NifRenderer.Editing;
using NifRenderer.Renderer;
using NifRenderer.UI.Components;
using NifRenderer.UI.Components.Docking;
using NifRenderer.Editing.Records;
using NifRenderer.Utils;
using SkiaSharp;
using System;
using System.Collections.Generic;

namespace NifRenderer.UI.Components.Dockers
{
    public class NifDocker : DockPanel
    {
        public static NifDocker Instance;

        public NifDocker(DockTabNode node) : base(node)
        {
            UserInput.FocusLost += CancelInteraction;

            Instance = this;

            _renderer = new(GameHost.Instance.GraphicsDevice);
        }

        private readonly Renderer.Renderer _renderer;

        public Renderer.Renderer Graphics => _renderer;

        public override bool OwnsHeader => true;

        private const int BaseHeaderHeight = 28;

        private static int HeaderTextSize => UIMetrics.ModeTextSize;

        private const int HeaderInfoSize = 12;

        public static int HeaderHeight => UIScale.Value(BaseHeaderHeight);

        private static int HeaderPad => UIScale.Value(7);

        private static int ModeButtonHeight => UIMetrics.ModeButtonHeight;

        private static int ModeGlyphInset => UIMetrics.ModeIconInset;

        private static int ModeGlyphSize => UIMetrics.ModeIconSize;

        private static int ModeTextInset => UIMetrics.ModeTextInset;

        private static int ModeCaretSize => UIScale.Value(9);

        private bool _hoverMode;

        public Rectangle Viewport => Rectangle;

        private Rectangle HeaderRect => new(Rectangle.X, Rectangle.Y, Width, HeaderHeight);

        private Rectangle ModeRect
        {
            get
            {
                var header = HeaderRect;

                var text = _renderer.Mode.Label();

                var width = (int)UIFont.Measure(text, HeaderTextSize).X + ModeTextInset + ModeCaretSize + HeaderPad * 2;

                var height = ModeButtonHeight;

                return new Rectangle(header.X + HeaderPad, header.Y + (header.Height - height) / 2, width, height);
            }
        }

        public void SetMode(EditorMode mode)
        {
            _renderer.SetMode(mode);

            ScreenContainer.Instance.ApplyModeVisibility();
        }

        private void ShowModeMenu()
        {
            ModeMenu.Show(EditorModes.All, _renderer.Mode, ModeRect, SetMode);
        }

        public void Load(string path)
        {
            _renderer.Load(path);
        }

        public void Load(SceneNode node)
        {
            _renderer.Load(node);
        }

        public void Load(IEnumerable<string> paths)
        {
            foreach (var path in paths)
                _renderer.Load(path);
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            Width = TabNode.Rect.Width;

            Height = TabNode.Rect.Height;

            Position = TabNode.Rect.Location.ToVector2();

            _hoverMode = ModeRect.Contains(UserInput.GetMousePos().ToPoint());

            _renderer.ViewportRect = Viewport;

            _renderer.Update();

            GameHost.Instance.Window.Title = _renderer.Title;
        }

        private bool _exporting;

        public void SaveProject()
        {
            var path = ProjectDialog.AskSave(_renderer.ProjectPath);

            if (path == null)
                return;

            _renderer.ProjectPath = path;

            var message = ProjectFile.Save(_renderer, path);

            _renderer.LastExportMessage = message;

        }

        public void ExportWithDialog()
        {
            if (_exporting)
                return;

            if (_renderer.SingleSelectedObject() == null)
            {
                _renderer.LastExportMessage = "请先在列表里选中一个模型";


                return;
            }

            _exporting = true;

            string? path;

            try
            {
                path = SaveDialog.Ask(_renderer.DefaultExportPath());
            }
            finally
            {
                _exporting = false;
            }

            if (path == null)
            {

                return;
            }


            _renderer.ExportCurrent(path);
        }

        public void CancelInteraction()
        {
            _boxStart = null;

            _dragBox = Rectangle.Empty;

            if (_renderer.Vertices.Dragging)
                _renderer.Vertices.CancelDrag();

            ContextMenu.Close();

            ModeMenu.Close();

            UIManager.Instance.CapturedComponent = null;
        }

        public override InputResult OnInput(InputEventArgs args)
        {
            if (TransformOperator.Instance.HandleInput(args, out var transformRecord))
            {
                if (transformRecord != null)
                    _renderer.History.Record(transformRecord);

                UIManager.Instance.CapturedComponent = TransformOperator.Instance.Active ? this : null;

                return InputResult.Consumed;
            }

            switch (args.Type)
            {
                case InputType.KeyDown:
                    return HandleKey(args.Key);

                case InputType.KeyUp:
                    return InputResult.Consumed;
            }

            if (HandleHeader(args))
                return InputResult.Consumed;

            if (TabNode.IsOverTabBar(args.MousePosition.ToPoint()))
                return InputResult.Continue;

            if (args.Type != InputType.MouseLeftUp && !Rectangle.Contains(args.MousePosition))
                return InputResult.Continue;

            switch (args.Type)
            {
                case InputType.MouseMove:
                    if (_renderer.Vertices.Dragging)
                    {
                        _renderer.Vertices.UpdateDrag((int)args.MousePosition.X, (int)args.MousePosition.Y);

                        return InputResult.Consumed;
                    }

                    if (_boxStart.HasValue)
                    {
                        _dragBox = MakeBox(_boxStart.Value, args.MousePosition.ToPoint());

                        _renderer.Vertices.UpdateBox(_boxStart.Value, args.MousePosition.ToPoint());

                        return InputResult.Consumed;
                    }

                    if (UserInput.CurrentMouseState.MiddleButton == ButtonState.Pressed)
                    {
                        if (UserInput.Shift)
                            _renderer.Pan((int)args.MouseDelta.X, (int)args.MouseDelta.Y);
                        else
                            _renderer.Rotate((int)args.MouseDelta.X, (int)args.MouseDelta.Y);
                    }

                    return InputResult.Consumed;

                case InputType.MouseWheel:
                    _renderer.Zoom(args.WheelDelta);

                    return InputResult.Consumed;

                case InputType.MouseLeftDown:
                    if (_renderer.IsEditMode && !args.Ctrl && !args.Shift
                        && _renderer.Vertices.Pick((int)args.MousePosition.X, (int)args.MousePosition.Y) is { } hit
                        && _renderer.Vertices.Contains(hit)
                        && _renderer.Vertices.BeginDrag((int)args.MousePosition.X, (int)args.MousePosition.Y))
                    {
                        UIManager.Instance.CapturedComponent = this;

                        return InputResult.Consumed;
                    }

                    _boxStart = args.MousePosition.ToPoint();

                    _dragBox = new Rectangle(_boxStart.Value.X, _boxStart.Value.Y, 0, 0);

                    if (_renderer.IsEditMode)
                        _renderer.Vertices.BeginBox(_boxStart.Value);

                    UIManager.Instance.CapturedComponent = this;

                    return InputResult.Consumed;

                case InputType.MouseLeftUp:
                    if (_renderer.Vertices.Dragging)
                    {
                        var moveRecord = _renderer.Vertices.EndDrag();

                        if (moveRecord != null)
                            _renderer.History.Record(moveRecord);

                        UIManager.Instance.CapturedComponent = null;

                        return InputResult.Consumed;
                    }

                    if (_boxStart.HasValue)
                    {
                        var box = _dragBox;

                        _boxStart = null;

                        _dragBox = null;

                        _renderer.Vertices.EndBox();

                        if (box is { } area)
                        {
                            var tiny = area.Width <= 3 && area.Height <= 3;

                            if (_renderer.IsEditMode)
                            {
                                if (tiny)
                                {
                                    var vertex = _renderer.Vertices.Pick((int)args.MousePosition.X, (int)args.MousePosition.Y);

                                    if (args.Ctrl)
                                        _renderer.Vertices.Toggle(vertex);
                                    else
                                        _renderer.Vertices.Select(vertex);
                                }
                                else
                                {
                                    _renderer.Vertices.SelectIn(area);
                                }
                            }
                            else if (tiny)
                            {
                                _renderer.SelectAt((int)args.MousePosition.X, (int)args.MousePosition.Y, args.Shift, args.Ctrl);
                            }
                            else
                            {
                                _renderer.SelectInBox(area, args.Shift, args.Ctrl);
                            }
                        }
                    }

                    UIManager.Instance.CapturedComponent = null;

                    return InputResult.Consumed;
            }

            return InputResult.Continue;
        }

        public Rectangle? DragBox => _dragBox;

        private bool HandleHeader(InputEventArgs args)
        {
            if (_boxStart.HasValue || _renderer.Vertices.Dragging || TransformOperator.Instance.Active)
                return false;

            var header = HeaderRect;

            var over = header.Contains(args.MousePosition.ToPoint());

            switch (args.Type)
            {
                case InputType.MouseMove:
                    return over;

                case InputType.MouseLeftDown:
                    if (!over)
                        return false;

                    if (ModeRect.Contains(args.MousePosition.ToPoint()))
                    {
                        ShowModeMenu();

                        return true;
                    }

                    var panel = PanelButtonAt(HeaderRect, args.MousePosition.ToPoint());

                    if (panel >= 0)
                    {
                        ScreenContainer.Instance.TogglePanel(PanelButtons()[panel].Panel);

                        return true;
                    }

                    return true;

                case InputType.MouseLeftUp:
                case InputType.MouseRightDown:
                case InputType.MouseRightUp:
                case InputType.MouseWheel:
                    return over;
            }

            return false;
        }

        private static Rectangle MakeBox(Point a, Point b) =>
            new(System.Math.Min(a.X, b.X), System.Math.Min(a.Y, b.Y), System.Math.Abs(b.X - a.X), System.Math.Abs(b.Y - a.Y));

        private Point? _boxStart;

        private Rectangle? _dragBox;

        private const float BonePickRadius = 6f;

        private void StartTransform(TransformMode mode)
        {
            var vertices = TransformSelection();

            if (vertices.Count == 0)
                return;

            var mouse = UserInput.CurrentMouseState;

            if (!TransformOperator.Instance.Begin(_renderer, vertices, mode, mouse.X, mouse.Y, holdToDrag: false))
                return;

            UIManager.Instance.CapturedComponent = this;
        }

        private List<VertexRef> TransformSelection()
        {
            if (_renderer.IsEditMode)
                return [.. _renderer.Vertices.Selected];

            var vertices = new List<VertexRef>();

            foreach (var shape in _renderer.Selection)
                for (var i = 0; i < shape.VertexCount; i++)
                    vertices.Add(new VertexRef(shape, i));

            return vertices;
        }

        private InputResult HandleKey(Keys key)
        {
            switch (key)
            {
                case Keys.Tab:
                {
                    var modes = EditorModes.All;

                    var index = System.Array.IndexOf(modes, _renderer.Mode);

                    if (index < 0)
                        index = 0;

                    for (var step = 1; step <= modes.Length; step++)
                    {
                        var candidate = modes[(index + step) % modes.Length];

                        SetMode(candidate);

                        if (_renderer.Mode == candidate)
                            break;
                    }

                    break;
                }

                case Keys.Delete:
                    if (_renderer.IsEditMode && _renderer.Vertices.SelectionCount > 0)
                        _renderer.Vertices.DeleteSelected();
                    else
                        _renderer.DeleteSelected();

                    break;

                case Keys.S when UserInput.Ctrl:
                    SaveProject();

                    break;

                case Keys.S when _renderer.IsEditMode || _renderer.SelectedShapeCount > 0:
                    StartTransform(new ResizeMode());

                    break;

                case Keys.S:
                    _renderer.EnsureVisible();

                    break;

                case Keys.G when _renderer.IsEditMode || _renderer.SelectedShapeCount > 0:
                    StartTransform(new TranslateMode());

                    break;

                case Keys.F:
                    _renderer.Frame();

                    break;

                case Keys.OemPeriod:
                    _renderer.EnsureVisible();

                    break;

                case Keys.A when UserInput.Ctrl:
                    if (_renderer.IsEditMode)
                        _renderer.Vertices.SelectAll();
                    else
                        _renderer.SelectAllObjects();

                    break;

                case Keys.R when _renderer.IsEditMode || _renderer.SelectedShapeCount > 0:
                    StartTransform(new RotateMode());

                    break;

                case Keys.R:
                case Keys.Home:
                    _renderer.ResetView();

                    break;

                case Keys.F3:
                    _renderer.ToggleWireframe();

                    break;

                default:
                    return InputResult.Continue;
            }

            return InputResult.Consumed;
        }

        public override void Draw(SpriteBatch spriteBatch, GameTime gameTime)
        {
            var device = spriteBatch.GraphicsDevice;

            var savedViewport = device.Viewport;

            var savedScissor = device.ScissorRectangle;

            spriteBatch.End();

            _renderer.ViewportRect = Viewport;

            _renderer.Draw(device);

            device.Viewport = savedViewport;

            device.ScissorRectangle = savedScissor;

            device.BlendState = BlendState.AlphaBlend;

            device.DepthStencilState = DepthStencilState.None;

            device.RasterizerState = UIManager.ScissorRasterizer;

            spriteBatch.Begin(rasterizerState: UIManager.ScissorRasterizer);

            DrawHeader(spriteBatch);
        }

        private void DrawHeader(SpriteBatch spriteBatch)
        {
            var header = HeaderRect;

            if (header.Width <= 0 || header.Height <= 0)
                return;

            spriteBatch.DrawRectangle(header, UITheme.HeaderBackground);

            DrawModeButton(spriteBatch);

            DrawPanelButtons(spriteBatch, header);

            DrawHeaderInfo(spriteBatch, header);
        }

        public override bool HandlesHeaderInput(Point position)
        {
            if (!HeaderRect.Contains(position))
                return false;

            if (ModeRect.Contains(position))
                return true;

            return PanelButtonAt(HeaderRect, position) >= 0;
        }

        private (DockPanel? Panel, string Icon)[] PanelButtons()
        {
            var screen = ScreenContainer.Instance;

            return
            [
                (screen.Meshes, "panel-meshes.svg"),
                (screen.Sliders, "panel-slider.svg")
            ];
        }

        private static Rectangle PanelButtonRect(Rectangle header, int index)
        {
            var size = UIScale.Value(20);

            var gap = UIScale.Value(2);

            var x = header.Right - HeaderPad - (index + 1) * size - index * gap;

            return new Rectangle(x, header.Y + (header.Height - size) / 2, size, size);
        }

        private int PanelButtonAt(Rectangle header, Point position)
        {
            var buttons = PanelButtons();

            for (var i = 0; i < buttons.Length; i++)
            {
                if (buttons[i].Panel != null && PanelButtonRect(header, i).Contains(position))
                    return i;
            }

            return -1;
        }

        private void DrawPanelButtons(SpriteBatch spriteBatch, Rectangle header)
        {
            var buttons = PanelButtons();

            var radius = UIMetrics.WidgetRoundness(0.2f);

            for (var i = 0; i < buttons.Length; i++)
            {
                if (buttons[i].Panel == null)
                    continue;

                var box = PanelButtonRect(header, i);

                if (box.X < ModeRect.Right + HeaderPad)
                    continue;

                var docked = ScreenContainer.Instance.IsPanelDocked(buttons[i].Panel);

                var hover = box.Contains(UserInput.GetMousePos().ToPoint());

                var fill = docked ? UITheme.HeaderButton : UITheme.HeaderButtonHover;

                if (hover)
                    fill = docked ? UITheme.RowSelected : UITheme.HeaderButtonOutline;

                var outline = hover ? UITheme.Accent : UITheme.HeaderButtonOutline;

                var rounded = SkiaCard.Rounded(spriteBatch.GraphicsDevice, box.Width, box.Height, radius, fill.ToSk(), outline.ToSk(), 1f);

                if (rounded != null)
                    spriteBatch.Draw(rounded, box, Color.White);
                else
                    spriteBatch.DrawRectangle(box, UITheme.Premultiply(fill));

                var color = docked || hover ? UITheme.HeaderButtonTextHover : UITheme.HeaderButtonText;

                var size = UIScale.Value(12);

                var icon = SkiaIcon.Load(spriteBatch.GraphicsDevice, buttons[i].Icon, size, new SKColor(color.R, color.G, color.B, color.A));

                if (icon != null)
                {
                    var glyph = new Rectangle(box.X + (box.Width - size) / 2, box.Y + (box.Height - size) / 2, size, size);

                    spriteBatch.Draw(icon, glyph, new Rectangle(0, 0, icon.Width, icon.Height), Color.White);
                }
            }
        }

        private void DrawModeButton(SpriteBatch spriteBatch)
        {
            var button = ModeRect;

            var fill = _hoverMode ? UITheme.HeaderButtonHover : UITheme.HeaderButton;

            var radius = UIMetrics.WidgetRoundness(0.2f);

            var fillSk = fill.ToSk();

            var rounded = SkiaCard.Rounded(spriteBatch.GraphicsDevice, button.Width, button.Height, radius, fillSk, UITheme.HeaderButtonOutline.ToSk(), 1f);

            if (rounded != null)
                spriteBatch.Draw(rounded, button, Color.White);
            else
                spriteBatch.DrawRectangle(button, UITheme.Premultiply(fill));

            var textColor = _hoverMode ? UITheme.HeaderButtonTextHover : UITheme.HeaderButtonText;

            var tint = new SKColor(textColor.R, textColor.G, textColor.B, textColor.A);

            var glyph = new Rectangle(button.X + ModeGlyphInset, button.Y + (button.Height - ModeGlyphSize) / 2, ModeGlyphSize, ModeGlyphSize);

            var glyphIcon = SkiaIcon.Load(spriteBatch.GraphicsDevice, _renderer.Mode.Icon(), glyph.Width, tint);

            if (glyphIcon != null)
                spriteBatch.Draw(glyphIcon, glyph, new Rectangle(0, 0, glyphIcon.Width, glyphIcon.Height), Color.White);

            var text = _renderer.Mode.Label();

            var textY = button.Y + (button.Height - UIScale.Value(HeaderTextSize)) / 2;

            UIFont.Draw(spriteBatch, text, new Vector2(button.X + ModeTextInset, textY), textColor, HeaderTextSize);

            var caret = new Rectangle(button.Right - HeaderPad - ModeCaretSize, button.Y + (button.Height - ModeCaretSize) / 2, ModeCaretSize, ModeCaretSize);

            var icon = SkiaIcon.Load(spriteBatch.GraphicsDevice, "caret-down.svg", caret.Width, tint);

            if (icon != null)
                spriteBatch.Draw(icon, caret, new Rectangle(0, 0, icon.Width, icon.Height), Color.White);
        }

        private void DrawHeaderInfo(SpriteBatch spriteBatch, Rectangle header)
        {
            var text = HeaderInfo();

            if (text.Length == 0)
                return;

            var size = UIFont.Measure(text, HeaderInfoSize);

            var right = header.Right;

            if (right > header.Right - HeaderPad)
                right = header.Right - HeaderPad;

            var buttonsLeft = PanelButtonRect(header, PanelButtons().Length - 1).X - HeaderPad;

            if (right > buttonsLeft)
                right = buttonsLeft;

            var x = right - HeaderPad - size.X;

            if (x < ModeRect.Right + HeaderPad)
                return;

            UIFont.Draw(spriteBatch, text, new Vector2(x, header.Y + (header.Height - UIScale.Value(HeaderInfoSize)) / 2), UITheme.TextDim, HeaderInfoSize);
        }

        private string HeaderInfo()
        {
            switch (_renderer.Mode)
            {
                case EditorMode.Edit:
                    return $"verts {_renderer.Vertices.SelectionCount}   selected {_renderer.SelectedShapeCount}";

                case EditorMode.Slider:
                    return ScreenContainer.Instance.Sliders is { } sliders
                        ? $"{Plural(sliders.Rows.Count, "slider")}   {Plural(sliders.Targets.Count, "shape")}"
                        : "";

                default:
                    return $"{Plural(_renderer.Objects.Count, "object")}   selected {_renderer.SelectedObjectCount}";
            }
        }

        private static string Plural(int count, string word) => count == 1 ? $"{count} {word}" : $"{count} {word}s";
    }
}
