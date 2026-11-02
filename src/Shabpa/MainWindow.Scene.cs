using System.Windows;
using System.Windows.Controls;
using Shabpa.Core;
using Shabpa.Kit;

namespace Shabpa;

/// <summary>The animated backdrop of a bought premium theme: one layer behind the pages, swapped when the theme changes.</summary>
public partial class MainWindow
{
    private ThemeScene? _scene;

    /// <summary>Test aid: which backdrop is on (None when the theme has none).</summary>
    public Scene SceneKind => _scene?.Kind ?? Scene.None;

    private void UpdateScene()
    {
        var want = PremiumTheme.Find(Ui.BaseTheme.Key)?.Scene ?? Scene.None;
        if (want == SceneKind) return;
        if (_scene is not null) { Root.Children.Remove(_scene); _scene = null; }
        if (want == Scene.None) return;
        _scene = new ThemeScene(want) { IsHitTestVisible = false };
        Root.Children.Insert(0, _scene);
    }
}
