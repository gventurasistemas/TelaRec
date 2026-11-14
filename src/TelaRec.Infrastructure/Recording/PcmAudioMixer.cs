using TelaRec.Core.Recording;
namespace TelaRec.Infrastructure.Recording;
public static class PcmAudioMixer
{
    public static byte[] MixPcm16(byte[] system, byte[] microphone, float systemGain, float microphoneGain)
    {
        var length=Math.Max(system.Length,microphone.Length); var output=new byte[length];
        for(var i=0;i+1<length;i+=2){ var a=i+1<system.Length?BitConverter.ToInt16(system,i)*systemGain:0; var b=i+1<microphone.Length?BitConverter.ToInt16(microphone,i)*microphoneGain:0; var value=(short)Math.Clamp(a+b,short.MinValue,short.MaxValue); BitConverter.TryWriteBytes(output.AsSpan(i,2),value); }
        return output;
    }
}
