using System;
using System.Linq;
using System.Threading;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using System.Windows.Controls;
using System.Diagnostics;

namespace FFMEGE {
    public partial class MediaElement : UserControl {
        private const int EndOfFileErrorCode = -541478725;
        private const int SuccessCode = 0;

        private static object FFLockObject = new object();
        private static bool FFmpegRegistered;
        private static string FFmpegPath = System.AppDomain.CurrentDomain.BaseDirectory + "zen";

        // setup logging @ correct Level Debug gives playback performance issues
        private static int FFmpegLogLevel = ffmpeg.AV_LOG_ERROR;
        //private static int FFmpegLogLevel = ffmpeg.AV_LOG_VERBOSE;
        //private static int FFmpegLogLevel = ffmpeg.AV_LOG_DEBUG;     

        private HWDecode CurrentHWDecode = HWDecode.D3D11;

        private enum HWDecode {
            CUDA,
            DXVA2,
            D3D11,
            SW
        }
    
        private Thread PlaybackThread;
        private unsafe av_log_set_callback_callback LogCallback;

        private unsafe void RegisterFFmpeg() {
            //Only load ffmpeg dll's once to save ram
            if (FFmpegRegistered) {
                WriteLine("FFmpeg Already Loaded");
                return;
            }

            WriteLine("Loading FFmpeg");
            NativeMethods.RegisterLibrariesSearchPath(FFmpegPath);

            //Load AVCodec Init
            ffmpeg.av_register_all();
            ffmpeg.avcodec_register_all();

            WriteLine($"FFmpeg version info: {ffmpeg.av_version_info()}");

            // setup logging @ correct Level Debug gives playback performance issues
            ffmpeg.av_log_set_level(FFmpegLogLevel);

            //Callback for logging
            LogCallback = (p0, level, format, vl) => {
                if (level > ffmpeg.av_log_get_level()) return;

                var lineSize = 1024;
                var lineBuffer = stackalloc byte[lineSize];
                var printPrefix = 1;
                ffmpeg.av_log_format_line(p0, level, format, vl, lineBuffer, lineSize, &printPrefix);
                var line = Marshal.PtrToStringAnsi((IntPtr)lineBuffer);
                WriteLine(line);
            };
            ffmpeg.av_log_set_callback(LogCallback);

            //Only load ffmpeg dll's once to save ram
            FFmpegRegistered = true;
        }

        public static unsafe string GetFFmpegErrorMessage(int code) {
            var errorStrBytes = new byte[1024];
            var errorStrPtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(sbyte)) * errorStrBytes.Length);

            ffmpeg.av_strerror(code, (byte*)errorStrPtr, (ulong)errorStrBytes.Length);
            Marshal.Copy(errorStrPtr, errorStrBytes, 0, errorStrBytes.Length);
            Marshal.FreeHGlobal(errorStrPtr);

