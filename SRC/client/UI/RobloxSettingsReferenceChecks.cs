using System.Drawing;
using System.Drawing.Drawing2D;
using RobloxChatLauncher.Services;

namespace RobloxChatLauncher.UI;

internal static class RobloxSettingsReferenceChecks
{
    internal static async Task CheckFrameRatePopupAsync(string path)
    {
        using var image=new Bitmap(path);
        var lines=await RobloxSettingsOcr.ReadSettingsAsync(image);
        var options=RobloxSettingsAutomation.ReadFrameRatePopup(image,lines);
        string[] expected={"Default (60 FPS)","60 FPS","120 FPS","144 FPS","160 FPS","165 FPS","180 FPS","200 FPS","240 FPS"};
        if(!options.Select(option=>option.Text).SequenceEqual(expected))
            throw new InvalidOperationException("Frame-rate options: "+string.Join(", ",options.Select(option=>option.Text)));
        if(options.Count(option=>option.Selected)!=1||!options[^1].Selected)
            throw new InvalidOperationException("Frame-rate popup must recognize 240 FPS as selected.");
        Console.WriteLine("Frame-rate popup: all nine options and selected 240 FPS verified.");
    }
    internal static async Task RunAsync(string directory)
    {
        using(var top=new Bitmap(Path.Combine(directory,"Img1-1.png")))
        using(var middle=new Bitmap(Path.Combine(directory,"Im1-2.png")))
        using(var bottom=new Bitmap(Path.Combine(directory,"Image1-3.png")))
        using(var changed=(Bitmap)top.Clone())
        {
            var mask=RobloxSettingsAutomation.LabelMask(top);
            if(mask.AsSpan().SequenceEqual(RobloxSettingsAutomation.LabelMask(middle))||
               mask.AsSpan().SequenceEqual(RobloxSettingsAutomation.LabelMask(bottom)))
                throw new InvalidOperationException("Scrolling must invalidate cached label geometry.");
            // Simulate the dark keyboard-focus rectangle behind a row without
            // changing its bright glyphs. This must not force another OCR pass.
            for(int y=151;y<196;y++)for(int x=572;x<875;x++)
            {
                var pixel=changed.GetPixel(x,y);
                if(Math.Min(pixel.R,Math.Min(pixel.G,pixel.B))<150)changed.SetPixel(x,y,Color.FromArgb(34,34,34));
            }
            if(!mask.AsSpan().SequenceEqual(RobloxSettingsAutomation.LabelMask(changed)))
                throw new InvalidOperationException("Highlight changes should reuse label recognition.");
            changed.SetPixel(600,170,Color.White);
            changed.SetPixel(600,171,Color.White);
            changed.SetPixel(600,172,Color.White);
            if(mask.AsSpan().SequenceEqual(RobloxSettingsAutomation.LabelMask(changed)))
                throw new InvalidOperationException("Changed label pixels must invalidate recognition.");
            Console.WriteLine("Navigation cache: highlight reuse and scroll/text invalidation verified.");
        }
        var correction=RobloxSettingsAutomation.PlanSettingMove(0,3,new[]{0,1,2,3},System.Windows.Forms.Keys.Down);
        if(correction.Key!=System.Windows.Forms.Keys.Up||
           RobloxSettingsAutomation.LimitSettingMove(correction.Key,correction.Count,30,0)!=correction.Count||
           RobloxSettingsAutomation.LimitSettingMove(System.Windows.Forms.Keys.Down,5,30,0)!=0||
           RobloxSettingsAutomation.LimitSettingMove(System.Windows.Forms.Keys.Up,5,30,29)!=1||
           RobloxSettingsAutomation.LimitSettingMove(System.Windows.Forms.Keys.Up,5,30,30)!=0)
            throw new InvalidOperationException("The 30th Down must allow OCR-directed Up correction while keeping both directions bounded.");
        Console.WriteLine("Scan boundary: correction still allowed after 30 Down presses; both direction limits verified.");
        var up=RobloxSettingsAutomation.PlanSettingMove(0,8,new[]{0,1,2,3,4,5,6,7,8},null);
        var down=RobloxSettingsAutomation.PlanSettingMove(12,2,new[]{0,1,2,3,4},null);
        var near=RobloxSettingsAutomation.PlanSettingMove(0,1,new[]{0,1,2},null);
        var initialize=RobloxSettingsAutomation.PlanSettingMove(0,-1,new[]{0,1,2},null);
        var unknownBelow=RobloxSettingsAutomation.PlanSettingMove(0,-1,new[]{5,6,7},System.Windows.Forms.Keys.Up);
        var targetAboveButOffscreen=RobloxSettingsAutomation.PlanSettingMove(0,8,new[]{5,6,7,8},System.Windows.Forms.Keys.Up);
        if(up!=(System.Windows.Forms.Keys.Up,1)||down!=(System.Windows.Forms.Keys.Down,5)||
           near!=(System.Windows.Forms.Keys.Up,1)||initialize!=(System.Windows.Forms.Keys.Down,1)||
           unknownBelow!=(System.Windows.Forms.Keys.Down,1)||
           targetAboveButOffscreen!=(System.Windows.Forms.Keys.Down,5))
            throw new InvalidOperationException("Fast scan direction, close approach or unselected-menu initialization failed.");
        Console.WriteLine("Target scan: five-key bursts toward target, single-key approach and initial Down verified.");
        // Exercise every supported setting against every possible selected row.
        // A visible target must never be approached in a multi-key burst.
        foreach(var setting in RobloxSettingsAutomation.Settings)
        {
            int target=Array.IndexOf(RobloxSettingsAutomation.NativeLabels,setting.Name);
            for(int selected=0;selected<RobloxSettingsAutomation.NativeLabels.Length;selected++)
            {
                var plan=RobloxSettingsAutomation.PlanSettingMove(target,selected,new[]{target,selected},null);
                int expectedCount=target==selected?0:1;
                var expectedKey=target<selected?System.Windows.Forms.Keys.Up:System.Windows.Forms.Keys.Down;
                if(plan.Count!=expectedCount||plan.Key!=expectedKey)
                    throw new InvalidOperationException("Incorrect single-row approach for "+setting.Name);
            }
        }
        Console.WriteLine("All settings: one-row corrections and no movement when already selected verified.");
        var menuKeys=new List<System.Windows.Forms.Keys>();
        await RobloxSettingsAutomation.EnterSettingsAsync(
            key=>{menuKeys.Add(key);return Task.CompletedTask;},()=>Task.FromResult(true));
        if(!menuKeys.SequenceEqual(new[]{System.Windows.Forms.Keys.Escape}))
            throw new InvalidOperationException("Remembered Settings must not receive Tab.");
        menuKeys.Clear();int reads=0;
        await RobloxSettingsAutomation.EnterSettingsAsync(
            key=>{menuKeys.Add(key);return Task.CompletedTask;},()=>Task.FromResult(++reads>=4));
        if(!menuKeys.SequenceEqual(new[]{System.Windows.Forms.Keys.Escape,System.Windows.Forms.Keys.Tab}))
            throw new InvalidOperationException("Delayed recognition must not cycle tabs.");
        Console.WriteLine("Menu entry: remembered Settings preserved; delayed recognition sends Tab only once.");
        var settingKeys=new List<System.Windows.Forms.Keys>();int selectionReads=0;
        await RobloxSettingsAutomation.FindDownAsync(
            key=>{settingKeys.Add(key);return Task.CompletedTask;},()=>Task.FromResult(++selectionReads==18));
        if(settingKeys.Count!=17||settingKeys.Any(key=>key!=System.Windows.Forms.Keys.Down))
            throw new InvalidOperationException("Setting selection must only move down until the target is confirmed.");
        settingKeys.Clear();
        await RobloxSettingsAutomation.FindDownAsync(
            key=>{settingKeys.Add(key);return Task.CompletedTask;},()=>Task.FromResult(false));
        if(settingKeys.Count!=30||settingKeys.Any(key=>key!=System.Windows.Forms.Keys.Down))
            throw new InvalidOperationException("Missing target must stop after 30 Down keys without reversing.");
        Console.WriteLine("Setting selection: Down-only search and bounded missing-target behavior verified.");
        var scanEvents=new List<string>();
        await RobloxSettingsAutomation.ScanDownAsync(
            key=>{scanEvents.Add(key.ToString());return Task.CompletedTask;},
            ()=>{scanEvents.Add("Read");return Task.CompletedTask;});
        var expectedScan=new List<string>{"Read"};
        for(int i=1;i<=30;i++){expectedScan.Add("Down");if(i%5==0)expectedScan.Add("Read");}
        if(!scanEvents.SequenceEqual(expectedScan))
            throw new InvalidOperationException("Initial scan must read first, send exactly 30 Down keys, and never send Up.");
        Console.WriteLine("Initial scan: 30 Down keys, zero Up keys, seven reads verified.");
        await CheckAsync(Path.Combine(directory,"Img1-1.png"),new Dictionary<string,string>
        {
            ["Shift Lock Switch"]="Off",["Camera Mode"]="Default (Classic)",["Movement Mode"]="Default (Keyboard)",
            ["Maximum Frame Rate"]="Default (60 FPS)",["Automatic Translations"]="Off",["Game Language"]="Unavailable",
            ["Automatic Chat Translation"]="On",["Chat Translation Language"]="English",["Option to View Untranslated Message"]="Off"
        },"Option to View Untranslated Message");
        await CheckAsync(Path.Combine(directory,"Image1-3.png"),new Dictionary<string,string>
        {
            ["Background transparency"]="0",["Text size"]="0",["UI navigation toggle"]="On",
            ["Performance Stats"]="Off",["MicroProfiler"]="Off",["Camera Inverted"]="Off",
            ["Developer Console"]="Open",["People's Names"]="Show",["My Badges"]="Show"
        },"My Badges");
        string sliders=Path.Combine(directory,"Im1-2.png");
        foreach(double scale in new[]{.75,1.0,1.5})
        {
            using var original=new Bitmap(sliders);
            using var image=new Bitmap((int)Math.Round(original.Width*scale),(int)Math.Round(original.Height*scale));
            using(var graphics=Graphics.FromImage(image))
            {
                graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(original,new Rectangle(Point.Empty,image.Size));
            }
            var lines=await RobloxSettingsOcr.ReadSettingsAsync(image);
            foreach(var (name,value) in new[]{("Camera Sensitivity","5"),("Volume","10"),("Fullscreen","Off"),("Graphics Mode","Automatic")})
            {
                var row=RequireValue(image,lines,name,value);
                await CheckFastRowAsync(image,row,value);
            }
            var selected=RobloxSettingsAutomation.ReadRow(image,lines,"Camera Sensitivity")!;
            if(!RobloxSettingsAutomation.IsRowSelected(image,selected))
                throw new InvalidOperationException($"Camera Sensitivity highlight was not found at {scale:P0}.");
            Console.WriteLine($"Slider reference at {scale:P0}: sensitivity 5/10, volume 10/10, selected row verified.");
        }
    }
    private static async Task CheckAsync(string path,Dictionary<string,string> expected,string selected)
    {
        using var image=new Bitmap(path);
        var lines=await RobloxSettingsOcr.ReadSettingsAsync(image);
        foreach(var pair in expected)
        {
            var row=RequireValue(image,lines,pair.Key,pair.Value);
            if(RobloxSettingsAutomation.Settings.Any(setting=>setting.Name==pair.Key&&!setting.Dropdown&&setting.Name!="Developer Console"))await CheckFastRowAsync(image,row,pair.Value);
            if(RobloxSettingsAutomation.IsRowSelected(image,row)!=(pair.Key==selected))
                throw new InvalidOperationException("Incorrect row focus: "+pair.Key);
        }
        Console.WriteLine($"{Path.GetFileName(path)}: {expected.Count} setting values and selected row verified.");
    }
    private static async Task CheckFastRowAsync(Bitmap image,NativeSettingRow row,string expected)
    {
        double scale=Math.Clamp((image.Width/2.0-row.Label.Left)/378.0,.65,2.5);
        var region=Rectangle.FromLTRB(Math.Max(0,row.Label.Left-25),Math.Max(0,row.Label.Top-(int)(18*scale)),
            Math.Min(image.Width,image.Width/2+(int)(410*scale)),Math.Min(image.Height,row.Label.Bottom+(int)(18*scale)));
        var timer=System.Diagnostics.Stopwatch.StartNew();
        var lines=await RobloxSettingsOcr.ReadRowAsync(image,region);
        RequireValue(image,lines,row.Name,expected);
        Console.WriteLine($"Fast row {row.Name}: {timer.ElapsedMilliseconds} ms.");
    }
    private static NativeSettingRow RequireValue(Bitmap image,IReadOnlyList<RobloxSettingsText> lines,string name,string value)
    {
        var row=RobloxSettingsAutomation.ReadRow(image,lines,name);
        if(row?.Value!=value)
            throw new InvalidOperationException($"{name}: expected {value}, read {row?.Value??"unreadable"} at {image.Width}x{image.Height}.");
        return row;
    }
}
