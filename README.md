# UsbCamera
C# source code for using usb camera and web camera in WinForms/WPF.  
With only single CSharp source code. No external library required.

# How to use
Add UsbCamera.cs to your project.  
Following is how to use. see also [SampleProject](https://github.com/secile/UsbCamera/tree/master/SampleProject).  
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
// 0:[Video], [MJPG], {Width=1280, Height=720}, 333333, 30fps, [VideoInfo], ...
// 1:[Video], [MJPG], {Width=320, Height=180}, 333333, 30fps, [VideoInfo], ...
// 2:[Video], [MJPG], {Width=320, Height=240}, 333333, 30fps, [VideoInfo], ...
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

# WPF support.
By default, GetBitmap() returns image of System.Drawing.Bitmap.  
In WPF, define 'USBCAMERA_WPF' symbol that makes GetBitmap() returns image of BitmapSource.
<img src="https://user-images.githubusercontent.com/29785639/142785991-c1f42bd1-6bea-459b-99c7-a0c8c175ce1e.png" width="360">

# Get image data as byte array (Experimental)
If 'USBCAMERA_BYTEARRAY' symbol defined, GetBitmap() returns image data as byte array.  
This is intended to be used with machine learning.  
GetBitmap function returns IEnumerable\<byte\> as a signature, but it actually returns byte array.  
So, please cast it to 'byte[]' before use.

```cs
button1.Click += (s, ev) =>
{
    var buf = (byte[])camera.GetBitmap(); // cast to byte[] before use.
    var bmp = BufferToBitmap(buf, camera.Size.Width, camera.Size.Height);
    pictureBox2.Image = bmp;
};

private static Bitmap BufferToBitmap(byte[] buffer, int width, int height)
{
    var result = new Bitmap(width, height);
    var bmpData = result.LockBits(new Rectangle(Point.Empty, result.Size), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
    System.Runtime.InteropServices.Marshal.Copy(buffer, 0, bmpData.Scan0, buffer.Length);
    result.UnlockBits(bmpData);
    return result;
}
```

# Gray scale camera supports. (Experimental)
It's now possible to use gray scale camera which buffer format is Y800, Y8, and Y16.  
gray scale camera support is under experimental,
If you have any opinion, please join issue [#46](https://github.com/secile/UsbCamera/issues/46).

Y800(and Y8) is an 8-bit, Y16 is a 16-bit grayscale format.  
To use these video format, please follow the steps below. or see also
[sample code](https://github.com/secile/UsbCamera/blob/master/SampleProject/UsbCameraByteArray/Form1.cs)
in SampleProject/UsbCameraByteArray.

1. define 'USBCAMERA_BYTEARRAY' symbol, that makes GetBitmap() returns image data as byte array. in case on WPF, define 'USBCAMERA_WPF' symbol at a same time.
2. select VideoFormat which SubType is Y800, Y8, or Y16.
3. to preview the image, do not use SetPreviewControl on WinForms. use Timer and GetBitmap() on WinForms, or use PreviewCaptured on WPF.
4. to show the image, you have to convert byte array to Bitmap(on WinForms) or BitmapSource(on WPF), use UsbCamera.ByteArrayUtility.Y800.CreateBitmap or UsbCamera.ByteArrayUtility.Y16.CreateBitmap function.

```cs
// do not use SetPreviewControl, use Timer and GetBitmap().
var timer = new System.Timers.Timer(1000 / 30) { SynchronizingObject = this };
timer.Elapsed += (s, ev) =>
{
    var buffer = (byte[])camera.GetBitmap(); // define 'USBCAMERA_BYTEARRAY' symbol, then GetBitmap() returns byte array of Y16 raw data.
    var bmp = UsbCamera.ByteArrayUtility.Y16.CreateBitmap(buffer, 12, camera.Size.Width, camera.Size.Height); // convert it to Bitmap.
    pictureBox1.Image = bmp;
};
```

note on using Y16 buffer format.

5. Y16 is a 16-bit grayscale format, but in general, C# Bitmap class is not able to handle 16-bit grayscale image properly. UsbCamera.ByteArrayUtility.Y16.CreateBitmap returns 8-bit grayscale image, which causes the lost of pixel bit depth. do not use the Bitmap to inspect the luminance value of image, use this only for preview.
6. to inspect/manipulate the luminance value of specific coordinates from byte array, use UsbCamera.ByteArrayUtility.Y16.GetValue/SetValue function.
7. Y16 video format has it's own bit depth(8, 10 or 12) and it's depend of your cameras spec. you must specify the bit depth as an argument when you call the UsbCamera.ByteArrayUtility.Y16.CreateBitmap, GetValue, SetValue function.

# Show preview.
There are 3 ways to show preview.

1. use SetPreviewControl.  
in WinForms, this works light, less GC, and recommended way.  
in WPF, SetPreviewControl requires window handle but WPF control does not have handle.
it is recommended to use PictureBox with WindowsFormsHost.
```C#
var handle = pictureBox.Handle;
camera.SetPreviewControl(handle, new Size(320, 240));
```

2. subscribe PreviewCaptured.  
in WPF with data binding, this is less GC, and recommended way.  
```C#
camera.PreviewCaptured += (bmp) =>
{
    Preview = bmp;
    OnPropertyChanged("Preview");
};
```

3. use Timer and GetBitmap().  
Use this if you modify acquired image and show it. (e.g. face detection.)  
in WinForms, use System.Timers.Timer.  
in WPF, use DispatcherTimer.
```C#
var timer = new System.Timers.Timer(1000 / 30) { SynchronizingObject = this };
timer.Elapsed += (s, ev) => pictureBox.Image = camera.GetBitmap();
timer.Start();
this.FormClosing += (s, ev) => timer.Stop();
```

### Note
Allocating bitmap so high frequency, esspecially large bitmap, can lead to exceptions bacause of out-of-memory.
Basically, bitmap object is disposed automatically by GC, so it is not occur in normal situation.  
In my environment, using VideoFormat that image size is larger than 1920×1080 cause exception.
In case of the situation, you have to dispose bitmap by yourself.

```cs
var timer = new System.Timers.Timer(1000 / 30) { SynchronizingObject = this };
timer.Elapsed += (s, ev) =>
{
    var oldImage = pictureBox1.Image;
    pictureBox1.Image = camera.GetBitmap();
    oldImage?.Dispose();
};
timer.Start();
```

# Still image capture.
Some cameras can produce a still image separate from the capture stream,  
and often the still image is of higher quality than the images produced by the capture stream.  
If your camera has a hardware button, your camera might supports still image capture. Push it, or call StillImageTrigger().  
When you capture still image, you receive StillImageCaptured call back. 
```C#
if (camera.StillImageAvailable)
{
    button.Click += (s, ev) => camera.StillImageTrigger();
    camera.StillImageCaptured += bmp => pictureBox.Image = bmp;
}
```

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
