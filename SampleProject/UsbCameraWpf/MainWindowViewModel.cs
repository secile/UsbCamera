using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using GitHub.secile.Video;

namespace UsbCameraWpf
{
    class MainWindowViewModel : ViewModel
    {
        private BitmapSource _Preview;
        public BitmapSource Preview
        {
            get { return _Preview; }
            set { RaiseAndSetIfChanged(ref _Preview, value); }
        }

        private BitmapSource _Capture;
        public BitmapSource Capture
        {
            get { return _Capture; }
            set { RaiseAndSetIfChanged(ref _Capture, value); }
        }

        public ICommand GetBitmap { get; private set; }

        public ICommand GetStillImage { get; private set; }

        public MainWindowViewModel()
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

            // to show preview, there are 3 ways.
            // 1. subscribe PreviewCaptured. (recommended.)
            camera.PreviewCaptured += (bmp) =>
            {
                // passed image can only be used for preview with data binding.
                // the image is single instance and updated by library. DO NOT USE for other purpose.
                Preview = bmp;
            };

            // 2. use Timer and GetBitmap().
            //var timer = new System.Windows.Threading.DispatcherTimer();
            //timer.Interval = TimeSpan.FromMilliseconds(1000.0 / 30);
            //timer.Tick += (s, ev) => Preview = camera.GetBitmap();
            //timer.Start();

            // 3. use SetPreviewControl and WindowsFormshost. (works light, but you can't use WPF merit.)
            // SetPreviewControl requires window handle but WPF control does not have handle.
            // it is recommended to use PictureBox with WindowsFormsHost.
            // or use handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            //var handle = pictureBox.Handle passed from MainWindow.xaml.
            //camera.SetPreviewControl(handle, new System.Windows.Size(320, 240));

            // start.
            camera.Start();

            GetBitmap = new RelayCommand(() => Capture = camera.GetBitmap());

            if (camera.StillImageAvailable)
            {
                GetStillImage = new RelayCommand(() => camera.StillImageTrigger());
                camera.StillImageCaptured += bmp => Capture = bmp;
            }
        }
    }
}
