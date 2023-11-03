using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Veldrid;

namespace googletiles
{
    // This extends from the "Win32HwndControl" from the SharpDX example code.
    public class VeldridComponent : Win32HwndControl
    {
        private Swapchain _sc;
        private CommandList _cl;
        private GraphicsDevice _gd;
        public EarthViz earthViz = null;
        bool earthVizInitialized = false;
        //TexturedCube tc = new TexturedCube();
        public bool Rendering { get; private set; }

        public static GraphicsDevice Graphics;
        protected override sealed void Initialize()
        {
            Graphics = _gd = GraphicsDevice.CreateD3D11(new GraphicsDeviceOptions());
            _cl = _gd.ResourceFactory.CreateCommandList();
            CreateSwapchain();

            Rendering = true;
            CompositionTarget.Rendering += OnCompositionTargetRendering;
        }

        protected override sealed void Uninitialize()
        {
            Rendering = false;
            CompositionTarget.Rendering -= OnCompositionTargetRendering;

            DestroySwapchain();
        }

        protected sealed override void Resized()
        {
            ResizeSwapchain();
        }

        private void OnCompositionTargetRendering(object sender, EventArgs eventArgs)
        {
            if (!Rendering)
                return;

            Render();
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            Mouse.Capture(this);
            earthViz.OnMouseDown(e, e.GetPosition(this));
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            earthViz.OnMouseMove(e, e.GetPosition(this));
            base.OnMouseMove(e);
        }
        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            earthViz.OnMouseUp(e, e.GetPosition(this));
            Mouse.Capture(null);
            base.OnMouseUp(e);
        }
        private double GetDpiScale()
        {
            PresentationSource source = PresentationSource.FromVisual(this);

            return source.CompositionTarget.TransformToDevice.M11;
        }
        
        protected virtual void CreateSwapchain()
        {
            double dpiScale = GetDpiScale();
            uint width = (uint)(ActualWidth < 0 ? 0 : Math.Ceiling(ActualWidth * dpiScale));
            uint height = (uint)(ActualHeight < 0 ? 0 : Math.Ceiling(ActualHeight * dpiScale));

            Module mainModule = typeof(VeldridComponent).Module;
            IntPtr hinstance = Marshal.GetHINSTANCE(mainModule);
            SwapchainSource win32Source = SwapchainSource.CreateWin32(Hwnd, hinstance);
            SwapchainDescription scDesc = new SwapchainDescription(win32Source, width, height, Veldrid.PixelFormat.R32_Float, true);

            _sc = _gd.ResourceFactory.CreateSwapchain(scDesc);
        }

        protected virtual void DestroySwapchain()
        {
            _sc.Dispose();
        }

        private void ResizeSwapchain()
        {
            double dpiScale = GetDpiScale();
            uint width = (uint)(ActualWidth < 0 ? 0 : Math.Ceiling(ActualWidth * dpiScale));
            uint height = (uint)(ActualHeight < 0 ? 0 : Math.Ceiling(ActualHeight * dpiScale));
            _sc.Resize(width, height);
        }

        protected virtual void Render()
        {
            if (earthViz != null && !earthVizInitialized)
            {
                earthViz.CreateResources(_gd, _sc, _gd.ResourceFactory);
                earthVizInitialized = true;
            }
            _cl.Begin();
            _cl.SetFramebuffer(_sc.Framebuffer);
            Random r = new Random();
            _cl.ClearColorTarget(
                0,
                new RgbaFloat(0, 0, 0, 1));
            _cl.ClearDepthStencil(1);

            if (earthViz != null)
                earthViz.Draw(_cl, 0.016f);

            _cl.End();
            _gd.SubmitCommands(_cl);
            _gd.SwapBuffers(_sc);
        }
    }
}