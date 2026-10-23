namespace Yumi.Desktop
{
    // Win32 COLORREF uses 0x00BBGGRR. Avoid black/white keys: both occur on the avatar.
    public static class OverlayWindowPolicy
    {
        public const uint BackgroundKey=0x00FF00FF;
        public const uint ColorKeyFlag=1;
        public static int ExtendedStyle(int original,bool clickThrough)
        {
            // Transparency and hit testing are independent. Never remove LAYERED
            // when click-through changes, and never activate a desktop overlay.
            int style=original|0x80000|0x80|0x08000000;
            return clickThrough?style|0x20:style&~0x20;
        }
    }
}
