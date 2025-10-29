using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using GitHub.secile.Video;

// This is a sample of using gray scale camera that supports Y8, Y800, and Y16 video format.
// This program is tested on the product of TheImagingSource USB camera.
// 
// Y800(and Y8) is an 8-bit, Y16 is a 16-bit grayscale format.
// To use these video format, please follow the steps below.
//
// (1) define 'USBCAMERA_BYTEARRAY' symbol, that makes GetBitmap() returns image data as byte array.
//     in case on WPF, define 'USBCAMERA_WPF' symbol at a same time.
//
// (2) select VideoFormat which SubType is Y800, Y8, or Y16.
//
// (3) to preview the image, do not use SetPreviewControl on WinForms.
//     use Timer and GetBitmap() on WinForms, or use PreviewCaptured on WPF.
//
// (4) to show the image, you have to convert byte array to Bitmap(on WinForms) or BitmapSource(on WPF),
//     use UsbCamera.ByteArrayUtility.Y800.CreateBitmap or UsbCamera.ByteArrayUtility.Y16.CreateBitmap function.
//
// note on using Y16 video format.
//
// (5) Y16 is a 16-bit grayscale format, but in general, C# Bitmap class is not able to handle 16-bit grayscale image properly.
//     UsbCamera.ByteArrayUtility.Y16.CreateBitmap returns 8-bit grayscale image, which causes the lost of pixel bit depth.
//     do not use the Bitmap to inspect the luminance value of image, use this only for preview.
//
// (6) to inspect/manipulate the luminance value of specific coordinates from byte array,
//     use UsbCamera.ByteArrayUtility.Y16.GetValue/SetValue function.
//
// (7) Y16 video format has it's own bit depth(8, 10 or 12) and it's depend of your cameras spec.
//     you must specify the bit depth as an argument when you call the UsbCamera.ByteArrayUtility.Y16.CreateBitmap, GetValue, SetValue function.

namespace UsbCameraByteArray
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            // find device.
            var devices = UsbCamera.FindDevices();
            if (devices.Length == 0) return; // no device.

            // get video format.
            var cameraIndex = 0;
            var formats = UsbCamera.GetVideoFormat(cameraIndex);

            // select the format you want.
            for (int i = 0; i < formats.Length; i++) Console.WriteLine("{0}:{1}", i, formats[i]);

            // *** select Y16 video format here. ***
            // if you use Y800, select Y800, and use UsbCamera.ByteArrayUtility.Y800 functions.
            var format = formats[0];

            // create instance.
            var camera = new UsbCamera(cameraIndex, format);
            // this closing event handler make sure that the instance is not subject to garbage collection.
            this.FormClosing += (s, ev) => camera.Release(); // release when close.

            // do not use SetPreviewControl, use Timer and GetBitmap().
            var timer = new System.Timers.Timer(1000 / 30) { SynchronizingObject = this };
            timer.Elapsed += (s, ev) =>
            {
                var buffer = (byte[])camera.GetBitmap(); // define 'USBCAMERA_BYTEARRAY' symbol, then GetBitmap() returns byte array of Y16 raw data.
                var bmp = UsbCamera.ByteArrayUtility.Y16.CreateBitmap(buffer, 12, camera.Size.Width, camera.Size.Height); // convert it to Bitmap.
                pictureBox1.Image = bmp;
            };

            timer.Start();
            this.FormClosing += (s, ev) => timer.Stop();

            // start.
            camera.Start();

            // get bitmap.
            button1.Click += (s, ev) =>
            {
                var buffer = (byte[])camera.GetBitmap();

                // get/set luminance value sample.
                // execute binarization.
                for (int y = 0; y < camera.Size.Height; y++)
                {
                    for (int x = 0; x < camera.Size.Width; x++)
                    {
                        var value = UsbCamera.ByteArrayUtility.Y16.GetValue(buffer, 12, camera.Size.Width, camera.Size.Height, x, y);
                        value = (ushort)(value > 0x0FFF / 2 ? 0x0FFF : 0);
                        UsbCamera.ByteArrayUtility.Y16.SetValue(buffer, 12, camera.Size.Width, camera.Size.Height, x, y, value);
                    }
                }

                pictureBox2.Image = UsbCamera.ByteArrayUtility.Y16.CreateBitmap(buffer, 12, camera.Size.Width, camera.Size.Height);
            };
        }
    }
}
