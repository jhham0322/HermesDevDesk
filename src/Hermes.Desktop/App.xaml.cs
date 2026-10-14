using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Hermes.Desktop;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var dataIndex = Array.IndexOf(e.Args, "--data-dir");
            var data = dataIndex >= 0 && e.Args.Length > dataIndex + 1 ? e.Args[dataIndex + 1] : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HermesDevDesk", "0.7.0");
            var window = new MainWindow(data); MainWindow = window;
            if (e.Args.Contains("--ui-smoke"))
            {
                window.Width = 1280; window.Height = 1392;
                window.Show();
                window.Dispatcher.BeginInvoke(async () =>
                {
                    try
                    {
                        var checks = await window.RunProgrammaticUiChecks(data);
                        window.UpdateLayout();
                        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using var stream = File.Create(Path.Combine(data, "ui-smoke.png")); encoder.Save(stream);
                        File.WriteAllText(Path.Combine(data, "ui-smoke.txt"), "WPF window loaded on .NET 10.\n" + checks);
                        await window.ShutdownController(); Shutdown(0);
                    }
                    catch (Exception ex) { File.WriteAllText(Path.Combine(data, "ui-smoke-error.txt"), ex.ToString()); Shutdown(1); }
                });
            }
            else window.Show();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Hermes DevDesk 시작 실패"); Shutdown(1); }
    }
}
