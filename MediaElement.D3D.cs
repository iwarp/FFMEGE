using System;
using System.Windows.Controls;
using System.Windows.Interop;
using SharpDX.DXGI;
using SharpDX.Direct3D11;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.D3DCompiler;
using System.IO;

namespace FFMEGE {
    public partial class MediaElement : UserControl {
        // Texture on FFmpeg Device
        private Texture2D TransferTexture;
        // Texture for Shader on D3D Device
        private Texture2D DisplayTexture;

        private SharpDX.Direct3D11.Device D3DDevice;
        private DeviceContext D3DContext;
        private RenderTargetView RenderTarget;
        private SwapChain SwapChain;

        private SharpDX.Direct3D11.Device FFmpegD3DDevice;
        private DeviceContext FFmpegD3DContext;

        private void SetupD3D() {

            SharpDX.Configuration.EnableObjectTracking = true;

            if (D3DDevice != null)
                return;

            var desc = new SwapChainDescription() {
                BufferCount = 1,
                ModeDescription =
                        new ModeDescription(Convert.ToInt32(host.ActualWidth), Convert.ToInt32(host.ActualHeight),
                                            new Rational(60, 1), Format.R8G8B8A8_UNorm),
                IsWindowed = true,
                OutputHandle = new WindowInteropHelper(HostWindow).Handle,
                SampleDescription = new SampleDescription(1, 0),
                SwapEffect = SwapEffect.Discard,
                Usage = Usage.RenderTargetOutput
            };

            SharpDX.Direct3D11.Device.CreateWithSwapChain(SharpDX.Direct3D.DriverType.Hardware, SharpDX.Direct3D11.DeviceCreationFlags.None, desc, out D3DDevice, out SwapChain);
            D3DContext = D3DDevice.ImmediateContext;

            //Load Shader from the Internal Resource
            string shader = "";
            var assembly = this.GetType().Assembly;
            using (var stream = assembly.GetManifestResourceStream(assembly.GetName().Name + "." + "ffmege.fx")) {
                shader = new StreamReader(stream).ReadToEnd();
            }

            // Compile Vertex and Pixel shaders
            var vertexShaderByteCode = ShaderBytecode.Compile(shader, "VS", "vs_4_0", ShaderFlags.None, EffectFlags.None);
            var vertexShader = new VertexShader(D3DDevice, vertexShaderByteCode);

            var pixelShaderByteCode = ShaderBytecode.Compile(shader, "PS", "ps_4_0", ShaderFlags.None, EffectFlags.None);
            var pixelShader = new PixelShader(D3DDevice, pixelShaderByteCode);

            var layout = new InputLayout(D3DDevice, ShaderSignature.GetInputSignature(vertexShaderByteCode), new[]
                {
                        new InputElement("POSITION", 0, Format.R32G32B32A32_Float, 0, 0),
                        new InputElement("TEXCOORD", 0, Format.R32G32_Float , 16, 0)
            });

            // Instantiate Vertex buiffer from vertex data
            var vertices = SharpDX.Direct3D11.Buffer.Create(D3DDevice, SharpDX.Direct3D11.BindFlags.VertexBuffer, new[]
                  {
                                      //  Position                  Colour                U V
                                      -1.0f, 1.0f, 0.5f, 1.0f,      0.0f, 0.0f,        //top left
                                      1.0f, -1.0f, 0.5f, 1.0f,      1.0f, 1.0f,        //bottom right
                                      -1.0f, -1.0f, 0.5f, 1.0f,     0.0f, 1.0f,        //bottom left

                                      -1.0f, 1.0f, 0.5f, 1.0f,      0.0f, 0.0f,        //top left
                                      1.0f, 1.0f, 0.5f, 1.0f,       1.0f, 0.0f,        //top right
                                      1.0f, -1.0f, 0.5f, 1.0f,      1.0f, 1.0f,        //bottom right
                });
            var vertexBufferBinding = new VertexBufferBinding(vertices, sizeof(float) * 6, 0);

            var sampler = new SamplerState(D3DDevice, new SamplerStateDescription() {
                Filter = Filter.MinMagMipLinear,
                AddressU = TextureAddressMode.Wrap,
                AddressV = TextureAddressMode.Wrap,
                AddressW = TextureAddressMode.Wrap,
                BorderColor = SharpDX.Color.Black,
                ComparisonFunction = Comparison.Never,
                MaximumAnisotropy = 1,
                MipLodBias = 0,
                MinimumLod = -float.MaxValue,
                MaximumLod = float.MaxValue
            });

            // Prepare All the stages
            D3DContext.InputAssembler.SetVertexBuffers(0, vertexBufferBinding);
            D3DContext.InputAssembler.InputLayout = layout;
            D3DContext.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;

            D3DContext.VertexShader.Set(vertexShader);
            D3DContext.PixelShader.SetSampler(0, sampler);
            D3DContext.PixelShader.Set(pixelShader);

            //Clean up temporary resources
            vertexShader.Dispose();
            pixelShader.Dispose();

            layout.Dispose();
            vertexBufferBinding.Buffer.Dispose();
            vertices.Dispose();
            sampler.Dispose();
        }

