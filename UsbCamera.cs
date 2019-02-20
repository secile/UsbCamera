using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Runtime.InteropServices;
using DShowLib;

namespace GitHub.secile.Video
{
    // [How to use]
    // string[] devices = UsbCamera.FindDevices();
    // if (devices.Length == 0) return; // not connected.
    //
    // var index = 0;
    // var camera = new UsbCamera(index, new Size(640, 480));
    // camera.Start();
    //
    // [A few seconds later...]
    //
    // var bmp = camera.GetBitmap();

    public class UsbCamera
    {
        /// <summary>Usb camera image size.</summary>
        public Size Size { get; private set; }

        /// <summary>Start using.</summary>
        public Action Start { get; private set; }

        /// <summary>Stop using.</summary>
        public Action Stop { get; private set; }

        /// <summary>Release resource.</summary>
        public Action Release { get; private set; }

        /// <summary>Get image.</summary>
        /// <remarks>Immediately after starting, images may not be acquired.</remarks>
        public Func<Bitmap> GetBitmap { get; private set; }

        /// <summary>
        /// Get available USB camera list.
        /// </summary>
        /// <returns>Array of camera name, or if no device found, zero length array/</returns>
        public static string[] FindDevices()
        {
            try
            {
                var list = DSUtility.EnumFilters(DSConst.FilterCategoryGUID.CLSID_VideoInputDeviceCategory);
                return list.Select(x => x.Name).ToArray();
            }
            catch (Exception)
            {
                return new string[0]; // not connected.
            }
        }

        /// <summary>
        /// Get video formats.
        /// </summary>
        public static VideoFormat[] GetVideoFormat(int camerIndex)
        {
            var filter = DSUtility.CreateFilter(DSConst.FilterCategoryGUID.CLSID_VideoInputDeviceCategory, camerIndex);
            var pin = DSUtility.FindPin(filter, 0, DSConst.PIN_DIRECTION.PINDIR_OUTPUT);
            return GetVideoOutputFormat(pin);
        }

        /// <summary>
        /// Create USB Camera.
        /// </summary>
        /// <param name="camerIndex">Camera index in FindDevices() result.</param>
        /// <param name="size">
        /// Size you want to create. If camera does not support the size, created with default size.
        /// Check Size property to know actual created size.
        /// </param>
        public UsbCamera(int camerIndex, Size size)
        {
            var camera_list = FindDevices();
            if (camerIndex >= camera_list.Length) throw new ArgumentException("USB camera is not available.", "index");
            Init(camerIndex, size);
        }

        private void Init(int index, Size size)
        {
            //----------------------------------
            // Create Filter Graph
            //----------------------------------
            // +--------------------+  +----------------+  +---------------+
            // |Video Capture Source|→| Sample Grabber |→| Null Renderer |
            // +--------------------+  +----------------+  +---------------+
            //                                 ↓GetBitmap()

            var graph = DSUtility.CreateNewGraph();

            //----------------------------------
            // VideoCaptureSource
            //----------------------------------
            var vcap_source = CreateVideoCaptureSource(index, size);
            graph.AddFilter(vcap_source, "VideoCapture");

            //------------------------------
            // SampleGrabber
            //------------------------------
            var grabber = CreateSampleGrabber();
            graph.AddFilter(grabber, "SampleGrabber");
            var i_grabber = (DSInterface.ISampleGrabber)grabber;
            i_grabber.SetBufferSamples(true); //サンプルグラバでのサンプリングを開始

            //---------------------------------------------------
            // Null Renderer
            //---------------------------------------------------
            var renderer = CreateNullRenderer();
            graph.AddFilter(renderer, "NullRenderer");

            //---------------------------------------------------
            // Create Filter Graph
            //---------------------------------------------------
            var builder = DSUtility.CoCreateInstance(DSConst.ClassGUID.CLSID_CaptureGraphBuilder2) as DShowLib.DSInterface.ICaptureGraphBuilder2;
            builder.SetFiltergraph(graph);
            var pinCategory = new Guid(DShowLib.DSConst.PinCategoryGUID.PIN_CATEGORY_CAPTURE);
            var mediaType = new Guid(DShowLib.DSConst.MediaTypeGUID.MEDIATYPE_Video);
            builder.RenderStream(ref pinCategory, ref mediaType, vcap_source, grabber, renderer);

            // SampleGrabber Format.
            {
                var mt = new DSStructure.AM_MEDIA_TYPE();
                i_grabber.GetConnectedMediaType(mt);
                var header = (DSStructure.DSVIDEOINFOHEADER)Marshal.PtrToStructure(mt.formatPtr, typeof(DSStructure.DSVIDEOINFOHEADER));
                var width = header.BmiHeader.Width;
                var height = header.BmiHeader.Height;
                var stride = width * (header.BmiHeader.BitCount / 8);
                DSUtility.DeleteMediaType(ref mt);

                Size = new Size(width, height);
                GetBitmap = () => GetBitmapMain(i_grabber, width, height, stride);
            }

            Start = () => DSUtilityPlay.GraphPlay(graph);
            Stop  = () => DSUtilityPlay.GraphStop(graph);
            Release = () =>
            {
                Stop();
                DSUtility.ReleaseInstance(ref i_grabber);
                DSUtility.ReleaseInstance(ref builder);
                DSUtility.ReleaseInstance(ref graph);
            };
        }

