using System.Windows;
using Shabpa.Kit;
using Xunit;

namespace Shabpa.Tests;

[Collection("Wpf")]
public class MainWindowClaimTests
{
    [Fact]
    public void AnEarlierWindowDoesNotStayTheMainWindow()
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try
            {
                var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                var notice = new Window();   // opened before the real one
                var real = new Window();
                app.MainWindow = notice;
                MainWindowClaim.Adopt(app, real);
                Assert.Same(real, app.MainWindow);
                MainWindowClaim.Adopt(app, real);   // idempotent
                Assert.Same(real, app.MainWindow);
            }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        Assert.Null(error);
    }
}
