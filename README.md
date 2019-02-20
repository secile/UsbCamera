# UsbCamera
C# source code for using USB camera in WinForms.

# How to use
Add a reference for 'DShowLib.dll' to your project.  
```C#
// check USB camera is available.
string[] devices = UsbCamera.FindDevice();
if (devices.Length == 0)
{
    MessageBox.Show("USB camera isnot available.");
    return;
}

// create USB camera and start.
var index = 0;
var camera = new UsbCamera(index, new Size(640, 480));
camera.Start();

// [A few seconds later...]

// get image.
var bmp = camera.GetBitmap();

// [If you want to display image in PictureBox]
int fps = 10;
var timer = new System.Timers.Timer(1000.0 / fps) { SynchronizingObject = this };
timer.Elapsed += (s, ev) => pbxImage.Image = camera.GetBitmap();
timer.Start();
```

# About 'DShowLib.dll'
This source code uses external library called 'DShowLib'.  
DShowLib is class library to use DirectShow from C#/VB.net created by M.Oshikiri.  
The library is free to use, permitted re-distribution even if commercial use.  
DShowLib is distributed on the following pages.  
http://www.geocities.co.jp/SiliconValley/7406/tips/dshowdll/dshowdll0.html  
Thank you Mr. M.Oshikiri.

# And...
You can make H264 recorder by using UsbCamera and OpenH264Lib.NET and AviWriter(in MotionJPEGWriter).  
See README.md in OpenH264.NET.
