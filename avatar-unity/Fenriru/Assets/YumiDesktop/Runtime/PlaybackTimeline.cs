using System;
using System.Collections.Generic;

namespace Yumi.Desktop
{
    // Bounded DAC-clock scheduler shared by facial animation and expressive delivery.
    public sealed class PlaybackFrame
    {
        public long Epoch, Due;
        public float Aa,Ih,Ou;
        public bool Speaking;
        public string Style="neutral";
    }
    public sealed class PlaybackTimeline
    {
        readonly Queue<PlaybackFrame> frames=new Queue<PlaybackFrame>();
        long epoch=long.MinValue,lastReceived;
        PlaybackFrame current=new PlaybackFrame();
        public void Clear() { frames.Clear();current=new PlaybackFrame();epoch=long.MinValue;lastReceived=0; }
        public void Push(PlaybackFrame value,long now)
        {
            if(value.Epoch<epoch)return;
            if(value.Epoch!=epoch) {frames.Clear();current=new PlaybackFrame();epoch=value.Epoch;}
            lastReceived=now;
            if(!value.Speaking) {frames.Clear();current=new PlaybackFrame {Epoch=epoch};return;}
            value.Due=Math.Max(now-250,Math.Min(now+1000,value.Due));
            value.Aa=Clamp(value.Aa);value.Ih=Clamp(value.Ih);value.Ou=Clamp(value.Ou);
            while(frames.Count>=24)frames.Dequeue();
            frames.Enqueue(value);
        }
        public PlaybackFrame Sample(long now)
        {
            while(frames.Count>0&&frames.Peek().Due<=now)current=frames.Dequeue();
            if(now-lastReceived>300||now-current.Due>300)return new PlaybackFrame {Epoch=epoch};
            return current;
        }
        static float Clamp(float value) { return float.IsNaN(value)||float.IsInfinity(value)?0:Math.Max(0,Math.Min(1,value)); }
    }
}
