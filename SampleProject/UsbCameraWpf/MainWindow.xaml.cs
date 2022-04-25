using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using GitHub.secile.Video;

namespace UsbCameraWpf
{
    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // find device.
            var devices = UsbCamera.FindDevices();
            if (devices.Length == 0) return; // no device.

            // get video format.
            var cameraIndex = 0;
            var formats = UsbCamera.GetVideoFormat(cameraIndex);

            // select the format you want.
            foreach (var item in formats) Console.WriteLine(item);
            var format = formats[0];

            // create instance.
            var camera = new UsbCamera(cameraIndex, format);
            // this closing event hander make sure that the instance is not subject to garbage collection.
            this.Closing += (s, ev) => camera.Release(); // release when close.

            // show preview on control. (works light.)
            // SetPreviewControl requires window handle but WPF control does not have handle.
            // it is recommended to use PictureBox with WindowsFormsHost.
            // or use handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            var handle = pictureBox.Handle;
            camera.SetPreviewControl(handle, new Size(320, 240));

            // or use this conventional way. (works little heavy)
            //var timer = new System.Timers.Timer(1000 / 30);
            //timer.Elapsed += (s, ev) => Dispatcher.Invoke(() => image.Source = camera.GetBitmap());
            //timer.Start();
            //this.Closing += (s, ev) => timer.Stop();

            // start.
            camera.Start();

            // get bitmap.
            button.Click += (s, ev) => image.Source = camera.GetBitmap();
        }
    }
}
