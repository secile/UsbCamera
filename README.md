# UsbCamera
C# source code for using USB camera in WinForms.  
With only single CSharp source code. No need external library.

# How to use
Add UsbCamera.cs to your project.    
```C#
// check USB camera is available.
string[] devices = UsbCamera.FindDevice();
if (devices.Length == 0) return; // no camera.

// create USB camera and start.
var index = 0;
var camera = new UsbCamera(index, new Size(640, 480));
camera.Start();

// [wait a few seconds until image buffer filled.]

// get image.
var bmp = camera.GetBitmap();

// If you want to display image in PictureBox
int fps = 10;
var timer = new System.Timers.Timer(1000.0 / fps) { SynchronizingObject = this };
timer.Elapsed += (s, ev) => pbxImage.Image = camera.GetBitmap();
timer.Start();

// Release resources.
myForm.FormClosing += (s, ev) =>
{
    timer.Stop();
    camera.Release();
};
```

# No need external library.
Previos version, this project used external library.
But now, no need to add external library to your project.

# Special Thanks.
This project make use of a part of source code of the project below. Thank you!   
* DShowLib (author: Mr. M.Oshikiri)  
    - http://www.geocities.co.jp/SiliconValley/7406/tips/dshowdll/dshowdll0.html  
* DSLab (author: cogorou)  
    - https://github.com/cogorou/DSLab

# And...
You can make H264 recorder by using UsbCamera and OpenH264Lib.NET and AviWriter(in MotionJPEGWriter).  
See README.md in OpenH264.NET.