        private void ShutdownD3D() {
            ClearD3D();

            if (RenderTarget != null) {
                RenderTarget.Dispose();
                RenderTarget = null;
            }

            if (SwapChain != null) {
                SwapChain.Dispose();
                SwapChain = null;
            }

            //Cleanup FFmpeg Device
            if (FFmpegD3DContext != null) {
                FFmpegD3DContext.Dispose();
                FFmpegD3DContext = null;
            }

            if (FFmpegD3DDevice != null) {
                FFmpegD3DDevice.Dispose();
                FFmpegD3DDevice = null;
            }

            //Cleanup D3D Device
            if (D3DContext != null) {
                D3DContext.Dispose();
                D3DContext = null;
            }

            if (D3DDevice != null) {
                D3DDevice.Dispose();
                D3DDevice = null;
            }

            Console.WriteLine("#############################################");
            Console.WriteLine(SharpDX.Diagnostics.ObjectTracker.ReportActiveObjects());

        }

        private void ClearD3D() {
            if (TransferTexture != null) {
                //Get D3D Device from Texture and Flush to free memory
                using (var device = TransferTexture.Device.ImmediateContext) {
                    TransferTexture.Dispose();
                    device.Flush();
                }

                TransferTexture = null;
            }

            //Remove the Shaders to release the objects
            if (D3DContext != null) {
                D3DContext.PixelShader.SetShaderResource(0, null);
                D3DContext.PixelShader.SetShaderResource(1, null);

                D3DContext.Flush();
            }

            //Order is important for DX must be in reverse order of creation.
            if (DisplayTexture != null) {
                DisplayTexture.Dispose();
                DisplayTexture = null;
            }

            RequestD3DRender();
        }

        private void PrepareShaders() {
            if (D3DDevice == null || DisplayTexture == null) {
                return;
            }

             //Create new Shader with R8 which contains the Y component of the picture
            ShaderResourceViewDescription srv = new ShaderResourceViewDescription {
                Format = Format.R8_UNorm,
                Dimension = ShaderResourceViewDimension.Texture2D
            };
            srv.Texture2D.MipLevels = 1;

            //Set Shaders onto the pipeline
            using (var textureView1 = new ShaderResourceView(D3DDevice, DisplayTexture, srv)) {
                D3DContext.PixelShader.SetShaderResource(0, textureView1);
            }

            //Create new Shader with R8G8 which contains the UV component of the picture
            srv.Format = Format.R8G8_UNorm;

            //Set Shaders onto the pipeline
            using (var textureView2 = new ShaderResourceView(D3DDevice, DisplayTexture, srv)) {
                D3DContext.PixelShader.SetShaderResource(1, textureView2);
            }
        }

        private void InitRenderTarget(IntPtr surface) {
            IntPtr sharedHandle;

            var c = new SharpDX.ComObject(surface);            
            var D3DImageResource = c.QueryInterface<SharpDX.DXGI.Resource>();


            sharedHandle = D3DImageResource.SharedHandle;
            
            var tempResource = D3DDevice.OpenSharedResource<SharpDX.Direct3D11.Resource>(sharedHandle);
            var d3DImageTexture = tempResource.QueryInterface<Texture2D>();

            RenderTarget = new RenderTargetView(D3DDevice, d3DImageTexture);

            var vp = new ViewportF {
                Width = (float)host.ActualWidth,
                Height = (float)host.ActualHeight,
                MinDepth = 0.0f,
                MaxDepth = 1.0f
            };

            D3DContext.Rasterizer.SetViewport(vp);
            D3DContext.OutputMerger.SetRenderTargets(RenderTarget);

            ////Cleanup
            tempResource.Dispose();
            d3DImageTexture.Dispose();
        }