        private Bitmap GetBitmapMain(DSInterface.ISampleGrabber i_grabber, int width, int height, int stride)
        {
            try
            {
                return GetBitmapMainMain(i_grabber, width, height, stride);
            }
            catch (COMException ex)
            {
                if (ex.ErrorCode == (int)DSConst.DirectShowHRESULT.VFW_E_WRONG_STATE)
                {
                    // image data is not ready yet.
                    // returns empty bitmap.
                    return new Bitmap(width, height);
                }

                throw;
            }
        }

        /// <summary>サンプルグラバから画像を取得する。</summary>
        private Bitmap GetBitmapMainMain(DSInterface.ISampleGrabber i_grabber, int width, int height, int stride)
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
        private DSInterface.IBaseFilter CreateSampleGrabber()
        {
            var filter = DSUtility.CreateFilter(DSConst.FilterGUID.CLSID_SampleGrabber);
            DSInterface.ISampleGrabber i_grabber = (DSInterface.ISampleGrabber)filter;

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

            var mt = new DSStructure.AM_MEDIA_TYPE();
            mt.majorType = new Guid(DSConst.MediaTypeGUID.MEDIATYPE_Video);
            mt.subType = new Guid(DSConst.MediaTypeGUID.MEDIASUBTYPE_RGB24);
            i_grabber.SetMediaType(mt);
            return filter;
        }

        /// <summary>
        /// Nullレンダラフィルタを作成する。
        /// </summary>
        /// <returns></returns>
        private DSInterface.IBaseFilter CreateNullRenderer()
        {
            var filter = DSUtility.CreateFilter(DSConst.FilterGUID.CLSID_NullRenderer);
            return filter;
        }

        /// <summary>
        /// Video Capture Sourceフィルタを作成する
        /// </summary>
        private DSInterface.IBaseFilter CreateVideoCaptureSource(int index, Size size)
        {
            var filter = DSUtility.CreateFilter(DSConst.FilterCategoryGUID.CLSID_VideoInputDeviceCategory, index);
            var pin = DSUtility.FindPin(filter, 0, DSConst.PIN_DIRECTION.PINDIR_OUTPUT);
            SetVideoOutputFormat(pin, size, 0);
            return filter;
        }

