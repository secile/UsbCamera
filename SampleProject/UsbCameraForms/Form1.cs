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

namespace UsbCameraForms
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
            foreach (var item in formats) Console.WriteLine(item);
            var format = formats[0];

            // create instance.
            var camera = new UsbCamera(cameraIndex, format);
            // this closing event handler make sure that the instance is not subject to garbage collection.
            this.FormClosing += (s, ev) => camera.Release(); // release when close.

            // show preview on control. (works light.)
            camera.SetPreviewControl(pictureBox1.Handle, pictureBox1.ClientSize);
            pictureBox1.Resize += (s, ev) => camera.SetPreviewSize(pictureBox1.ClientSize); // support resize.

            // or use this conventional way. (works little heavy)
            //var timer = new System.Timers.Timer(1000 / 30) { SynchronizingObject = this };
            //timer.Elapsed += (s, ev) => pictureBox1.Image = camera.GetBitmap();
            //timer.Start();
            //this.FormClosing += (s, ev) => timer.Stop();

            // start.
            camera.Start();

            // get bitmap.
            button1.Click += (s, ev) => pictureBox2.Image = camera.GetBitmap();
        }
    }
}
