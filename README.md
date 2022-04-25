# UsbCamera
C# source code for using USB camera in WinForms/WPF.  
With only single CSharp source code. No external library required.

# How to use
Add UsbCamera.cs to your project.  
Following is how to use. see also SampleProject.  
```C#
// [How to use]
// check USB camera is available.
string[] devices = UsbCamera.FindDevices();
if (devices.Length == 0) return; // no camera.
            
// get video format.
var cameraIndex = 0;
var formats = UsbCamera.GetVideoFormat(cameraIndex);

// select the format you want.
foreach (var item in formats) Console.WriteLine(item);
// for example, video format is like as follows.
// 0:[Video], [MJPG], {Width=1280, Height=720}, 333333, [VideoInfo], ...
// 1:[Video], [MJPG], {Width=320, Height=180}, 333333, [VideoInfo], ...
// 2:[Video], [MJPG], {Width=320, Height=240}, 333333, [VideoInfo], ...
// ...
var format = formats[0];
            
// create instance.
var camera = new UsbCamera(cameraIndex, format);
// this closing event handler make sure that the instance is not subject to garbage collection.
this.FormClosing += (s, ev) => camera.Release(); // release when close.

// show preview on control.
camera.SetPreviewControl(pictureBox1.Handle, pictureBox1.ClientSize);
pictureBox1.Resize += (s, ev) => camera.SetPreviewSize(pictureBox1.ClientSize); // support resize.

// start.
camera.Start();
            
// get image.
// Immediately after starting the USB camera,
// GetBitmap() fails because image buffer is not prepared yet.
var bmp = camera.GetBitmap();
```

# if WPF
By default, GetBitmap() returns image of System.Drawing.Bitmap.  
If WPF, define 'USBCAMERA_WPF' symbol that makes GetBitmap() returns image of BitmapSource.
<img src="https://user-images.githubusercontent.com/29785639/142785991-c1f42bd1-6bea-459b-99c7-a0c8c175ce1e.png" width="360">

# Adjust Tilt, Zoom, Exposure, Brightness, Contrast, etc...
```C#
// adjust properties.
UsbCamera.PropertyItems.Property prop;
prop = camera.Properties[DirectShow.CameraControlProperty.Exposure];
if (prop.Available)
{
    // get current value
    var val = prop.GetValue();
    // set new value
    var min = prop.Min;
    var max = prop.Max;
    var def = prop.Default;
    var step = prop.Step;
    prop.SetValue(DirectShow.CameraControlFlags.Manual, def);
}

prop = camera.Properties[DirectShow.VideoProcAmpProperty.WhiteBalance];
if (prop.Available && prop.CanAuto)
{
    prop.SetValue(DirectShow.CameraControlFlags.Auto, 0);
}
```

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