        /// <summary>
        /// ビデオキャプチャデバイスの出力形式を選択する。
        /// フォーマットの列挙で同サイズがあれば、そのフォーマットを設定する。
        /// 存在しなかった場合は出力形式を変更しない。
        /// </summary>
        /// <param name="pin">ビデオキャプチャデバイスの出力ピン</param>
        /// <param name="size">指定のサイズ</param>
        /// <param name="fps">フレームレートを指定する。0のとき変更しない(デフォルト)。</param>
        private static bool SetVideoOutputFormat(DSInterface.IPin pin, Size size, double fps)
        {
            var vformat = GetVideoOutputFormat(pin);
            
            // for debug
            // for (int i = 0; i < vformat.Length; i++) Console.WriteLine("{0}:{1}", i, vformat[i]);

            for (int i = 0; i < vformat.Length; i++)
            {
                if (vformat[i].MajorType == DSUtility.GetMediaTypeName(DSConst.MediaTypeGUID.MEDIATYPE_Video))
                {
                    // MajorTypeがVideoの場合、SubTypeは色空間を表す。
                    // BuffaloのWebカメラは[YUY2]と[MPEG]だった。
                    // マイクロビジョンのUSBカメラは[YUY2]と[YUVY]だった。
                    // 固定できないためコメントアウト。最初に見つかったフォーマットを利用する。
                    // if (vformat[i].SubType == DSUtility.GetMediaTypeName(DSConst.MediaTypeGUID.MEDIASUBTYPE_YUY2))

                    // FORMAT_VideoInfoのみ対応する。(FORMAT_VideoInfo2はSampleGrabber未対応のためエラー。)
                    // https://msdn.microsoft.com/ja-jp/library/cc370616.aspx
                    if (DSUtility.CompGUIDString(vformat[i].Caps.Guid.ToString(), DSConst.FormatTypeGUID.FORMAT_VideoInfo))
                    {
                        if (vformat[i].Size.Width == size.Width && vformat[i].Size.Height == size.Height)
                        {
                            SetVideoOutputFormat(pin, i, size, fps);
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// ビデオキャプチャデバイスがサポートするメディアタイプ・サイズを取得する。
        /// </summary>
        private static VideoFormat[] GetVideoOutputFormat(DSInterface.IPin pin)
        {
            // IAMStreamConfigインタフェース取得
            var config = pin as DSInterface.IAMStreamConfig;
            if (config == null)
            {
                throw new DSUtilityException.DSException("IAMStreamConfigインタフェースを取得できません。");
            }

            // フォーマット個数取得
            int cap_count = 0, cap_size = 0;
            config.GetNumberOfCapabilities(ref cap_count, ref cap_size);
            if (cap_size != Marshal.SizeOf(typeof(VIDEO_STREAM_CONFIG_CAPS)))
            {
                throw new DSUtilityException.DSException("VIDEO_STREAM_CONFIG_CAPSを取得できません。");
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
                DSStructure.AM_MEDIA_TYPE mt = null;
                config.GetStreamCaps(i, out mt, cap_data);
                entry.Caps = DSUtility.PtrToStructure<VIDEO_STREAM_CONFIG_CAPS>(cap_data);

                // フォーマット情報の読み取り
                entry.MajorType = DSUtility.GetMediaTypeName(mt.majorType);
                entry.SubType = DSUtility.GetMediaTypeName(mt.subType);

                if (DSUtility.CompGUIDString(mt.formatType.ToString(), DSConst.FormatTypeGUID.FORMAT_VideoInfo))
                {
                    var vinfo = DSUtility.PtrToStructure<DSStructure.DSVIDEOINFOHEADER>(mt.formatPtr);
                    entry.Size = new Size(vinfo.BmiHeader.Width, vinfo.BmiHeader.Height);
                    entry.TimePerFrame = vinfo.AvgTimePerFrame;
                }
                else if (DSUtility.CompGUIDString(mt.formatType.ToString(), DSConst.FormatTypeGUID.FORMAT_VideoInfo2))
                {
                    var vinfo = DSUtility.PtrToStructure<DSVIDEOINFOHEADER2>(mt.formatPtr);
                    entry.Size = new Size(vinfo.BmiHeader.Width, vinfo.BmiHeader.Height);
                    entry.TimePerFrame = vinfo.AvgTimePerFrame;
                }

                // 解放
                DSUtility.DeleteMediaType(ref mt);

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
        /// <param name="fps">0以上を指定するとフレームレートを変更する。事前にVIDEO_STREAM_CONFIG_CAPSで取得した可能範囲内を指定すること。</param>
        private static void SetVideoOutputFormat(DSInterface.IPin pin, int index, Size size, double fps)
        {
            // IAMStreamConfigインタフェース取得
            var config = pin as DSInterface.IAMStreamConfig;
            if (config == null)
            {
                throw new DSUtilityException.DSException("ピンはIAMStreamConfigインタフェースを公開しません。");
            }

            // フォーマット個数取得
            int cap_count = 0, cap_size = 0;
            config.GetNumberOfCapabilities(ref cap_count, ref cap_size);
            if (cap_size != Marshal.SizeOf(typeof(VIDEO_STREAM_CONFIG_CAPS)))
            {
                throw new DSUtilityException.DSException("VIDEO_STREAM_CONFIG_CAPSを取得できません。");
            }

            // データ用領域確保
            var cap_data = Marshal.AllocHGlobal(cap_size);

            // idx番目のフォーマット情報取得
            DSStructure.AM_MEDIA_TYPE mt = null;
            config.GetStreamCaps(index, out mt, cap_data);
            var cap = DSUtility.PtrToStructure<VIDEO_STREAM_CONFIG_CAPS>(cap_data);
            // 仕様ではVideoCaptureDeviceはメディア タイプごとに一定範囲の出力フォーマットをサポートできる。例えば以下のように。
            // [0]:YUY2 最小:160x120, 最大:320x240, X軸4STEP, Y軸2STEPごと
            // [1]:RGB8 最小:640x480, 最大:640x480, X軸0STEP, Y軸0STEPごと
            // SetFormatで出力サイズとフレームレートをこの範囲内で設定可能。
            // ただし試した限り、ほとんどのUSBカメラはサイズ固定(最大・最小が同じ)で返してきた。
            // https://msdn.microsoft.com/ja-jp/library/cc353344.aspx
            // https://msdn.microsoft.com/ja-jp/library/cc371290.aspx

            if (DSUtility.CompGUIDString(mt.formatType.ToString(), DSConst.FormatTypeGUID.FORMAT_VideoInfo))
            {
                var vinfo = DSUtility.PtrToStructure<DSStructure.DSVIDEOINFOHEADER>(mt.formatPtr);
                if (!size.IsEmpty) { vinfo.BmiHeader.Width = size.Width; vinfo.BmiHeader.Height = size.Height; }
                if (fps > 0) { vinfo.AvgTimePerFrame = (long)(10000000 / fps); }
                Marshal.StructureToPtr(vinfo, mt.formatPtr, true);
            }
            else if (DSUtility.CompGUIDString(mt.formatType.ToString(), DSConst.FormatTypeGUID.FORMAT_VideoInfo2))
            {
                var vinfo = DSUtility.PtrToStructure<DSVIDEOINFOHEADER2>(mt.formatPtr);
                if (!size.IsEmpty) { vinfo.BmiHeader.Width = size.Width; vinfo.BmiHeader.Height = size.Height; }
                if (fps > 0) { vinfo.AvgTimePerFrame = (long)(10000000 / fps); }
                Marshal.StructureToPtr(vinfo, mt.formatPtr, true);
            }

            // フォーマットを選択
            config.SetFormat(mt);

            // 解放
            if (cap_data != System.IntPtr.Zero) Marshal.FreeHGlobal(cap_data);
            if (mt != null) DSUtility.DeleteMediaType(ref mt);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DSVIDEOINFOHEADER2
        {
            public DSStructure.DSRECT SrcRect;
            public DSStructure.DSRECT TagRect;
            public int BitRate;
            public int BitErrorRate;
            public long AvgTimePerFrame;
            public int InterlaceFlags;
            public int CopyProtectFlags;
            public int PictAspectRatioX;
            public int PictAspectRatioY;
            public int ControlFlags; // or Reserved1
            public int Reserved2;
            public DSStructure.DSBITMAPINFOHEADER BmiHeader;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct VIDEO_STREAM_CONFIG_CAPS
        {
            public Guid Guid;
            public uint VideoStandard;
            public Size InputSize;
            public Size MinCroppingSize;
            public Size MaxCroppingSize;
            public int CropGranularityX;
            public int CropGranularityY;
            public int CropAlignX;
            public int CropAlignY;
            public Size MinOutputSize;
            public Size MaxOutputSize;
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

        public class VideoFormat
        {
            public string MajorType { get; set; }  // [Video]など
            public string SubType { get; set; }    // [YUY2], [MJPG]など
            public Size Size { get; set; }         // ビデオサイズ
            public long TimePerFrame { get; set; } // ビデオフレームの平均表示時間を100ナノ秒単位で。30fpsのとき「333333」
            public VIDEO_STREAM_CONFIG_CAPS Caps { get; set; }

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
}
