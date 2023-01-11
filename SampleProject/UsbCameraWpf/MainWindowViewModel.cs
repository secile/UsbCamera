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
            set
            {
                if (_Preview != value)
                    _Preview = value;
                OnPropertyChanged();
            }
        }

        private BitmapSource _Capture;
        public BitmapSource Capture
        {
            get { return _Capture; }
            set
            {
                _Capture = value;
                OnPropertyChanged();
            }
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

            // show preview on control. (works light, but you can't use WPF merit, e.g. Transform.)
            // SetPreviewControl requires window handle but WPF control does not have handle.
            // it is recommended to use PictureBox with WindowsFormsHost.
            // or use handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            //var handle = pictureBox.Handle passed from MainWindow.xaml.
            //camera.SetPreviewControl(handle, new System.Windows.Size(320, 240));

            // update preview. (works little heavy)
            var timer = new System.Windows.Threading.DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(1000.0 / 30);
            timer.Tick += (s, ev) => Preview = camera.GetBitmap();
            timer.Start();

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