            var errorMessage = System.Text.Encoding.ASCII.GetString(errorStrBytes).Split('\0').FirstOrDefault();
            return errorMessage;
        }

        #region HW Get Format Callbacks
        /// <summary>
        /// Cuda Video Callback when AVCodec Opening to determine if the track matches the HWAccel format
        /// </summary>
        /// <param name="avctx"></param>
        /// <param name="pix_fmts"></param>
        /// <returns></returns>
        private unsafe AVPixelFormat Get_Format_CUDA(AVCodecContext* avctx, AVPixelFormat* pix_fmts) {
            WriteLine("Get_Format: CUDA");
            while (*pix_fmts != AVPixelFormat.AV_PIX_FMT_NONE) {
                if (*pix_fmts == AVPixelFormat.AV_PIX_FMT_CUDA) {
                    //DecodeContext* decode = avctx->opaque;
                    AVHWFramesContext* frames_ctx;
                    //ffmpeg.AVQSVFramesContext* frames_hwctx;
                    int ret;

                    /* create a pool of surfaces to be used by the decoder */
                    avctx->hw_frames_ctx = ffmpeg.av_hwframe_ctx_alloc(avctx->hw_device_ctx);
                    if (avctx->hw_frames_ctx == null)
                        return AVPixelFormat.AV_PIX_FMT_NONE;

                    frames_ctx = (AVHWFramesContext*)avctx->hw_frames_ctx->data;
                    //frames_hwctx = (ffmpeg.AVQSVFramesContext*)frames_ctx->hwctx;

                    frames_ctx->format = AVPixelFormat.AV_PIX_FMT_CUDA;
                    frames_ctx->sw_format = avctx->sw_pix_fmt;
                    frames_ctx->width = avctx->coded_width;
                    frames_ctx->height = avctx->coded_height;
                    frames_ctx->initial_pool_size = 1;

                    //frames_hwctx->frame_type = (int)ffmpeg.MFXMemType.MFX_MEMTYPE_VIDEO_MEMORY_DECODER_TARGET;

                    ret = ffmpeg.av_hwframe_ctx_init(avctx->hw_frames_ctx);
                    if (ret < 0)
                        return AVPixelFormat.AV_PIX_FMT_NONE;

                    return AVPixelFormat.AV_PIX_FMT_CUDA;
                }

                pix_fmts++;
            }

            WriteLine("The CUDA pixel format not offered in get_format()\n");

            return AVPixelFormat.AV_PIX_FMT_NONE;
        }

        /// <summary>
        /// DXVA Video Callback when AVCodec Opening to determine if the track matches the HWAccel format
        /// </summary>
        /// <param name="avctx"></param>
        /// <param name="pix_fmts"></param>
        /// <returns></returns>
        private unsafe AVPixelFormat Get_Format_DXVA(AVCodecContext* avctx, AVPixelFormat* pix_fmts) {
            while (*pix_fmts != AVPixelFormat.AV_PIX_FMT_NONE) {
                if (*pix_fmts == AVPixelFormat.AV_PIX_FMT_DXVA2_VLD) {
                    return AVPixelFormat.AV_PIX_FMT_D3D11VA_VLD;
                }

                pix_fmts++;
            }

            WriteLine("The DXVA2 pixel format not offered in get_format()\n");

            return AVPixelFormat.AV_PIX_FMT_NONE;
        }

        /// <summary>
        /// D3D11 Video Callback when AVCodec Opening to determine if the track matches the HWAccel format
        /// </summary>
        /// <param name="avctx"></param>
        /// <param name="pix_fmts"></param>
        /// <returns></returns>
        private unsafe AVPixelFormat Get_Format_D3D11(AVCodecContext* avctx, AVPixelFormat* pix_fmts) {
            WriteLine("Get_Format: D3D11");

            while (*pix_fmts != AVPixelFormat.AV_PIX_FMT_NONE) {
                if (*pix_fmts == AVPixelFormat.AV_PIX_FMT_D3D11) {
                    return AVPixelFormat.AV_PIX_FMT_D3D11;
                }

                pix_fmts++;
            }

            WriteLine("The D3D11 pixel format not offered in get_format()\n");

            return AVPixelFormat.AV_PIX_FMT_NONE;
        }

        #endregion


        /// <summary>
        /// Decoder Function for Thread. All containted in one to simplify memory management of the unmanaged resources
        /// </summary>
        private unsafe void Decoder() {
            int retErr = 0;

            //Open Input Context to open media
            AVFormatContext* pInputContext = ffmpeg.avformat_alloc_context();

            if (ffmpeg.avformat_open_input(&pInputContext, uri.OriginalString, null, null) != 0)
                throw new ApplicationException(@"Could not open file");

            if (ffmpeg.avformat_find_stream_info(pInputContext, null) != 0)
                throw new ApplicationException(@"Could not find stream info");

            //Loop thru to find the stream that the video
            AVStream* pStream = null;
            for (var i = 0; i < pInputContext->nb_streams; i++)
                if (pInputContext->streams[i]->codec->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO) {
                    pStream = pInputContext->streams[i];
                    break;
                }
            if (pStream == null)
                throw new ApplicationException(@"Could not found video stream");

            WriteLine($"Codec name: {ffmpeg.avcodec_get_name(pStream->codec->codec_id)} CodecID: {pStream->codec->codec_id}");

            //Get Codec name from input stream
            string codecname = ffmpeg.avcodec_get_name(pStream->codec->codec_id);

            AVCodec* pCodec = null;

            //Open Codec, cuvid has sepearte encoders to make the hwaccel work
            if (CurrentHWDecode == HWDecode.CUDA) {
                pCodec = ffmpeg.avcodec_find_decoder_by_name(codecname + "_cuvid");
                if (pCodec == null) {
                    //use software codec if hardware type doesnt exist
                    WriteLine("Failing back to Software from: " + codecname);
                    CurrentHWDecode = HWDecode.SW;
                    pCodec = ffmpeg.avcodec_find_decoder_by_name(codecname);
                }
            }
            else {
                pCodec = ffmpeg.avcodec_find_decoder_by_name(codecname);
            }

            if (pCodec == null)
                throw new ApplicationException(@"Unsupported codec");

            //create new deocder context
            AVCodecContext* pCodecContext = ffmpeg.avcodec_alloc_context3(pCodec);

            ffmpeg.avcodec_parameters_to_context(pCodecContext, pStream->codecpar);

            //read size and fps from input stream
            int width = pStream->codec->width;
            int height = pStream->codec->height;

            SwsContext* pConvertContext = null;
            if (CurrentHWDecode != HWDecode.D3D11) {
                pConvertContext = ffmpeg.sws_getContext(width, height, pCodecContext->pix_fmt, width, height, AVPixelFormat.AV_PIX_FMT_BGRA, ffmpeg.SWS_FAST_BILINEAR, null, null, null);

                if (pConvertContext == null)
                    throw new ApplicationException(@"Could not initialize the conversion context");
            }

            // HWaccel Decoder Setup     
            if (!(CurrentHWDecode == HWDecode.SW)) {
                WriteLine("Initilizing Hwaccel: " + CurrentHWDecode.ToString());
                switch (CurrentHWDecode) {
                    case HWDecode.CUDA:
                        retErr = ffmpeg.av_hwdevice_ctx_create(&pCodecContext->hw_device_ctx, AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA, "auto", null, 0);
                        pCodecContext->get_format = new AVCodecContext_get_format(Get_Format_CUDA);
                        break;
                    case HWDecode.D3D11:
                        retErr = ffmpeg.av_hwdevice_ctx_create(&pCodecContext->hw_device_ctx, AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA, "auto", null, 0);
                        pCodecContext->get_format = new AVCodecContext_get_format(Get_Format_D3D11);
                        break;
                    case HWDecode.DXVA2:
                        retErr = ffmpeg.av_hwdevice_ctx_create(&pCodecContext->hw_device_ctx, AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2, "auto", null, 0);
                        pCodecContext->get_format = new AVCodecContext_get_format(Get_Format_DXVA);
                        break;
                }

                if (retErr < 0) {
                    WriteLine("Error creating a HWAccel device");
                    return;
                }
            }

            lock (FFLockObject) {
                if (ffmpeg.avcodec_open2(pCodecContext, pCodec, null) < 0)
                    throw new ApplicationException(@"Could not open codec");
            }

            //Create Packet Object for reading from file       
            AVPacket packet = new AVPacket();
            AVPacket* pPacket = &packet;

            //Init Packet Memory
            ffmpeg.av_init_packet(pPacket);

            //Create Frames for decoder
            AVFrame* pDecodedFrame = null;    // frame from decoder
            AVFrame* pHWTransferFrame = null; //frame to transfer from hwaccel
            AVFrame* pConvertedFrame = null;  //frame for output for software

            switch (CurrentHWDecode) {
                case HWDecode.CUDA:
                case HWDecode.DXVA2:
                case HWDecode.SW:
                    pDecodedFrame = ffmpeg.av_frame_alloc();
                    pHWTransferFrame = ffmpeg.av_frame_alloc();
                    pConvertedFrame = ffmpeg.av_frame_alloc();
                    break;
                case HWDecode.D3D11:
                    pDecodedFrame = ffmpeg.av_frame_alloc();
                    break;
            }

            //Sizes for SW Decode Bitmap
            int convertedFrameBufferSize=0;
            byte[] convertedFrameBufferArray = null;
            var dstData = new byte_ptrArray4();
            var dstLinesize = new int_array4();

            if (CurrentHWDecode == HWDecode.SW) {
                convertedFrameBufferSize = ffmpeg.av_image_get_buffer_size(AVPixelFormat.AV_PIX_FMT_RGBA, width, height, 1);
                convertedFrameBufferArray = new byte[convertedFrameBufferSize];
            }

            //D3D11 DXGI resource and surface index
            uint* resource = null;
            uint* resourceIndex = null;
            

            //Playback Loop Variables
            Stopwatch sw = new Stopwatch();
            int FrameRateLoopTime = Convert.ToInt32((float)1000 / (pStream->codec->framerate.num / pStream->codec->framerate.den));
            int frameNumber = 0;
            int readFrameResult = 0;
            bool doLoop = false;
            bool emptyPacket;

            fixed (byte* convertedFrameBuffer = convertedFrameBufferArray) {
                if (CurrentHWDecode == HWDecode.SW) {
                    retErr = ffmpeg.av_image_fill_arrays(ref dstData, ref dstLinesize, convertedFrameBuffer, AVPixelFormat.AV_PIX_FMT_RGBA, width, height, 1);
                }

                while ((readFrameResult != EndOfFileErrorCode || doLoop) && MediaStopping == false) {
                    if (MediaPause) {
                        Thread.Sleep(FrameRateLoopTime);
                    }
                    else {
                        try {
                            //Read packet from file
                            readFrameResult = ffmpeg.av_read_frame(pInputContext, pPacket);

                            if (readFrameResult == 0) {
                                doLoop = false;
                            }
                            else if (readFrameResult == EndOfFileErrorCode) {
                                doLoop = true;
                                frameNumber = 0;
                                //Rewind the clip
                                ffmpeg.av_seek_frame(pInputContext, -1, 0, ffmpeg.AVSEEK_FLAG_BACKWARD);
                            }
                            else if (readFrameResult < 0) {
                                break;
                            }

                            emptyPacket = readFrameResult == EndOfFileErrorCode;

                            if (pPacket->stream_index != pStream->index)
                                continue;

                            if (readFrameResult == SuccessCode) {
                                //submit packet to decoder
                                int sendResult = ffmpeg.avcodec_send_packet(pCodecContext, pPacket);
                                if (sendResult < 0) {
                                    break;
                                }
                                else {
                                    //read packet from decoder
                                    retErr = ffmpeg.avcodec_receive_frame(pCodecContext, pDecodedFrame);

                                    if (retErr == 0) {
                                        //got a decoded frame
                                        switch (CurrentHWDecode) {
                                            case HWDecode.CUDA:
                                            case HWDecode.DXVA2:
                                                //copy from GPU to CPU
                                                retErr = ffmpeg.av_hwframe_transfer_data(pHWTransferFrame, pDecodedFrame, 0);
                                                if (retErr == 0) {
                                                    //Convert from NV12 to RGBA
                                                    ffmpeg.sws_scale(pConvertContext, pHWTransferFrame->data, pHWTransferFrame->linesize, 0, height, dstData, dstLinesize);
                                                }
                                                break;
                                            case HWDecode.D3D11:
                                                //get handle to Texture2D and texture index
                                                resource = (uint*)pDecodedFrame->data[0];
                                                resourceIndex = (uint*)pDecodedFrame->data[1];
                                                break;
                                            case HWDecode.SW:
                                                //Convert from NV12 to RGBA
                                                ffmpeg.sws_scale(pConvertContext, pDecodedFrame->data, pDecodedFrame->linesize, 0, height, dstData, dstLinesize);
                                                break;
                                        }
                                    }
                                    else if (retErr == -11 || retErr == EndOfFileErrorCode) {
                                        //skip frame.
                                    }
                                    else if (retErr < 0) {
                                        string msg = GetFFmpegErrorMessage(retErr);
                                        throw new ApplicationException($@"Error while receiving frame {frameNumber}\n Message: {msg}");
                                    }
                                }
                            }
                        }
                        finally {
                            ffmpeg.av_packet_unref(pPacket);
                            ffmpeg.av_frame_unref(pDecodedFrame);
                            ffmpeg.av_frame_unref(pHWTransferFrame);
                        }

                        //wait for syncronisation from GenLock                     
                        GenLock.GenLockEvent.WaitOne();

                        //Update Image in WPF
                        if (CurrentHWDecode == HWDecode.D3D11)
                            UpdateImage((IntPtr)resource, (uint)resourceIndex);
                        else
                            UpdateImage(width, height, (IntPtr)convertedFrameBuffer, convertedFrameBufferSize, dstLinesize[0]);
                    }
                }
            }

            convertedFrameBufferArray = null;

            //flush decoder
            packet.data = null;
            packet.size = 0;
            ffmpeg.avcodec_send_packet(pCodecContext, pPacket);
            ffmpeg.av_packet_unref(pPacket);

            //free frames
            ffmpeg.av_frame_free(&pDecodedFrame);
            ffmpeg.av_frame_free(&pHWTransferFrame);
            ffmpeg.av_frame_free(&pConvertedFrame);

            ffmpeg.sws_freeContext(pConvertContext);

            //close input
            WriteLine("close input");
            ffmpeg.avformat_close_input(&pInputContext);

            WriteLine("close codec");
            ffmpeg.avcodec_close(pCodecContext);

            WriteLine("Free D3D");
            ShutdownD3D();

            WriteLine("Close HW Frames");
            ffmpeg.av_buffer_unref(&pCodecContext->hw_frames_ctx);

            WriteLine("free codec");
            ffmpeg.avcodec_free_context(&pCodecContext);

            MediaStopped = true;
            MediaStopping = true;
        }
    }
}
