using System;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using Microsoft.Wpf.Interop.DirectX;

namespace FFMEGE {
    public partial class MediaElement : UserControl, IDisposable {

        private readonly Image ViewBox = new Image();
        private readonly D3D11Image d3dImage = new D3D11Image();
        private readonly Grid host = new Grid();

        private int ID;
        private Window HostWindow;

        private Uri uri;

        private bool Shutdown = false;
        private bool MediaPlaying;
        private bool MediaStopping = false;
        private bool MediaStopped = false;
        private bool MediaPause = false;

        private WriteableBitmap TargetBitmap;

        public MediaElement(int id, Window window) {
            if (window == null)
                throw new NullReferenceException("Window is Null");

            ID = id;
            if (CurrentHWDecode == HWDecode.D3D11) {
                //Set Grid as base control with nested Image with D3DSource
                Content = host;
                host.Margin = new Thickness(0);
                host.Background = Brushes.Black;
                host.Loaded += Host_Loaded;
                host.Children.Add(ViewBox);

                ViewBox.Source = d3dImage;
                HostWindow = window;

                this.SizeChanged += Img_SizeChanged;
            }
            else {
                Content = host;
                host.Margin = new Thickness(0);
                host.Background = Brushes.Black;
                host.Children.Add(ViewBox);
            }

            //Register FFmpeg DLL's
            RegisterFFmpeg();

            this.Unloaded += MediaElement_Unloaded;

            GenLock.TimerStart();
        }


        #region Events
        private void Host_Loaded(object sender, RoutedEventArgs e) {
            //Cant setup until Grid is loaded and part of the WPF window.
            d3dImage.WindowOwner = (new System.Windows.Interop.WindowInteropHelper(HostWindow)).Handle;
            this.SizeChanged += Img_SizeChanged;
            d3dImage.OnRender = DoRender;

            d3dImage.RequestRender();
        }

        private void Img_SizeChanged(object sender, SizeChangedEventArgs e) {
            double dpiScale = 1.0; // default value for 96 dpi

            // determine DPI
            // (as of .NET 4.6.1, this returns the DPI of the primary monitor, if you have several different DPIs)            
            if (PresentationSource.FromVisual(this).CompositionTarget is HwndTarget hwndTarget) {
                dpiScale = hwndTarget.TransformToDevice.M11;
            }

            int surfWidth = (int)(host.ActualWidth < 0 ? 0 : Math.Ceiling(host.ActualWidth * dpiScale));
            int surfHeight = (int)(host.ActualHeight < 0 ? 0 : Math.Ceiling(host.ActualHeight * dpiScale));

            // Notify the D3D11Image of the pixel size desired for the DirectX rendering.
            // The D3DRendering component will determine the size of the new surface it is given, at that point.
            d3dImage.SetPixelSize(surfWidth, surfHeight);

        }

        private void MediaElement_Unloaded(object sender, RoutedEventArgs e) {
            Shutdown = true;
            ShutdownD3D();
        }

        #endregion

        #region Methods

        private void WriteLine(string str) {
            System.Diagnostics.Debug.WriteLine(ID + ": " + str);
        }

        public void Play() {
            MediaStopping = false;
            MediaStopped = false;
            MediaPlaying = true;

            if (PlaybackThread != null && PlaybackThread.ThreadState == ThreadState.Running)
                return;

            //create new thread if thread is stopped.
            if (PlaybackThread != null && PlaybackThread.ThreadState == System.Threading.ThreadState.Stopped)
                PlaybackThread = null;

            ViewBox.Visibility = Visibility.Visible;

            //create new thread
            if (PlaybackThread == null) {
                PlaybackThread = new Thread(new ThreadStart(Decoder)) {
                    Name = "FFMEGE - Playback Thread",
                    Priority = ThreadPriority.Normal
                };
                PlaybackThread.SetApartmentState(ApartmentState.STA);
                PlaybackThread.IsBackground = true;

                PlaybackThread.Start();
            }
        }
        public void Pause() {
            MediaPause = !MediaPause;
        }

        public unsafe void Stop() {

            if (MediaPlaying) {
                MediaStopping = true;
            }

            TargetBitmap = null;
            //Dont clear the source for D3D
            //if (CurrentHWDecode != HWDecode.D3D11)
            //    ViewBox.Source = null;
            //ViewBox.InvalidateVisual();

            ViewBox.Visibility = Visibility.Hidden;
        }


        /// <summary>
        /// Software Update Present Image
        /// </summary>
        /// <param name="Width"></param>
        /// <param name="Height"></param>
        /// <param name="FrameBuffer"></param>
        /// <param name="FrameBufferLength"></param>
        /// <param name="FrameBufferStride"></param>
        private void UpdateImage(int Width, int Height, IntPtr FrameBuffer, int FrameBufferLength, int FrameBufferStride) {
            if (TargetBitmap == null) {

                ViewBox.Dispatcher.Invoke(() => {
                    using (var bitmap = new System.Drawing.Bitmap(Width, Height, FrameBufferStride, System.Drawing.Imaging.PixelFormat.Format32bppRgb, FrameBuffer)) {
                        var bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(bitmap.GetHbitmap(), IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(Width, Height));
                        TargetBitmap = new WriteableBitmap(bitmapSource);
                    }
                    ViewBox.Source = TargetBitmap;
                });
            }
            if (Shutdown == false) {
                try {
                    ViewBox.Dispatcher.Invoke(() => {
                        if (Shutdown == false) {
                            var updateRect = new Int32Rect(0, 0, Width, Height);
                            if (TargetBitmap != null)
                                TargetBitmap.WritePixels(updateRect, FrameBuffer, FrameBufferLength, FrameBufferStride);
                        }
                    });
                }
                catch { }
            }
        }

        #endregion

        #region Properties

        public Uri Source {
            get {
                return uri;
            }
            set {
                if (MediaPlaying) {
                    Stop();
                    uri = value;
                    Play();
                }
                else {
                    uri = value;
                    Play();
                }
            }
        }

        public Stretch Stretch {
            get {
                return ViewBox.Stretch;
            }
            set {
                ViewBox.Stretch = value;
            }
        }

        public bool IsPlaying {
            get {
                return IsPlaying;
            }
        }

        #endregion

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue) {
                if (disposing) {
                    // TODO: dispose managed state (managed objects).
                    d3dImage.Dispose();
                }
                GenLock.TimerStop();
                ShutdownD3D();
                // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                // TODO: set large fields to null.

                disposedValue = true;
            }
        }

        // TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
        // ~MediaElement() {
        //   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
        //   Dispose(false);
        // }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}