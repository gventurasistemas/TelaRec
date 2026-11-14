using System.Runtime.InteropServices;
using TelaRec.Core.Recording;
using Vortice.MediaFoundation;

namespace TelaRec.Infrastructure.Recording;

/// <summary>
/// Consulta MFTs registrados pelo Windows. A enumeração é somente uma capacidade:
/// a criação do Sink Writer continua sendo a confirmação final e possui fallback.
/// </summary>
public sealed class MediaFoundationVideoEncoderCapabilitiesService : IVideoEncoderCapabilitiesService
{
    private static readonly Guid VideoEncoderCategory = new("F79EAC7D-E545-4387-BDEE-D647D7BDE42A");
    private static readonly Guid VideoMajorType = new("73646976-0000-0010-8000-00AA00389B71");
    private static readonly Guid Nv12 = new("3231564E-0000-0010-8000-00AA00389B71");
    private static readonly Guid H264 = new("34363248-0000-0010-8000-00AA00389B71");
    private static readonly Guid Hevc = new("43564548-0000-0010-8000-00AA00389B71");
    private const uint HardwareOnly = 0x00000004; // MFT_ENUM_FLAG_HARDWARE
    private const uint SoftwareTransforms = 0x00000003; // MFT_ENUM_FLAG_SYNCMFT | MFT_ENUM_FLAG_ASYNCMFT

    private readonly Lazy<VideoEncoderCapabilities> _capabilities = new(Probe);

    public VideoEncoderCapabilities GetCapabilities() => _capabilities.Value;

    private static VideoEncoderCapabilities Probe()
    {
        try
        {
            MediaFactory.MFStartup();
            try
            {
                var h264Hardware = HasEncoder(H264, HardwareOnly);
                var hevcHardware = HasEncoder(Hevc, HardwareOnly);
                var hevcSoftware = HasEncoder(Hevc, SoftwareTransforms);
                return new VideoEncoderCapabilities(h264Hardware, hevcHardware, hevcSoftware);
            }
            finally { MediaFactory.MFShutdown(); }
        }
        catch (DllNotFoundException) { return VideoEncoderCapabilities.SafeDefault; }
        catch (EntryPointNotFoundException) { return VideoEncoderCapabilities.SafeDefault; }
        catch (COMException) { return VideoEncoderCapabilities.SafeDefault; }
    }

    private static bool HasEncoder(Guid outputSubtype, uint flags)
    {
        var input = new MftRegisterTypeInfo(VideoMajorType, Nv12);
        var output = new MftRegisterTypeInfo(VideoMajorType, outputSubtype);
        var inputPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<MftRegisterTypeInfo>());
        var outputPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<MftRegisterTypeInfo>());
        try
        {
            Marshal.StructureToPtr(input, inputPointer, false);
            Marshal.StructureToPtr(output, outputPointer, false);
            var result = MFTEnumEx(VideoEncoderCategory, flags, inputPointer, outputPointer, out var activations, out var count);
            if (activations != IntPtr.Zero)
            {
                for (var index = 0; index < count; index++)
                {
                    var activation = Marshal.ReadIntPtr(activations, index * IntPtr.Size);
                    if (activation != IntPtr.Zero) Marshal.Release(activation);
                }
                Marshal.FreeCoTaskMem(activations);
            }
            return result >= 0 && count > 0;
        }
        finally
        {
            Marshal.FreeCoTaskMem(inputPointer);
            Marshal.FreeCoTaskMem(outputPointer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct MftRegisterTypeInfo(Guid majorType, Guid subtype)
    {
        public readonly Guid MajorType = majorType;
        public readonly Guid Subtype = subtype;
    }

    [DllImport("mfplat.dll")]
    private static extern int MFTEnumEx(Guid category, uint flags, IntPtr inputType, IntPtr outputType, out IntPtr activations, out uint count);
}
