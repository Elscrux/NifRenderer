using System;
using Avalonia.Controls;
using NifRenderer;

namespace MyGame;

public partial class MainWindow : Window
{
    private readonly GameHost _game;

    public MainWindow()
    {
        InitializeComponent();

        NifRenderer.Shader.FxCompiler.Prepare("Content/Shaders/NifRender.fx", "Content/Shaders/NifRender.mgfx");

        _game = new GameHost();

        // _game.Run();

        // SetGame() is shorthand for GameHost.Game = _game.
        // MonoGameControl calls game.Run() automatically on the first render
        // frame — do not call it separately, or MonoGame will double-initialise.
        //
        // For MVVM patterns, set the Game property directly from a ViewModel:
        //   <mg:MonoGameControl Game="{Binding Game}" />
        Host.Game = _game;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        // Dispose stops the audio thread, releases GL buffers, and closes the
        // SDL window. Omitting this can leave the process hanging after the
        // Avalonia window has closed because MonoGame's audio thread is still
        // running.
        _game.Dispose();
    }
}
