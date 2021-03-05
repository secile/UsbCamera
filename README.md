# UsbCamera
C# source code for using USB camera in WinForms/WPF.  
With only single CSharp source code. No external library required.

# How to use
Add UsbCamera.cs to your project.  
```C#
// [How to use]
// check USB camera is available.
string[] devices = UsbCamera.FindDevices();
if (devices.Length == 0) return; // no camera.
            
// check format.
int cameraIndex = 0;
UsbCamera.VideoFormat[] formats = UsbCamera.GetVideoFormat(cameraIndex);
for(int i=0; i<formats.Length; i++) Console.WriteLine("{0}:{1}", i, formats[i]);
            
// create usb camera and start.
var camera = new UsbCamera(cameraIndex, formats[0]);
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

# if WPF
By default, GetBitmap() returns image of System.Drawing.Bitmap.  
If WPF, define 'USBCAMERA_WPF' symbol that makes GetBitmap() returns image of BitmapSource.

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

# No external library required.
Previos version, this project used external library.
But now, no need to add external library to your project.

# Special Thanks.
This project make use of a part of source code of the project below. Thank you!   
* DShowLib (author: Mr. M.Oshikiri)  
    - http://www.geocities.co.jp/SiliconValley/7406/tips/dshowdll/dshowdll0.html  
* DSLab (author: cogorou)  
    - https://github.com/cogorou/DSLab

# Example.
You can make H264 recorder by using UsbCamera and OpenH264Lib.NET and [AviWriter](https://github.com/secile/MotionJPEGWriter/blob/master/source/AviWriter.cs)(in MotionJPEGWriter).  
See README.md in [OpenH264Lib.NET](https://github.com/secile/OpenH264Lib.NET).

You can make MotionJPEG movie with audio, see README.md in [AudioIO](https://github.com/secile/AudioIO).
