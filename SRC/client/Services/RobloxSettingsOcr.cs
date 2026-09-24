using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace RobloxChatLauncher.Services;

internal readonly record struct RobloxSettingsText(string Text, Rectangle Bounds);

internal static class RobloxSettingsOcr
{
    private static readonly System.Collections.Concurrent.ConcurrentBag<OcrEngine> Engines=new();
    internal static Task<IReadOnlyList<RobloxSettingsText>> ReadAsync(Bitmap image) =>
        ReadRegionAsync(image,new Rectangle(Point.Empty,image.Size),1);

    // Separate enlarged columns keep native OCR from merging labels and values.
    internal static async Task<IReadOnlyList<RobloxSettingsText>> ReadSettingsAsync(Bitmap image)
    {
        int width=Math.Min(1800,image.Width),left=(image.Width-width)/2;
        int top=Math.Max(0,(image.Height-1800)/2),height=Math.Min(1800,image.Height);
        int split=image.Width/2-80;
        var columns=await Task.WhenAll(ReadRegionAsync(image,Rectangle.FromLTRB(left,top,split,top+height),2),
            ReadRegionAsync(image,Rectangle.FromLTRB(split,top,left+width,top+height),2));
        return columns[0].Concat(columns[1]).OrderBy(line=>line.Bounds.Top).ThenBy(line=>line.Bounds.Left).ToArray();
    }

    internal static async Task<IReadOnlyList<RobloxSettingsText>> ReadRowAsync(Bitmap image,Rectangle region)
    {
        int split=image.Width/2-80;
        var columns=await Task.WhenAll(
            ReadRegionAsync(image,Rectangle.FromLTRB(region.Left,region.Top,split,region.Bottom),2),
            ReadRegionAsync(image,Rectangle.FromLTRB(split,region.Top,region.Right,region.Bottom),2));
        return columns[0].Concat(columns[1]).ToArray();
    }
    internal static async Task<IReadOnlyList<RobloxSettingsText>> ReadRegionAsync(Bitmap image,Rectangle region,double requestedScale)
    {
        var engine=Engines.TryTake(out var cached)?cached:OcrEngine.TryCreateFromLanguage(new Language("en-US"))??OcrEngine.TryCreateFromUserProfileLanguages();
        if(engine==null)throw new InvalidOperationException("Windows text recognition is unavailable. Install an OCR language in Windows Language settings.");
        try
        {
        double scale=Math.Min(requestedScale,(double)OcrEngine.MaxImageDimension/Math.Max(region.Width,region.Height));
        using var converted=new Bitmap(Math.Max(1,(int)Math.Round(region.Width*scale)),Math.Max(1,(int)Math.Round(region.Height*scale)),PixelFormat.Format32bppArgb);
        using(var graphics=Graphics.FromImage(converted))
        {
            graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(image,new Rectangle(Point.Empty,converted.Size),region,GraphicsUnit.Pixel);
        }
        var bits=converted.LockBits(new Rectangle(Point.Empty,converted.Size),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
        byte[] pixels=new byte[bits.Stride*converted.Height];
        try{Marshal.Copy(bits.Scan0,pixels,0,pixels.Length);}
        finally{converted.UnlockBits(bits);}
        using var software=SoftwareBitmap.CreateCopyFromBuffer(pixels.AsBuffer(),BitmapPixelFormat.Bgra8,converted.Width,converted.Height,BitmapAlphaMode.Premultiplied);
        var result=await engine.RecognizeAsync(software);
        return result.Lines.Where(line=>line.Words.Count>0).Select(line=>new RobloxSettingsText(line.Text,
            Rectangle.FromLTRB(region.X+(int)Math.Floor(line.Words.Min(word=>word.BoundingRect.X)/scale),
                region.Y+(int)Math.Floor(line.Words.Min(word=>word.BoundingRect.Y)/scale),
                region.X+(int)Math.Ceiling(line.Words.Max(word=>word.BoundingRect.X+word.BoundingRect.Width)/scale),
                region.Y+(int)Math.Ceiling(line.Words.Max(word=>word.BoundingRect.Y+word.BoundingRect.Height)/scale)))).ToArray();
        }
        finally { Engines.Add(engine); }
    }
}
