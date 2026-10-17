using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using NifRenderer.UI;
using NifRenderer.UI.Components;
using NifRenderer.UI.Components.Dockers;
using NifRenderer.Utils;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace NifRenderer
{
    public class GameHost : Game
    {
        public static GameHost Instance;

        private GraphicsDeviceManager _graphics;

        private SpriteBatch _spriteBatch;

        private UIManager? _uiManager;

        private string[] _arguments;

        private bool _titleBarApplied;

        public GameHost() : this([])
        {
        }

        public GameHost(string[] arguments)
        {
            _arguments = arguments ?? [];

            _graphics = new GraphicsDeviceManager(this)
            {
                PreferredDepthStencilFormat = DepthFormat.Depth24Stencil8
            };

            Content.RootDirectory = "Content";

            IsMouseVisible = true;

            Window.FileDrop += Window_FileDrop;

            Window.TextInput += Window_TextInput;




            Instance = this;
        }

        private void Window_TextInput(object sender, TextInputEventArgs args)
        {
            UIManager.Instance?.DispatchTextInput(args.Character);
        }

        private void Window_FileDrop(object sender, FileDropEventArgs e)
        {
            foreach (var file in e.Files)
                NifDocker.Instance.Load(file);
        }

        protected override void Initialize()
        {
            WindowUtils.Hide();

            _graphics.PreferredBackBufferWidth = 2000;

            _graphics.PreferredBackBufferHeight = 1000;

            _graphics.PreferMultiSampling = true;

            _graphics.IsFullScreen = false;

            _graphics.ApplyChanges();

            _spriteBatch = new(_graphics.GraphicsDevice);

            _uiManager = new(new ScreenContainer());

            UIManager.Instance = _uiManager;

            base.Initialize();
        }

        protected override void LoadContent()
        {
        }

        protected override void Update(GameTime gameTime)
        {
            try
            {
                UserInput.Update(UserInput.UsesExternalInput
                    ? UserInput.HasFocus
                    : IsActive && WindowUtils.HasFocus);

                _uiManager.Update(gameTime);

                base.Update(gameTime);
            }
            catch (Exception exception)
            {
            }
        }

        protected override void Draw(GameTime gameTime)
        {
            try
            {
                DrawFrame(gameTime);
            }
            catch (Exception exception)
            {

                try
                {
                    GraphicsDevice.SetRenderTarget(null);
                }
                catch
                {
                }

                try
                {
                    _spriteBatch.End();
                }
                catch
                {
                }
            }
            finally
            {
                if (!_windowShown)
                {
                    _windowShown = true;

                    WindowUtils.Show();
                }
            }
        }

        private bool _windowShown;

        private void DrawFrame(GameTime gameTime)
        {
            if (!_titleBarApplied)
                _titleBarApplied = WindowUtils.ApplyTitleBarColor(UITheme.ScreenBackground, UITheme.TextPrimary);

            GraphicsDevice.SetRenderTarget(null);

            GraphicsDevice.Viewport = new Viewport(0, 0, GraphicsDevice.PresentationParameters.BackBufferWidth, GraphicsDevice.PresentationParameters.BackBufferHeight);

            GraphicsDevice.ScissorRectangle = GraphicsDevice.Viewport.Bounds;

            GraphicsDevice.Clear(ClearOptions.Target | ClearOptions.DepthBuffer | ClearOptions.Stencil, ScreenContainer.ScreenBackground, 1f, 0);

            if (!SkiaGameRendering.SkiaRenderer.IsInitialized && SkiaGameRendering.SkiaRenderer.IsReady)
                SkiaGameRendering.SkiaRenderer.Initialize(GraphicsDevice);

            _uiManager.Draw(_spriteBatch, gameTime);

            if (_uiManager.Root is ScreenContainer screen)
                screen.DrawPanelBorders();

            base.Draw(gameTime);
        }
    }
}
