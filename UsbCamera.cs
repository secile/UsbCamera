using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace GitHub.secile.Video
{
    // [How to use]
    // string[] devices = UsbCamera.FindDevices();
    // if (devices.Length == 0) return; // no camera.
    //
    // check format.
    // UsbCamera.VideoFormat[] formats = UsbCamera.GetVideoFormat(0);
    // foreach (var item in formats) Console.WriteLine(item);
    //
    // create usb camera and start.
    // var index = 0;
    // var camera = new UsbCamera(index, formats[0]);
    // camera.Start();
    //
    // get image.
    // Immediately after starting the USB camera,
    // GetBitmap() fails because image buffer is not prepared yet.
    // var bmp = camera.GetBitmap();
    
    class UsbCamera
    {
        /// <summary>Usb camera image size.</summary>
        public Size Size { get; private set; }

        /// <summary>Start.</summary>
        public Action Start { get; private set; }

        /// <summary>Stop.</summary>
        public Action Stop { get; private set; }

        /// <summary>Release resource.</summary>
        public Action Release { get; private set; }

        /// <summary>Get image.</summary>
        /// <remarks>Immediately after starting, fails because image buffer is not prepared yet.</remarks>
        public Func<Bitmap> GetBitmap { get; private set; }

        /// <summary>
        /// Get available USB camera list.
        /// </summary>
        /// <returns>Array of camera name, or if no device found, zero length array.</returns>
        public static string[] FindDevices()
        {
            return DirectShow.GetFiltes(DirectShow.DsGuid.CLSID_VideoInputDeviceCategory).ToArray();
        }

        /// <summary>
        /// Get video formats.
        /// </summary>
        public static VideoFormat[] GetVideoFormat(int cameraIndex)
        {
            var filter = DirectShow.CreateFilter(DirectShow.DsGuid.CLSID_VideoInputDeviceCategory, cameraIndex);
            var pin = DirectShow.FindPin(filter, 0, DirectShow.PIN_DIRECTION.PINDIR_OUTPUT);
            return GetVideoOutputFormat(pin);
        }

        /// <summary>
        /// Create USB Camera. If device do not support the size, default size will applied.
        /// </summary>
        /// <param name="cameraIndex">Camera index in FindDevices() result.</param>
        /// <param name="size">
        /// Size you want to create. Normally use Size property of VideoFormat in GetVideoFormat() result.
        /// </param>
        public UsbCamera(int cameraIndex, Size size) : this(cameraIndex, new VideoFormat() { Size = size })
        {
        }

        /// <summary>
        /// Create USB Camera. If device do not support the format, default format will applied.
        /// </summary>
        /// <param name="cameraIndex">Camera index in FindDevices() result.</param>
        /// <param name="format">
        /// Normally use GetVideoFormat() result.
        /// You can change TimePerFrame value from Caps.MinFrameInterval to Caps.MaxFrameInterval.
        /// TimePerFrame = 10,000,000 / frame duration. (ex: 333333 in case 30fps).
        /// You can change Size value in case Caps.MaxOutputSize > Caps.MinOutputSize and OutputGranularityX/Y is not zero.
        /// Size = any value from Caps.MinOutputSize to Caps.MaxOutputSize step with OutputGranularityX/Y.
        /// </param>
        public UsbCamera(int cameraIndex, VideoFormat format)
        {
            var camera_list = FindDevices();
            if (cameraIndex >= camera_list.Length) throw new ArgumentException("USB camera is not available.", "cameraIndex");
            Init(cameraIndex, format);
        }

        private void Init(int index, VideoFormat format)
        {
            //----------------------------------
            // Create Filter Graph
            //----------------------------------
            // +--------------------+  +----------------+  +---------------+
            // |Video Capture Source|→| Sample Grabber |→| Null Renderer |
            // +--------------------+  +----------------+  +---------------+
            //                                 ↓GetBitmap()

            var graph = DirectShow.CreateGraph();

            //----------------------------------
            // VideoCaptureSource
            //----------------------------------
            var vcap_source = CreateVideoCaptureSource(index, format);
            graph.AddFilter(vcap_source, "VideoCapture");

            //------------------------------
            // SampleGrabber
            //------------------------------
            var grabber = CreateSampleGrabber();
            graph.AddFilter(grabber, "SampleGrabber");
            var i_grabber = (DirectShow.ISampleGrabber)grabber;
            i_grabber.SetBufferSamples(true); //サンプルグラバでのサンプリングを開始

            //---------------------------------------------------
            // Null Renderer
            //---------------------------------------------------
            var renderer = DirectShow.CoCreateInstance(DirectShow.DsGuid.CLSID_NullRenderer) as DirectShow.IBaseFilter;
            graph.AddFilter(renderer, "NullRenderer");

            //---------------------------------------------------
            // Create Filter Graph
            //---------------------------------------------------
            var builder = DirectShow.CoCreateInstance(DirectShow.DsGuid.CLSID_CaptureGraphBuilder2) as DirectShow.ICaptureGraphBuilder2;
            builder.SetFiltergraph(graph);
            var pinCategory = DirectShow.DsGuid.PIN_CATEGORY_CAPTURE;
            var mediaType = DirectShow.DsGuid.MEDIATYPE_Video;
            builder.RenderStream(ref pinCategory, ref mediaType, vcap_source, grabber, renderer);

            // SampleGrabber Format.
            {
                var mt = new DirectShow.AM_MEDIA_TYPE();
                i_grabber.GetConnectedMediaType(mt);
                var header = (DirectShow.VIDEOINFOHEADER)Marshal.PtrToStructure(mt.pbFormat, typeof(DirectShow.VIDEOINFOHEADER));
                var width = header.bmiHeader.biWidth;
                var height = header.bmiHeader.biHeight;
                var stride = width * (header.bmiHeader.biBitCount / 8);
                DirectShow.DeleteMediaType(ref mt);

                Size = new Size(width, height);
                GetBitmap = () => GetBitmapMain(i_grabber, width, height, stride);
            }

            // Assign Delegates
            Start = () => DirectShow.PlayGraph(graph, DirectShow.FILTER_STATE.Running);
            Stop = () => DirectShow.PlayGraph(graph, DirectShow.FILTER_STATE.Stopped);
            Release = () =>
            {
                Stop();

                DirectShow.ReleaseInstance(ref i_grabber);
                DirectShow.ReleaseInstance(ref builder);
                DirectShow.ReleaseInstance(ref graph);
            };
        }

        /// <summary>Get Bitmap from Sample Grabber</summary>
        private Bitmap GetBitmapMain(DirectShow.ISampleGrabber i_grabber, int width, int height, int stride)
        {
            try
            {
                return GetBitmapMainMain(i_grabber, width, height, stride);
            }
            catch (COMException ex)
            {
                const uint VFW_E_WRONG_STATE = 0x80040227;
                if ((uint)ex.ErrorCode == VFW_E_WRONG_STATE)
                {
                    // image data is not ready yet. return empty bitmap.
                    return new Bitmap(width, height);
                }

                throw;
            }
        }

        /// <summary>Get Bitmap from Sample Grabber</summary>
        private Bitmap GetBitmapMainMain(DirectShow.ISampleGrabber i_grabber, int width, int height, int stride)
        {
            // サンプルグラバから画像を取得するためには
            // まずサイズ0でGetCurrentBufferを呼び出しバッファサイズを取得し
            // バッファ確保して再度GetCurrentBufferを呼び出す。
            // 取得した画像は逆になっているので反転させる必要がある。
            int sz = 0;
            i_grabber.GetCurrentBuffer(ref sz, IntPtr.Zero); // IntPtr.Zeroで呼び出してバッファサイズ取得
            if (sz == 0) return null;

            // メモリ確保し画像データ取得
            var ptr = Marshal.AllocCoTaskMem(sz);
            i_grabber.GetCurrentBuffer(ref sz, ptr);

            // 画像データをbyte配列に入れなおす
            var data = new byte[sz];
            Marshal.Copy(ptr, data, 0, sz);

            // 画像を作成
            var result = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            var bmp_data = result.LockBits(new Rectangle(Point.Empty, result.Size), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);

            // 上下反転させながら1行ごとコピー
            for (int y = 0; y < height; y++)
            {
                var src_idx = sz - (stride * (y + 1)); // 最終行から
                var dst = new IntPtr(bmp_data.Scan0.ToInt32() + (stride * y));
                Marshal.Copy(data, src_idx, dst, stride);
            }
            result.UnlockBits(bmp_data);
            Marshal.FreeCoTaskMem(ptr);

            return result;
        }

        /// <summary>
        /// サンプルグラバを作成する
        /// </summary>
        private DirectShow.IBaseFilter CreateSampleGrabber()
        {
            var filter = DirectShow.CreateFilter(DirectShow.DsGuid.CLSID_SampleGrabber);
            var ismp = filter as DirectShow.ISampleGrabber;

            // サンプル グラバを最初に作成したときは、優先メディア タイプは設定されていない。
            // これは、グラフ内のほぼすべてのフィルタに接続はできるが、受け取るデータ タイプを制御できないとうことである。
            // したがって、残りのグラフを作成する前に、ISampleGrabber::SetMediaType メソッドを呼び出して、
            // サンプル グラバに対してメディア タイプを設定すること。

            // サンプル グラバは、接続した時に他のフィルタが提供するメディア タイプとこの設定されたメディア タイプとを比較する。
            // 調べるフィールドは、メジャー タイプ、サブタイプ、フォーマット タイプだけである。
            // これらのフィールドでは、値 GUID_NULL は "あらゆる値を受け付ける" という意味である。
            // 通常は、メジャー タイプとサブタイプを設定する。

            // https://msdn.microsoft.com/ja-jp/library/cc370616.aspx
            // https://msdn.microsoft.com/ja-jp/library/cc369546.aspx
            // サンプル グラバ フィルタはトップダウン方向 (負の biHeight) のビデオ タイプ、または
            // FORMAT_VideoInfo2 のフォーマット タイプのビデオ タイプはすべて拒否する。

            var mt = new DirectShow.AM_MEDIA_TYPE();
            mt.MajorType = DirectShow.DsGuid.MEDIATYPE_Video;
            mt.SubType = DirectShow.DsGuid.MEDIASUBTYPE_RGB24;
            ismp.SetMediaType(mt);
            return filter;
        }

        /// <summary>
        /// Video Capture Sourceフィルタを作成する
        /// </summary>
        private DirectShow.IBaseFilter CreateVideoCaptureSource(int index, VideoFormat format)
        {
            var filter = DirectShow.CreateFilter(DirectShow.DsGuid.CLSID_VideoInputDeviceCategory, index);
            var pin = DirectShow.FindPin(filter, 0, DirectShow.PIN_DIRECTION.PINDIR_OUTPUT);
            SetVideoOutputFormat(pin, format);
            return filter;
        }

        /// <summary>
        /// ビデオキャプチャデバイスの出力形式を選択する。
        /// </summary>
        private static void SetVideoOutputFormat(DirectShow.IPin pin, VideoFormat format)
        {
            var formats = GetVideoOutputFormat(pin);

            // 仕様ではVideoCaptureDeviceはメディア タイプごとに一定範囲の出力フォーマットをサポートできる。例えば以下のように。
            // [0]:YUY2 最小:160x120, 最大:320x240, X軸4STEP, Y軸2STEPごと
            // [1]:RGB8 最小:640x480, 最大:640x480, X軸0STEP, Y軸0STEPごと
            // SetFormatで出力サイズとフレームレートをこの範囲内で設定可能。
            // ただし試した限り、手持ちのUSBカメラはすべてサイズ固定(最大・最小が同じ)で返してきた。

            // https://msdn.microsoft.com/ja-jp/windows/dd407352(v=vs.80)
            // VIDEO_STREAM_CONFIG_CAPSの以下を除くほとんどのメンバーはdeprecated(非推奨)である。
            // アプリケーションはその他のメンバーの利用を避けること。かわりにIAMStreamConfig::GetFormatを利用すること。
            // - Guid:FORMAT_VideoInfo or FORMAT_VideoInfo2など。
            // - VideoStandard:アナログTV信号のフォーマット(NTSC, PALなど)をAnalogVideoStandard列挙体で指定する。
            // - MinFrameInterval, MaxFrameInterval:ビデオキャプチャデバイスがサポートするフレームレートの範囲。100ナノ秒単位。

            // 上記によると、VIDEO_STREAM_CONFIG_CAPSは現在はdeprecated(非推奨)であるらしい。かわりにIAMStreamConfig::GetFormatを使用することらしい。
            // 上記仕様を守ったデバイスは出力サイズを固定で返すが、守ってない古いデバイスは出力サイズを可変で返す、と考えられる。
            // 参考までに、VIDEO_STREAM_CONFIG_CAPSで解像度・クロップサイズ・フレームレートなどを変更する手順は以下の通り。

            // ①フレームレート(これは非推奨ではない)
            // VIDEO_STREAM_CONFIG_CAPS のメンバ MinFrameInterval と MaxFrameInterval は各ビデオ フレームの最小の長さと最大の長さである。
            // 次の式を使って、これらの値をフレーム レートに変換できる。
            // frames per second = 10,000,000 / frame duration

            // 特定のフレーム レートを要求するには、メディア タイプにある構造体 VIDEOINFOHEADER か VIDEOINFOHEADER2 の AvgTimePerFrame の値を変更する。
            // デバイスは最小値と最大値の間で可能なすべての値はサポートしていないことがあるため、ドライバは使用可能な最も近い値を使う。

            // ②Cropping(画像の一部切り抜き)
            // MinCroppingSize = (160, 120) // Cropping最小サイズ。
            // MaxCroppingSize = (320, 240) // Cropping最大サイズ。
            // CropGranularityX = 4         // 水平方向細分度。
            // CropGranularityY = 8         // 垂直方向細分度。
            // CropAlignX = 2               // the top-left corner of the source rectangle can sit.
            // CropAlignY = 4               // the top-left corner of the source rectangle can sit.

            // ③出力サイズ
            // https://msdn.microsoft.com/ja-jp/library/cc353344.aspx
            // https://msdn.microsoft.com/ja-jp/library/cc371290.aspx
            // VIDEO_STREAM_CONFIG_CAPS 構造体は、このメディア タイプに使える最小と最大の幅と高さを示す。
            // また、"ステップ" サイズ"も示す。ステップ サイズは、幅または高さを調整できるインクリメントの値を定義する。
            // たとえば、デバイスは次の値を返すことがある。
            // MinOutputSize: 160 × 120
            // MaxOutputSize: 320 × 240
            // OutputGranularityX:8 ピクセル (水平ステップ サイズ)
            // OutputGranularityY:8 ピクセル (垂直ステップ サイズ)
            // これらの数値が与えられると、幅は範囲内 (160、168、176、... 304、312、320) の任意の値に、
            // 高さは範囲内 (120、128、136、... 224、232、240) の任意の値に設定できる。

            // 出力サイズの可変のUSBカメラがないためデバッグするには以下のコメントを外す。
            // I have no USB camera of variable output size, uncomment below to debug.
            //size = new Size(168, 126);
            //vformat[0].Caps = new DirectShow.VIDEO_STREAM_CONFIG_CAPS()
            //{
            //    Guid = DirectShow.DsGuid.FORMAT_VideoInfo,
            //    MinOutputSize = new DirectShow.SIZE() { cx = 160, cy = 120 },
            //    MaxOutputSize = new DirectShow.SIZE() { cx = 320, cy = 240 },
            //    OutputGranularityX = 4,
            //    OutputGranularityY = 2
            //};

            // VIDEO_STREAM_CONFIG_CAPSは現在では非推奨。まずは固定サイズを探す
            // VIDEO_STREAM_CONFIG_CAPS is deprecated. First, find just the fixed size.
            for (int i = 0; i < formats.Length; i++)
            {
                var item = formats[i];

                // VideoInfoのみ対応する。(VideoInfo2はSampleGrabber未対応のため)
                // VideoInfo only... (SampleGrabber do not support VideoInfo2)
                // https://msdn.microsoft.com/ja-jp/library/cc370616.aspx
                if (item.MajorType != DirectShow.DsGuid.GetNickname(DirectShow.DsGuid.MEDIATYPE_Video)) continue;
                if (string.IsNullOrEmpty(format.SubType) == false && format.SubType != item.SubType) continue;
                if (item.Caps.Guid != DirectShow.DsGuid.FORMAT_VideoInfo) continue;

                if (item.Size.Width == format.Size.Width && item.Size.Height == format.Size.Height)
                {
                    SetVideoOutputFormat(pin, i, format.Size, format.TimePerFrame);
                    return;
                }
            }

            // 固定サイズが見つからなかった。可変サイズの範囲を探す。
            // Not found fixed size, search for variable size.
            for (int i = 0; i < formats.Length; i++)
            {
                var item = formats[i];

                // VideoInfoのみ対応する。(VideoInfo2はSampleGrabber未対応のため)
                // VideoInfo only... (SampleGrabber do not support VideoInfo2)
                // https://msdn.microsoft.com/ja-jp/library/cc370616.aspx
                if (item.MajorType != DirectShow.DsGuid.GetNickname(DirectShow.DsGuid.MEDIATYPE_Video)) continue;
                if (string.IsNullOrEmpty(format.SubType) == false && format.SubType != item.SubType) continue;
                if (item.Caps.Guid != DirectShow.DsGuid.FORMAT_VideoInfo) continue;

                if (item.Caps.OutputGranularityX == 0) continue;
                if (item.Caps.OutputGranularityY == 0) continue;

                for (int w = item.Caps.MinOutputSize.cx; w < item.Caps.MaxOutputSize.cx; w += item.Caps.OutputGranularityX)
                {
                    for (int h = item.Caps.MinOutputSize.cy; h < item.Caps.MaxOutputSize.cy; h += item.Caps.OutputGranularityY)
                    {
                        if (w == format.Size.Width && h == format.Size.Height)
                        {
                            SetVideoOutputFormat(pin, i, format.Size, format.TimePerFrame);
                            return;
                        }
                    }
                }
            }

            // サイズが見つかなかった場合はデフォルトサイズとする。
            // Not found, use default size.
            SetVideoOutputFormat(pin, 0, Size.Empty, 0);
        }


        /// <summary>
        /// ビデオキャプチャデバイスがサポートするメディアタイプ・サイズを取得する。
        /// </summary>
        private static VideoFormat[] GetVideoOutputFormat(DirectShow.IPin pin)
        {
            // IAMStreamConfigインタフェース取得
            var config = pin as DirectShow.IAMStreamConfig;
            if (config == null)
            {
                throw new InvalidOperationException("IAMStreamConfigインタフェースを取得できません。");
            }

            // フォーマット個数取得
            int cap_count = 0, cap_size = 0;
            config.GetNumberOfCapabilities(ref cap_count, ref cap_size);
            if (cap_size != Marshal.SizeOf(typeof(DirectShow.VIDEO_STREAM_CONFIG_CAPS)))
            {
                throw new InvalidOperationException("IAMStreamConfigインタフェースを取得できません。");
            }

            // 返却値の確保
            var result = new VideoFormat[cap_count];

            // データ用領域確保
            var cap_data = Marshal.AllocHGlobal(cap_size);

            // 列挙
            for (int i = 0; i < cap_count; i++)
            {
                var entry = new VideoFormat();

                // x番目のフォーマット情報取得
                DirectShow.AM_MEDIA_TYPE mt = null;
                config.GetStreamCaps(i, ref mt, cap_data);
                entry.Caps = PtrToStructure<DirectShow.VIDEO_STREAM_CONFIG_CAPS>(cap_data);

                // フォーマット情報の読み取り
                entry.MajorType = DirectShow.DsGuid.GetNickname(mt.MajorType);
                entry.SubType = DirectShow.DsGuid.GetNickname(mt.SubType);

                if (mt.FormatType == DirectShow.DsGuid.FORMAT_VideoInfo)
                {
                    var vinfo = PtrToStructure<DirectShow.VIDEOINFOHEADER>(mt.pbFormat);
                    entry.Size = new Size(vinfo.bmiHeader.biWidth, vinfo.bmiHeader.biHeight);
                    entry.TimePerFrame = vinfo.AvgTimePerFrame;
                }
                else if (mt.FormatType == DirectShow.DsGuid.FORMAT_VideoInfo2)
                {
                    var vinfo = PtrToStructure<DirectShow.VIDEOINFOHEADER2>(mt.pbFormat);
                    entry.Size = new Size(vinfo.bmiHeader.biWidth, vinfo.bmiHeader.biHeight);
                    entry.TimePerFrame = vinfo.AvgTimePerFrame;
                }

                // 解放
                DirectShow.DeleteMediaType(ref mt);

                result[i] = entry;
            }

            // 解放
            Marshal.FreeHGlobal(cap_data);

            return result;
        }

        /// <summary>
        /// ビデオキャプチャデバイスの出力形式を選択する。
        /// 事前にGetVideoOutputFormatでメディアタイプ・サイズを得ておき、その中から希望のindexを指定する。
        /// 同時に出力サイズとフレームレートを変更することができる。
        /// </summary>
        /// <param name="index">希望のindexを指定する</param>
        /// <param name="size">Empty以外を指定すると出力サイズを変更する。事前にVIDEO_STREAM_CONFIG_CAPSで取得した可能範囲内を指定すること。</param>
        /// <param name="timePerFrame">0以上を指定するとフレームレートを変更する。事前にVIDEO_STREAM_CONFIG_CAPSで取得した可能範囲内を指定すること。</param>
        private static void SetVideoOutputFormat(DirectShow.IPin pin, int index, Size size, long timePerFrame)
        {
            // IAMStreamConfigインタフェース取得
            var config = pin as DirectShow.IAMStreamConfig;
            if (config == null)
            {
                throw new InvalidOperationException("ピンはIAMStreamConfigインタフェースを公開しません。");
            }

            // フォーマット個数取得
            int cap_count = 0, cap_size = 0;
            config.GetNumberOfCapabilities(ref cap_count, ref cap_size);
            if (cap_size != Marshal.SizeOf(typeof(DirectShow.VIDEO_STREAM_CONFIG_CAPS)))
            {
                throw new InvalidOperationException("VIDEO_STREAM_CONFIG_CAPSを取得できません。");
            }

            // データ用領域確保
            var cap_data = Marshal.AllocHGlobal(cap_size);

            // idx番目のフォーマット情報取得
            DirectShow.AM_MEDIA_TYPE mt = null;
            config.GetStreamCaps(index, ref mt, cap_data);
            var cap = PtrToStructure<DirectShow.VIDEO_STREAM_CONFIG_CAPS>(cap_data);
            
            if (mt.FormatType == DirectShow.DsGuid.FORMAT_VideoInfo)
            {
                var vinfo = PtrToStructure<DirectShow.VIDEOINFOHEADER>(mt.pbFormat);
                if (!size.IsEmpty) { vinfo.bmiHeader.biWidth = size.Width; vinfo.bmiHeader.biHeight = size.Height; }
                if (timePerFrame > 0) { vinfo.AvgTimePerFrame = timePerFrame; }
                Marshal.StructureToPtr(vinfo, mt.pbFormat, true);
            }
            else if (mt.FormatType == DirectShow.DsGuid.FORMAT_VideoInfo2)
            {
                var vinfo = PtrToStructure<DirectShow.VIDEOINFOHEADER2>(mt.pbFormat);
                if (!size.IsEmpty) { vinfo.bmiHeader.biWidth = size.Width; vinfo.bmiHeader.biHeight = size.Height; }
                if (timePerFrame > 0) { vinfo.AvgTimePerFrame = timePerFrame; }
                Marshal.StructureToPtr(vinfo, mt.pbFormat, true);
            }

            // フォーマットを選択
            config.SetFormat(mt);

            // 解放
            if (cap_data != System.IntPtr.Zero) Marshal.FreeHGlobal(cap_data);
            if (mt != null) DirectShow.DeleteMediaType(ref mt);
        }

        private static T PtrToStructure<T>(IntPtr ptr)
        {
            return (T)Marshal.PtrToStructure(ptr, typeof(T));
        }

        public class VideoFormat
        {
            public string MajorType { get; set; }  // [Video]など
            public string SubType { get; set; }    // [YUY2], [MJPG]など
            public Size Size { get; set; }         // ビデオサイズ
            public long TimePerFrame { get; set; } // ビデオフレームの平均表示時間を100ナノ秒単位で。30fpsのとき「333333」
            public DirectShow.VIDEO_STREAM_CONFIG_CAPS Caps { get; set; }

            public override string ToString()
            {
                return string.Format("{0}, {1}, {2}, {3}, {4}", MajorType, SubType, Size, TimePerFrame, CapsString());
            }

            private string CapsString()
            {
                var sb = new StringBuilder();
                foreach (var info in Caps.GetType().GetFields())
                {
                    sb.AppendFormat("{0}={1}, ", info.Name, info.GetValue(Caps));
                }
                return sb.ToString();
            }
        }
    }

    static class DirectShow
    {
        #region Function

        /// <summary>COMオブジェクトのインスタンスを作成する。</summary>
        public static object CoCreateInstance(Guid clsid)
        {
            return Activator.CreateInstance(Type.GetTypeFromCLSID(clsid));
        }

        /// <summary>COMオブジェクトのインスタンスを開放する。</summary>
        public static void ReleaseInstance<T>(ref T com) where T : class
        {
            if (com != null)
            {
                Marshal.ReleaseComObject(com);
                com = null;
            }
        }

        /// <summary>フィルタグラフを作成する。</summary>
        public static IGraphBuilder CreateGraph()
        {
            return CoCreateInstance(DsGuid.CLSID_FilterGraph) as IGraphBuilder;
        }

        /// <summary>フィルタグラフを再生・停止・一時停止する。</summary>
        public static void PlayGraph(IGraphBuilder graph, FILTER_STATE state)
        {
            var mediaControl = graph as IMediaControl;
            if (mediaControl == null) return;

            switch (state)
            {
                case FILTER_STATE.Paused: mediaControl.Pause(); break;
                case FILTER_STATE.Stopped: mediaControl.Stop(); break;
                default: mediaControl.Run(); break;
            }
        }

        /// <summary>フィルタの一覧を取得する。</summary>
        public static List<string> GetFiltes(Guid category)
        {
            var result = new List<string>();

            EnumMonikers(category, (moniker, prop) =>
            {
                object value = null;
                prop.Read("FriendlyName", ref value, 0);
                var name = (string)value;

                result.Add(name);

                return false; // 継続。
            });

            return result;
        }

        /// <summary>フィルタのインスタンスを作成する。CLSIDで指定する。</summary>
        public static IBaseFilter CreateFilter(Guid clsid)
        {
            return CoCreateInstance(clsid) as IBaseFilter;
        }

        /// <summary>フィルタのインスタンスを作成する。CategoryとIndexで指定する。</summary>
        public static IBaseFilter CreateFilter(Guid category, int index)
        {
            IBaseFilter result = null;

            int curr_index = 0;
            EnumMonikers(category, (moniker, prop) =>
            {
                // 指定indexになるまで継続。
                if (index != curr_index++) return false;

                // フィルタのインスタンス作成して返す。
                {
                    object value = null;
                    Guid guid = DirectShow.DsGuid.IID_IBaseFilter;
                    moniker.BindToObject(null, null, ref guid, out value);
                    result = value as IBaseFilter;
                    return true;
                }
            });

            if (result == null) throw new ArgumentException("can't create filter.");
            return result;
        }

        /// <summary>モニカを列挙する。</summary>
        /// <remarks>モニカとはCOMオブジェクトを識別する別名のこと。</remarks>
        private static void EnumMonikers(Guid category, Func<IMoniker, IPropertyBag, bool> func)
        {
            IEnumMoniker enumerator = null;
            ICreateDevEnum device = null;

            try
            {
                // ICreateDevEnum インターフェース取得.
                device = (ICreateDevEnum)Activator.CreateInstance(Type.GetTypeFromCLSID(DsGuid.CLSID_SystemDeviceEnum));

                // IEnumMonikerの作成.
                device.CreateClassEnumerator(ref category, ref enumerator, 0);

                // 列挙可能なデバイスが存在しない場合null
                if (enumerator == null) return;

                // 列挙.
                var monikers = new IMoniker[1];
                var fetched = IntPtr.Zero;

                while (enumerator.Next(monikers.Length, monikers, fetched) == 0)
                {
                    var moniker = monikers[0];

                    // プロパティバッグへのバインド.
                    object value = null;
                    Guid guid = DsGuid.IID_IPropertyBag;
                    moniker.BindToStorage(null, null, ref guid, out value);
                    var prop = (IPropertyBag)value;

                    try
                    {
                        // trueで列挙完了。falseで継続する。
                        var rc = func(moniker, prop);
                        if (rc == true) break;
                    }
                    finally
                    {
                        // プロパティバッグの解放
                        Marshal.ReleaseComObject(prop);

                        // 列挙したモニカの解放.
                        if (moniker != null) Marshal.ReleaseComObject(moniker);
                    }
                }
            }
            finally
            {
                if (enumerator != null) Marshal.ReleaseComObject(enumerator);
                if (device != null) Marshal.ReleaseComObject(device);
            }
        }

        /// <summary>ピンを検索する。</summary>
        public static IPin FindPin(IBaseFilter filter, string name)
        {
            var result = EnumPins(filter, (info) =>
            {
                return (info.achName == name);
            });

            if (result == null) throw new ArgumentException("can't fild pin.");
            return result;
        }

        /// <summary>ピンを検索する。</summary>
        public static IPin FindPin(IBaseFilter filter, int index, PIN_DIRECTION direction)
        {
            int curr_index = 0;
            var result = EnumPins(filter, (info) =>
            {
                // directionを確認。
                if (info.dir != direction) return false;

                // indexは最後にチェック。
                return (index == curr_index++);
            });

            if (result == null) throw new ArgumentException("can't fild pin.");
            return result;
        }

        /// <summary>Pinを列挙する。</summary>
        private static IPin EnumPins(IBaseFilter filter, Func<PIN_INFO, bool> func)
        {
            IEnumPins pins = null;
            IPin ipin = null;

            try
            {
                filter.EnumPins(ref pins);

                int fetched = 0;
                while (pins.Next(1, ref ipin, ref fetched) == 0)
                {
                    if (fetched == 0) break;

                    var info = new PIN_INFO();
                    try
                    {
                        ipin.QueryPinInfo(info);
                        var rc = func(info);
                        if (rc) return ipin;
                    }
                    finally
                    {
                        if (info.pFilter != null) Marshal.ReleaseComObject(info.pFilter);
                    }
                }
            }
            catch
            {
                if (ipin != null) Marshal.ReleaseComObject(ipin);
                throw;
            }
            finally
            {
                if (pins != null) Marshal.ReleaseComObject(pins);
            }

            return null;
        }

        /// <summary>ピンを接続する。</summary>
        public static void ConnectFilter(IGraphBuilder graph, IBaseFilter out_flt, int out_no, IBaseFilter in_flt, int in_no)
        {
            var out_pin = FindPin(out_flt, out_no, PIN_DIRECTION.PINDIR_OUTPUT);
            var inp_pin = FindPin(in_flt, in_no, PIN_DIRECTION.PINDIR_INPUT);
            graph.Connect(out_pin, inp_pin);
        }

        /// <summary>メディアタイプを開放する。</summary>
        public static void DeleteMediaType(ref AM_MEDIA_TYPE mt)
        {
            if (mt.lSampleSize != 0) Marshal.FreeCoTaskMem(mt.pbFormat);
            if (mt.pUnk != IntPtr.Zero) Marshal.FreeCoTaskMem(mt.pUnk);
            mt = null;
        }

        #endregion


        #region Interface

        [ComVisible(true), ComImport(), Guid("56a8689f-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IFilterGraph
        {
            int AddFilter([In] IBaseFilter pFilter, [In, MarshalAs(UnmanagedType.LPWStr)] string pName);
            int RemoveFilter([In] IBaseFilter pFilter);
            int EnumFilters([In, Out] ref IEnumFilters ppEnum);
            int FindFilterByName([In, MarshalAs(UnmanagedType.LPWStr)] string pName, [In, Out] ref IBaseFilter ppFilter);
            int ConnectDirect([In] IPin ppinOut, [In] IPin ppinIn, [In, MarshalAs(UnmanagedType.LPStruct)] AM_MEDIA_TYPE pmt);
            int Reconnect([In] IPin ppin);
            int Disconnect([In] IPin ppin);
            int SetDefaultSyncSource();
        }

        [ComVisible(true), ComImport(), Guid("56a868a9-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IGraphBuilder : IFilterGraph
        {
            int Connect([In] IPin ppinOut, [In] IPin ppinIn);
            int Render([In] IPin ppinOut);
            int RenderFile([In, MarshalAs(UnmanagedType.LPWStr)] string lpcwstrFile, [In, MarshalAs(UnmanagedType.LPWStr)] string lpcwstrPlayList);
            int AddSourceFilter([In, MarshalAs(UnmanagedType.LPWStr)] string lpcwstrFileName, [In, MarshalAs(UnmanagedType.LPWStr)] string lpcwstrFilterName, [In, Out] ref IBaseFilter ppFilter);
            int SetLogFile(IntPtr hFile);
            int Abort();
            int ShouldOperationContinue();
        }

        [ComVisible(true), ComImport(), Guid("56a868b1-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
        public interface IMediaControl
        {
            int Run();
            int Pause();
            int Stop();
            int GetState(int msTimeout, out int pfs);
            int RenderFile(string strFilename);
            int AddSourceFilter([In] string strFilename, [In, Out, MarshalAs(UnmanagedType.IDispatch)] ref object ppUnk);
            int get_FilterCollection([In, Out, MarshalAs(UnmanagedType.IDispatch)] ref object ppUnk);
            int get_RegFilterCollection([In, Out, MarshalAs(UnmanagedType.IDispatch)] ref object ppUnk);
            int StopWhenReady();
        }

        [ComVisible(true), ComImport(), Guid("93E5A4E0-2D50-11d2-ABFA-00A0C9C6E38D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface ICaptureGraphBuilder2
        {
            int SetFiltergraph([In] IGraphBuilder pfg);
            int GetFiltergraph([In, Out] ref IGraphBuilder ppfg);
            int SetOutputFileName([In] ref Guid pType, [In, MarshalAs(UnmanagedType.LPWStr)] string lpstrFile, [In, Out] ref IBaseFilter ppbf, [In, Out] ref IFileSinkFilter ppSink);
            int FindInterface([In] ref Guid pCategory, [In] ref Guid pType, [In] IBaseFilter pbf, [In] IntPtr riid, [In, Out, MarshalAs(UnmanagedType.IUnknown)] ref object ppint);
            int RenderStream([In] ref Guid pCategory, [In] ref Guid pType, [In, MarshalAs(UnmanagedType.IUnknown)] object pSource, [In] IBaseFilter pfCompressor, [In] IBaseFilter pfRenderer);
            int ControlStream([In] ref Guid pCategory, [In] ref Guid pType, [In] IBaseFilter pFilter, [In] IntPtr pstart, [In] IntPtr pstop, [In] short wStartCookie, [In] short wStopCookie);
            int AllocCapFile([In, MarshalAs(UnmanagedType.LPWStr)] string lpstrFile, [In] long dwlSize);
            int CopyCaptureFile([In, MarshalAs(UnmanagedType.LPWStr)] string lpwstrOld, [In, MarshalAs(UnmanagedType.LPWStr)] string lpwstrNew, [In] int fAllowEscAbort, [In] IAMCopyCaptureFileProgress pFilter);
            int FindPin([In] object pSource, [In] int pindir, [In] ref Guid pCategory, [In] ref Guid pType, [In, MarshalAs(UnmanagedType.Bool)] bool fUnconnected, [In] int num, [Out] out IntPtr ppPin);
        }

        [ComVisible(true), ComImport(), Guid("a2104830-7c70-11cf-8bce-00aa00a3f1a6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IFileSinkFilter
        {
            int SetFileName([In, MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [In, MarshalAs(UnmanagedType.LPStruct)] AM_MEDIA_TYPE pmt);
            int GetCurFile([In, Out, MarshalAs(UnmanagedType.LPWStr)] ref string pszFileName, [Out, MarshalAs(UnmanagedType.LPStruct)] out AM_MEDIA_TYPE pmt);
        }

        [ComVisible(true), ComImport(), Guid("670d1d20-a068-11d0-b3f0-00aa003761c5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAMCopyCaptureFileProgress
        {
            int Progress(int iProgress);
        }


        [ComVisible(true), ComImport(), Guid("C6E13370-30AC-11d0-A18C-00A0C9118956"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAMCameraControl
        {
            int GetRange([In] CameraControlProperty Property, [In, Out] ref int pMin, [In, Out] ref int pMax, [In, Out] ref int pSteppingDelta, [In, Out] ref int pDefault, [In, Out] ref int pCapsFlag);
            int Set([In] CameraControlProperty Property, [In] int lValue, [In] int Flags);
            int Get([In] CameraControlProperty Property, [In, Out] ref int lValue, [In, Out] ref int Flags);
        }


        [ComVisible(true), ComImport(), Guid("C6E13360-30AC-11d0-A18C-00A0C9118956"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAMVideoProcAmp
        {
            int GetRange([In] VideoProcAmpProperty Property, [In, Out] ref int pMin, [In, Out] ref int pMax, [In, Out] ref int pSteppingDelta, [In, Out] ref int pDefault, [In, Out] ref int pCapsFlag);
            int Set([In] VideoProcAmpProperty Property, [In] int lValue, [In] int Flags);
            int Get([In] VideoProcAmpProperty Property, [In, Out] ref int lValue, [In, Out] ref int Flags);
        }


        [ComVisible(true), ComImport(), Guid("6A2E0670-28E4-11D0-A18C-00A0C9118956"), System.Security.SuppressUnmanagedCodeSecurity, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAMVideoControl
        {
            int GetCaps([In] IPin pPin, [Out] out int pCapsFlags);
            int SetMode([In] IPin pPin, [In] int Mode);
            int GetMode([In] IPin pPin, [Out] out int Mode);
            int GetCurrentActualFrameRate([In] IPin pPin, [Out] out long ActualFrameRate);
            int GetMaxAvailableFrameRate([In] IPin pPin, [In] int iIndex, [In] Size Dimensions, [Out] out long MaxAvailableFrameRate);
            int GetFrameRateList([In] IPin pPin, [In] int iIndex, [In] Size Dimensions, [Out] out int ListSize, [Out] out IntPtr FrameRates);
        }

        [ComVisible(true), ComImport(), Guid("56a86895-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IBaseFilter
        {
            // Inherits IPersist
            int GetClassID([Out] out Guid pClassID);

            // Inherits IMediaControl
            int Stop();
            int Pause();
            int Run(long tStart);
            int GetState(int dwMilliSecsTimeout, [In, Out] ref int filtState);
            int SetSyncSource([In] IReferenceClock pClock);
            int GetSyncSource([In, Out] ref IReferenceClock pClock);

            // -----
            int EnumPins([In, Out] ref IEnumPins ppEnum);
            int FindPin([In, MarshalAs(UnmanagedType.LPWStr)] string Id, [In, Out] ref IPin ppPin);
            int QueryFilterInfo([Out] FILTER_INFO pInfo);
            int JoinFilterGraph([In] IFilterGraph pGraph, [In, MarshalAs(UnmanagedType.LPWStr)] string pName);
            int QueryVendorInfo([In, Out, MarshalAs(UnmanagedType.LPWStr)] ref string pVendorInfo);
        }


        /// <summary>
        /// フィルタ グラフ内のフィルタを列挙するインタフェース.
        /// </summary>
        [ComVisible(true), ComImport(), Guid("56a86893-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IEnumFilters
        {
            int Next([In] int cFilters, [In, Out] ref IBaseFilter ppFilter, [In, Out] ref int pcFetched);
            int Skip([In] int cFilters);
            void Reset();
            void Clone([In, Out] ref IEnumFilters ppEnum);
        }

        [ComVisible(true), ComImport(), Guid("C6E13340-30AC-11d0-A18C-00A0C9118956"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAMStreamConfig
        {
            int SetFormat([In, MarshalAs(UnmanagedType.LPStruct)] AM_MEDIA_TYPE pmt);
            int GetFormat([In, Out, MarshalAs(UnmanagedType.LPStruct)] ref AM_MEDIA_TYPE ppmt);
            int GetNumberOfCapabilities(ref int piCount, ref int piSize);
            int GetStreamCaps(int iIndex, [In, Out, MarshalAs(UnmanagedType.LPStruct)] ref AM_MEDIA_TYPE ppmt, IntPtr pSCC);
        }

        [ComVisible(true), ComImport(), Guid("56a8689a-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMediaSample
        {
            int GetPointer(ref IntPtr ppBuffer);
            int GetSize();
            int GetTime(ref long pTimeStart, ref long pTimeEnd);
            int SetTime([In, MarshalAs(UnmanagedType.LPStruct)] UInt64 pTimeStart, [In, MarshalAs(UnmanagedType.LPStruct)] UInt64 pTimeEnd);
            int IsSyncPoint();
            int SetSyncPoint([In, MarshalAs(UnmanagedType.Bool)] bool bIsSyncPoint);
            int IsPreroll();
            int SetPreroll([In, MarshalAs(UnmanagedType.Bool)] bool bIsPreroll);
            int GetActualDataLength();
            int SetActualDataLength(int len);
            int GetMediaType([In, Out, MarshalAs(UnmanagedType.LPStruct)] ref AM_MEDIA_TYPE ppMediaType);
            int SetMediaType([In, MarshalAs(UnmanagedType.LPStruct)] AM_MEDIA_TYPE pMediaType);
            int IsDiscontinuity();
            int SetDiscontinuity([In, MarshalAs(UnmanagedType.Bool)] bool bDiscontinuity);
            int GetMediaTime(ref long pTimeStart, ref long pTimeEnd);
            int SetMediaTime([In, MarshalAs(UnmanagedType.LPStruct)] UInt64 pTimeStart, [In, MarshalAs(UnmanagedType.LPStruct)] UInt64 pTimeEnd);
        }

        [ComVisible(true), ComImport(), Guid("89c31040-846b-11ce-97d3-00aa0055595a"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IEnumMediaTypes
        {
            int Next([In] int cMediaTypes, [In, Out, MarshalAs(UnmanagedType.LPStruct)] ref AM_MEDIA_TYPE ppMediaTypes, [In, Out] ref int pcFetched);
            int Skip([In] int cMediaTypes);
            int Reset();
            int Clone([In, Out] ref IEnumMediaTypes ppEnum);
        }

        [ComVisible(true), ComImport(), Guid("56a86891-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IPin
        {
            int Connect([In] IPin pReceivePin, [In, MarshalAs(UnmanagedType.LPStruct)] AM_MEDIA_TYPE pmt);
            int ReceiveConnection([In] IPin pReceivePin, [In, MarshalAs(UnmanagedType.LPStruct)] AM_MEDIA_TYPE pmt);
            int Disconnect();
            int ConnectedTo([In, Out] ref IPin ppPin);
            int ConnectionMediaType([Out, MarshalAs(UnmanagedType.LPStruct)] AM_MEDIA_TYPE pmt);
            int QueryPinInfo([Out] PIN_INFO pInfo);
            int QueryDirection(ref PIN_DIRECTION pPinDir);
            int QueryId([In, Out, MarshalAs(UnmanagedType.LPWStr)] ref string Id);
            int QueryAccept([In, MarshalAs(UnmanagedType.LPStruct)] AM_MEDIA_TYPE pmt);
            int EnumMediaTypes([In, Out] ref IEnumMediaTypes ppEnum);
            int QueryInternalConnections(IntPtr apPin, [In, Out] ref int nPin);
            int EndOfStream();
            int BeginFlush();
            int EndFlush();
            int NewSegment(long tStart, long tStop, double dRate);
        }

        [ComVisible(true), ComImport(), Guid("56a86892-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IEnumPins
        {
            int Next([In] int cPins, [In, Out] ref IPin ppPins, [In, Out] ref int pcFetched);
            int Skip([In] int cPins);
            void Reset();
            void Clone([In, Out] ref IEnumPins ppEnum);
        }

        [ComVisible(true), ComImport(), Guid("56a86897-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IReferenceClock
        {
            int GetTime(ref long pTime);
            int AdviseTime(long baseTime, long streamTime, IntPtr hEvent, ref int pdwAdviseCookie);
            int AdvisePeriodic(long startTime, long periodTime, IntPtr hSemaphore, ref int pdwAdviseCookie);
            int Unadvise(int dwAdviseCookie);
        }

        [ComVisible(true), ComImport(), Guid("29840822-5B84-11D0-BD3B-00A0C911CE86"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface ICreateDevEnum
        {
            int CreateClassEnumerator([In] ref Guid pType, [In, Out] ref System.Runtime.InteropServices.ComTypes.IEnumMoniker ppEnumMoniker, [In] int dwFlags);
        }

        [ComVisible(true), ComImport(), Guid("55272A00-42CB-11CE-8135-00AA004BB851"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IPropertyBag
        {
            int Read([MarshalAs(UnmanagedType.LPWStr)] string PropName, ref object Var, int ErrorLog);
            int Write(string PropName, ref object Var);
        }

        [ComVisible(true), ComImport(), Guid("6B652FFF-11FE-4fce-92AD-0266B5D7C78F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface ISampleGrabber
        {
            int SetOneShot([In, MarshalAs(UnmanagedType.Bool)] bool OneShot);
            int SetMediaType([In, MarshalAs(UnmanagedType.LPStruct)] AM_MEDIA_TYPE pmt);
            int GetConnectedMediaType([Out, MarshalAs(UnmanagedType.LPStruct)] AM_MEDIA_TYPE pmt);
            int SetBufferSamples([In, MarshalAs(UnmanagedType.Bool)] bool BufferThem);
            int GetCurrentBuffer(ref int pBufferSize, IntPtr pBuffer);
            int GetCurrentSample(IntPtr ppSample);
            int SetCallback(ISampleGrabberCB pCallback, int WhichMethodToCallback);
        }

        [ComVisible(true), ComImport(), Guid("0579154A-2B53-4994-B0D0-E773148EFF85"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface ISampleGrabberCB
        {
            [PreserveSig()]
            int SampleCB(double SampleTime, IMediaSample pSample);
            [PreserveSig()]
            int BufferCB(double SampleTime, IntPtr pBuffer, int BufferLen);
        }

        #endregion


        #region Structure

        [Serializable]
        [StructLayout(LayoutKind.Sequential), ComVisible(false)]
        public class AM_MEDIA_TYPE
        {
            public Guid MajorType;
            public Guid SubType;
            [MarshalAs(UnmanagedType.Bool)]
            public bool bFixedSizeSamples;
            [MarshalAs(UnmanagedType.Bool)]
            public bool bTemporalCompression;
            public uint lSampleSize;
            public Guid FormatType;
            public IntPtr pUnk;
            public uint cbFormat;
            public IntPtr pbFormat;
        }

        [Serializable]
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode), ComVisible(false)]
        public class FILTER_INFO
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string achName;
            [MarshalAs(UnmanagedType.IUnknown)]
            public object pGraph;
        }

        [Serializable]
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode), ComVisible(false)]
        public class PIN_INFO
        {
            public IBaseFilter pFilter;
            public PIN_DIRECTION dir;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string achName;
        }

        [Serializable]
        [StructLayout(LayoutKind.Sequential, Pack = 8), ComVisible(false)]
        public struct VIDEO_STREAM_CONFIG_CAPS
        {
            public Guid Guid;
            public uint VideoStandard;
            public SIZE InputSize;
            public SIZE MinCroppingSize;
            public SIZE MaxCroppingSize;
            public int CropGranularityX;
            public int CropGranularityY;
            public int CropAlignX;
            public int CropAlignY;
            public SIZE MinOutputSize;
            public SIZE MaxOutputSize;
            public int OutputGranularityX;
            public int OutputGranularityY;
            public int StretchTapsX;
            public int StretchTapsY;
            public int ShrinkTapsX;
            public int ShrinkTapsY;
            public long MinFrameInterval;
            public long MaxFrameInterval;
            public int MinBitsPerSecond;
            public int MaxBitsPerSecond;
        }

        [Serializable]
        [StructLayout(LayoutKind.Sequential), ComVisible(false)]
        public struct VIDEOINFOHEADER
        {
            public RECT SrcRect;
            public RECT TrgRect;
            public int BitRate;
            public int BitErrorRate;
            public long AvgTimePerFrame;
            public BITMAPINFOHEADER bmiHeader;
        }

        [Serializable]
        [StructLayout(LayoutKind.Sequential), ComVisible(false)]
        public struct VIDEOINFOHEADER2
        {
            public RECT SrcRect;
            public RECT TrgRect;
            public int BitRate;
            public int BitErrorRate;
            public long AvgTimePerFrame;
            public int InterlaceFlags;
            public int CopyProtectFlags;
            public int PictAspectRatioX;
            public int PictAspectRatioY;
            public int ControlFlags; // or Reserved1
            public int Reserved2;
            public BITMAPINFOHEADER bmiHeader;
        }

        [Serializable]
        [StructLayout(LayoutKind.Sequential, Pack = 2), ComVisible(false)]
        public struct BITMAPINFOHEADER
        {
            public int biSize;
            public int biWidth;
            public int biHeight;
            public short biPlanes;
            public short biBitCount;
            public int biCompression;
            public int biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public int biClrUsed;
            public int biClrImportant;
        }

        [Serializable]
        [StructLayout(LayoutKind.Sequential), ComVisible(false)]
        public struct WAVEFORMATEX
        {
            public ushort wFormatTag;
            public ushort nChannels;
            public uint nSamplesPerSec;
            public uint nAvgBytesPerSec;
            public short nBlockAlign;
            public short wBitsPerSample;
            public short cbSize;
        }

        [Serializable]
        [StructLayout(LayoutKind.Sequential, Pack = 8), ComVisible(false)]
        public struct SIZE
        {
            public int cx;
            public int cy;
            public override string ToString() { return string.Format("{{{0}, {1}}}", cx, cy); } // for debugging.
        }

        [Serializable]
        [StructLayout(LayoutKind.Sequential), ComVisible(false)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
            public override string ToString() { return string.Format("{{{0}, {1}, {2}, {3}}}", Left, Top, Right, Bottom); } // for debugging.
        }
        #endregion


        #region Enum

        [ComVisible(false)]
        public enum PIN_DIRECTION
        {
            PINDIR_INPUT = 0,
            PINDIR_OUTPUT = 1,
        }

        [ComVisible(false)]
        public enum FILTER_STATE : int
        {
            Stopped = 0,
            Paused = 1,
            Running = 2,
        }

        [ComVisible(false)]
        public enum CameraControlProperty
        {
            Pan = 0,
            Tilt = 1,
            Roll = 2,
            Zoom = 3,
            Exposure = 4,
            Iris = 5,
            Focus = 6,
        }

        [ComVisible(false), Flags()]
        public enum CameraControlFlags
        {
            Auto = 0x0001,
            Manual = 0x0002,
        }

        [ComVisible(false)]
        public enum VideoProcAmpProperty
        {
            Brightness = 0,
            Contrast = 1,
            Hue = 2,
            Saturation = 3,
            Sharpness = 4,
            Gamma = 5,
            ColorEnable = 6,
            WhiteBalance = 7,
            BacklightCompensation = 8,
            Gain = 9
        }

        #endregion


        #region Guid

        public static class DsGuid
        {
            // MediaType
            public static readonly Guid MEDIATYPE_Video = new Guid("{73646976-0000-0010-8000-00AA00389B71}");
            public static readonly Guid MEDIATYPE_Audio = new Guid("{73647561-0000-0010-8000-00AA00389B71}");

            // SubType
            public static readonly Guid MEDIASUBTYPE_None = new Guid("{E436EB8E-524F-11CE-9F53-0020AF0BA770}");
            public static readonly Guid MEDIASUBTYPE_YUYV = new Guid("{56595559-0000-0010-8000-00AA00389B71}");
            public static readonly Guid MEDIASUBTYPE_IYUV = new Guid("{56555949-0000-0010-8000-00AA00389B71}");
            public static readonly Guid MEDIASUBTYPE_YVU9 = new Guid("{39555659-0000-0010-8000-00AA00389B71}");
            public static readonly Guid MEDIASUBTYPE_YUY2 = new Guid("{32595559-0000-0010-8000-00AA00389B71}");
            public static readonly Guid MEDIASUBTYPE_YVYU = new Guid("{55595659-0000-0010-8000-00AA00389B71}");
            public static readonly Guid MEDIASUBTYPE_UYVY = new Guid("{59565955-0000-0010-8000-00AA00389B71}");
            public static readonly Guid MEDIASUBTYPE_MJPG = new Guid("{47504A4D-0000-0010-8000-00AA00389B71}");
            public static readonly Guid MEDIASUBTYPE_RGB565 = new Guid("{E436EB7B-524F-11CE-9F53-0020AF0BA770}");
            public static readonly Guid MEDIASUBTYPE_RGB555 = new Guid("{E436EB7C-524F-11CE-9F53-0020AF0BA770}");
            public static readonly Guid MEDIASUBTYPE_RGB24 = new Guid("{E436EB7D-524F-11CE-9F53-0020AF0BA770}");
            public static readonly Guid MEDIASUBTYPE_RGB32 = new Guid("{E436EB7E-524F-11CE-9F53-0020AF0BA770}");
            public static readonly Guid MEDIASUBTYPE_ARGB32 = new Guid("{773C9AC0-3274-11D0-B724-00AA006C1A01}");
            public static readonly Guid MEDIASUBTYPE_PCM = new Guid("{00000001-0000-0010-8000-00AA00389B71}");
            public static readonly Guid MEDIASUBTYPE_WAVE = new Guid("{E436EB8B-524F-11CE-9F53-0020AF0BA770}");

            // FormatType
            public static readonly Guid FORMAT_None = new Guid("{0F6417D6-C318-11D0-A43F-00A0C9223196}");
            public static readonly Guid FORMAT_VideoInfo = new Guid("{05589F80-C356-11CE-BF01-00AA0055595A}");
            public static readonly Guid FORMAT_VideoInfo2 = new Guid("{F72A76A0-EB0A-11d0-ACE4-0000C0CC16BA}");
            public static readonly Guid FORMAT_WaveFormatEx = new Guid("{05589F81-C356-11CE-BF01-00AA0055595A}");

            // CLSID
            public static readonly Guid CLSID_AudioInputDeviceCategory = new Guid("{33D9A762-90C8-11d0-BD43-00A0C911CE86}");
            public static readonly Guid CLSID_AudioRendererCategory = new Guid("{E0F158E1-CB04-11d0-BD4E-00A0C911CE86}");
            public static readonly Guid CLSID_VideoInputDeviceCategory = new Guid("{860BB310-5D01-11d0-BD3B-00A0C911CE86}");
            public static readonly Guid CLSID_VideoCompressorCategory = new Guid("{33D9A760-90C8-11d0-BD43-00A0C911CE86}");

            public static readonly Guid CLSID_NullRenderer = new Guid("{C1F400A4-3F08-11D3-9F0B-006008039E37}");
            public static readonly Guid CLSID_SampleGrabber = new Guid("{C1F400A0-3F08-11D3-9F0B-006008039E37}");

            public static readonly Guid CLSID_FilterGraph = new Guid("{E436EBB3-524F-11CE-9F53-0020AF0BA770}");
            public static readonly Guid CLSID_SystemDeviceEnum = new Guid("{62BE5D10-60EB-11d0-BD3B-00A0C911CE86}");
            public static readonly Guid CLSID_CaptureGraphBuilder2 = new Guid("{BF87B6E1-8C27-11d0-B3F0-00AA003761C5}");

            public static readonly Guid IID_IPropertyBag = new Guid("{55272A00-42CB-11CE-8135-00AA004BB851}");
            public static readonly Guid IID_IBaseFilter = new Guid("{56a86895-0ad4-11ce-b03a-0020af0ba770}");
            public static readonly Guid IID_IAMStreamConfig = new Guid("{C6E13340-30AC-11d0-A18C-00A0C9118956}");

            public static readonly Guid PIN_CATEGORY_CAPTURE = new Guid("{fb6c4281-0353-11d1-905f-0000c0cc16ba}");
            public static readonly Guid PIN_CATEGORY_PREVIEW = new Guid("{fb6c4282-0353-11d1-905f-0000c0cc16ba}");
            public static readonly Guid PIN_CATEGORY_STILL = new Guid("{fb6c428a-0353-11d1-905f-0000c0cc16ba}");

            private static Dictionary<string, Guid> NicknameCache = null;

            /// <summary>
            /// Guidをわかりやすい文字列で返す。
            /// MEDIATYPE_Videoなら[Video]を返す。PIN_CATEGORY_CAPTUREなら[CATEGORY_CAPTURE]を返す。
            /// </summary>
            public static string GetNickname(Guid guid)
            {
                // リフレクションでstatic public GuidのDictionaryを作成。結果はキャッシュしておく。
                if (NicknameCache == null)
                {
                    NicknameCache = typeof(DsGuid).GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)
                        .Where(x => x.FieldType == typeof(Guid))
                        .ToDictionary(x => x.Name, x => (Guid)x.GetValue(null));
                }

                var items = NicknameCache.Where(x => x.Value == guid);
                if (items.Any())
                {
                    var item = items.FirstOrDefault();
                    var elem = item.Key.Split('_');

                    // '_'で分割して、2個目以降を連結する。
                    // MEDIATYPE_Videoなら[Video]を返す。
                    // PIN_CATEGORY_CAPTUREなら[CATEGORY_CAPTURE]を返す。
                    if (elem.Length >= 2)
                    {
                        var text = string.Join("_", elem.Skip(1).ToArray());
                        return string.Format("[{0}]", text);
                    }
                    else
                    {
                        return item.Key;
                    }
                }

                // 対応してない場合はToStringを呼び出す。
                return guid.ToString();
            }
        }
        #endregion
    }
}
