using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shabpa.Kit;

namespace Shabpa.Tests;

public class ThemedShadowTests
{
    [Fact]
    public void Themed_shadow_colour_follows_the_resource_when_it_is_replaced()
    {
        Color? before = null, after = null;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var host = new Grid();
                host.Resources["ShadowSoftC"] = Color.FromRgb(1, 2, 3);
                var shadow = Shadow.Themed(new Shadow(), "ShadowSoftC");
                host.Children.Add(shadow);
                before = shadow.Color;
                host.Resources["ShadowSoftC"] = Color.FromRgb(9, 8, 7); // what Ui.SetTheme does to the live dictionary
                after = shadow.Color;
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new Exception("STA test failed", error);
        Assert.Equal(Color.FromRgb(1, 2, 3), before);
        Assert.Equal(Color.FromRgb(9, 8, 7), after);
    }
}
