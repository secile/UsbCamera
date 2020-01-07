# UsbCamera
C# source code for using USB camera in WinForms.  
With only single CSharp source code. No need external library.

# How to use
Add UsbCamera.cs to your project.    
```C#
// [How to use]
// check USB camera is available.
string[] devices = UsbCamera.FindDevices();
if (devices.Length == 0) return; // no camera.
            
// check format.
UsbCamera.VideoFormat[] formats = UsbCamera.GetVideoFormat(0);
foreach (var item in formats) Console.WriteLine("{0}-{1}", Array.IndexOf(formats, item), item);
            
// create usb camera and start.
var index = 0;
var camera = new UsbCamera(index, formats[0]);
camera.Start();
            
// get image.
// Immediately after starting the USB camera,
// GetBitmap() fails because image buffer is not prepared yet.
var bmp = camera.GetBitmap();
            
// show image in PictureBox.
var timer = new System.Timers.Timer(100) { SynchronizingObject = this };
timer.Elapsed += (s, ev) => pbxScreen.Image = camera.GetBitmap();
timer.Start();

// release resource when close.
this.FormClosing += (s, ev) => timer.Stop();
this.FormClosing += (s, ev) => camera.Stop();
```

# Adjust Tilt, Zoom, Exposure, Brightness, Contrast, etc...
```C#
// adjust properties.
UsbCamera.PropertyItems.Property prop;
prop = camera.Properties[DirectShow.CameraControlProperty.Exposure];
if (prop.Available)
{
    var min = prop.Min;
    var max = prop.Max;
    var def = prop.Default;
    var step = prop.Step;
    prop.SetValue(DirectShow.CameraControlFlags.Manual, prop.Default);
}

prop = camera.Properties[DirectShow.VideoProcAmpProperty.WhiteBalance];
if (prop.Available && prop.CanAuto)
{
    prop.SetValue(DirectShow.CameraControlFlags.Auto, 0);
}
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
