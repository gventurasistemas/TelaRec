using System.Runtime.InteropServices;
using Vortice.DXGI;
using Vortice.Direct3D11;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Capture;

namespace TelaRec.Infrastructure.Recording;

internal static class Direct3D11Interop
{
    private static readonly Guid CaptureItemInteropId = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateItemDelegate(IntPtr self, IntPtr handle, ref Guid iid, out IntPtr item);
    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string source, uint length, out IntPtr hstring);
    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsDeleteString(IntPtr hstring);
    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoGetActivationFactory(IntPtr className, ref Guid iid, out IntPtr factory);
    [ComImport, Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess { void GetInterface(ref Guid iid, out IntPtr device); }
    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    public static IDirect3DDevice CreateWinRtDevice(IDXGIDevice dxgiDevice)
    {
        Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var unknown));
        try { return WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(unknown); }
        finally { Marshal.Release(unknown); }
    }

    public static GraphicsCaptureItem CreateItemForMonitor(IntPtr monitor)
    {
        return CreateItem(monitor, 4);
    }
    public static GraphicsCaptureItem CreateItemForWindow(IntPtr window)
    {
        return CreateItem(window, 3);
    }
    private static GraphicsCaptureItem CreateItem(IntPtr handle, int slot)
    {
        const string className = "Windows.Graphics.Capture.GraphicsCaptureItem";
        Marshal.ThrowExceptionForHR(WindowsCreateString(className, (uint)className.Length, out var name));
        try
        {
            var factoryId = CaptureItemInteropId;
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(name, ref factoryId, out var factory));
            try
            {
                var method = Marshal.GetDelegateForFunctionPointer<CreateItemDelegate>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(factory), IntPtr.Size * slot));
                var iid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
                Marshal.ThrowExceptionForHR(method(factory, handle, ref iid, out var item));
                try { return WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(item); }
                finally { Marshal.Release(item); }
            }
            finally { Marshal.Release(factory); }
        }
        finally { WindowsDeleteString(name); }
    }
    public static ID3D11Texture2D GetTexture(IDirect3DSurface surface)
    {
        var unknown = WinRT.MarshalInterface<IDirect3DSurface>.FromManaged(surface);
        try
        {
            var access = (IDirect3DDxgiInterfaceAccess)Marshal.GetObjectForIUnknown(unknown);
            var iid = typeof(ID3D11Texture2D).GUID;
            access.GetInterface(ref iid, out var texture);
            return new ID3D11Texture2D(texture);
        }
        finally { Marshal.Release(unknown); }
    }
}