        /// <summary>
        /// Update D3DImage for WPF
        /// </summary>
        /// <param name="surface">Pointer to Surface from FFMpeg</param>
        /// <param name="surfaceIndex">Index to subsufaace from the current update</param>
        private void UpdateImage(IntPtr surface, uint surfaceIndex) {            
            if (Shutdown == false) {
                if (surface != IntPtr.Zero) {
                    //Access ffmpeg d3d device
                    using (var ffmpegSrc = CppObject.FromPointer<Texture2D>(surface)) {

                        if (FFmpegD3DDevice == null) {
                            FFmpegD3DDevice = ffmpegSrc.Device;
                            FFmpegD3DContext = FFmpegD3DDevice.ImmediateContext;
                        }

                        //Need to Create a Transfer Texture on the ffmpeg device to allow shared access from wpf d3ddevice
                        //Only Create Once
                        if (TransferTexture == null) {
                            var tempDesc = ffmpegSrc.Description;
                            tempDesc.ArraySize = 1;
                            tempDesc.BindFlags |= BindFlags.ShaderResource;
                            tempDesc.OptionFlags = ResourceOptionFlags.Shared;
                            tempDesc.Usage = ResourceUsage.Default;

                            TransferTexture = new Texture2D(FFmpegD3DDevice, tempDesc);
                        }

                        ////Copy the ffmpegTexture to Transfer Texture, Also select the correct sub texture index to a single texture for transfer
                        FFmpegD3DContext.CopySubresourceRegion(ffmpegSrc, Convert.ToInt32(surfaceIndex), null, TransferTexture, 0);
                    }

                    //Hack to create D3D Device so we dont miss the first frame
                    if (D3DDevice == null)
                        RequestD3DRender();

                    if (D3DDevice != null && TransferTexture != null && DisplayTexture == null) {
                        //Convert transfer texture thru to DXGI resource Object 
                        using (var resource = TransferTexture.QueryInterface<SharpDX.DXGI.Resource>()) {
                            //Get shared handle from DXGI resource
                            using (var sharedResource = D3DDevice.OpenSharedResource<SharpDX.DXGI.Resource>(resource.SharedHandle)) {
                                //Convert back to Texture2D Object
                                using (var sharedTexture = sharedResource.QueryInterface<SharpDX.Direct3D11.Texture2D>()) {
                                    DisplayTexture = sharedResource.QueryInterface<SharpDX.Direct3D11.Texture2D>();
                                }
                            }
                        }
                    }

                    //Request a render evey time we have a new frame from FFMpeg           
                    RequestD3DRender();
                }
            }
        }

        private void RequestD3DRender() {
            if (Shutdown == false) {
                try {
                    ViewBox.Dispatcher.Invoke(() => {
                        d3dImage.RequestRender();
                    });
                }
                catch { }
            }
        }

        private void DoRender(IntPtr surface, bool IsNewSurface) {

            //Create D3D device - Could be moved to seperate location but not sure which thread this should live on.
            if (D3DDevice == null) {
                SetupD3D();
            }

            //Dont continue if there is no D3DDevice
            if (D3DDevice == null)
                return;

            if (IsNewSurface || RenderTarget == null) {
                //If surface is new or resized create new RenderTarget
                //ClearD3D();
                InitRenderTarget(surface);
            }

            if (RenderTarget == null)
                return;

            d3dImage.Lock();

            //Clear RTV output to black
            D3DContext.ClearRenderTargetView(RenderTarget, new SharpDX.Color4(0, 0, 0, 0));

            //Dont Draw if there is no Texture
            if (DisplayTexture != null) {

                //Create Shaders if required can only be done once the Surface has be initalized.
                PrepareShaders();

                //Draw 6 Vertexes 
                D3DContext.Draw(6, 0);
            }

            //Flush Context and do Drawing
            D3DContext.Flush();

            d3dImage.Unlock();
        }
    }
}
