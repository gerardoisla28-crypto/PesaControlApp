namespace PesaControl.Core.Models;

public class DvrChannelFrameEventArgs : EventArgs
{
    public int Channel { get; }
    public byte[] FrameData { get; }

    public DvrChannelFrameEventArgs(int channel, byte[] frameData)
    {
        Channel = channel;
        FrameData = frameData;
    }
}
