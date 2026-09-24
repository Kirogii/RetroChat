using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using RobloxChatLauncher.Utils;

namespace RobloxChatLauncher.Services;

internal sealed record RobloxSetting(string Name,bool Slider,int Minimum=0,int Maximum=10,bool Dropdown=false);
internal sealed record RobloxSettingsResult(bool Success,string Message,IReadOnlyDictionary<string,string> Values);
internal sealed record NativeSettingRow(string Name,Rectangle Label,string? Value,Rectangle[] Segments);

internal static class RobloxSettingsAutomation
{
    // Settings shown in the supplied native menu screenshots, in native order.
    internal const int FullSettingsTraversal=30;
    internal static readonly RobloxSetting[] Settings =
    {
        new("Shift Lock Switch",false),new("Camera Mode",false),new("Movement Mode",false),
        new("Maximum Frame Rate",false,Dropdown:true),new("Automatic Translations",false),
        new("Game Language",false,Dropdown:true),new("Automatic Chat Translation",false),
        new("Chat Translation Language",false,Dropdown:true),new("Option to View Untranslated Message",false),
        new("In-game friends chat notifications",false),new("Camera Sensitivity",true),
        new("Output Device",false),new("Volume",true),new("Haptics",false),new("Fullscreen",false),
        new("Graphics Mode",false),new("Graphics Quality",true,1),
        new("Background transparency",true),new("Text size",true,0,3),
        new("UI navigation toggle",false),new("Performance Stats",false),new("MicroProfiler",false),
        new("Camera Inverted",false),new("Developer Console",false),
        new("People's Names",false),new("My Badges",false)
    };
    // Native order is only a navigation hint. OCR confirms the destination.
    internal static readonly string[] NativeLabels =
    {
        "Shift Lock Switch","Camera Mode","Movement Mode","Maximum Frame Rate",
        "Automatic Translations","Game Language","Automatic Chat Translation","Chat Translation Language",
        "Option to View Untranslated Message","In-game friends chat notifications","Camera Sensitivity",
        "Output Device","Volume","Haptics","Fullscreen","Graphics Mode","Graphics Quality",
        "Background transparency","Text size","UI navigation toggle","Performance Stats","MicroProfiler",
        "Camera Inverted","Developer Console","People's Names","My Badges","Chat summaries","Rephrased messages"
    };

    internal static async Task EnterSettingsAsync(Func<Keys,Task> press,Func<Task<bool>> settingsVisible)
    {
        await press(Keys.Escape);
        // Roblox can remember Settings. Do not tab away from it.
        if(await settingsVisible())return;
        await press(Keys.Tab);
        // Slow rendering/OCR must never cause repeated Tab presses.
        for(int attempt=0;attempt<5;attempt++)
        {
            if(await settingsVisible())return;
            await Task.Delay(150);
        }
        throw new InvalidOperationException("Settings was not visible after Esc and Tab; no adjustment was sent.");
    }
    internal static async Task FindDownAsync(Func<Keys,Task> press,Func<Task<bool>> found)
    {
        for(int step=0;step<=FullSettingsTraversal;step++)
        {
            if(await found())return;
            if(step<FullSettingsTraversal)await press(Keys.Down);
        }
    }
    internal static async Task ScanDownAsync(Func<Keys,Task> press,Func<Task> capture)
    {
        await capture();
        for(int step=1;step<=FullSettingsTraversal;step++)
        {
            await press(Keys.Down);
            if(step%5==0)await capture();
        }
    }
    internal static (string Text,bool Selected)[] ReadFrameRatePopup(Bitmap image,IReadOnlyList<RobloxSettingsText> lines)
    {
        var options=lines.Where(line=>Math.Abs(line.Bounds.Left+line.Bounds.Width/2-image.Width/2)<image.Width*.10
            &&System.Text.RegularExpressions.Regex.IsMatch(line.Text.Trim(),@"^(?:Default\s*\()?\d+\s*FPS\)?$",System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            .OrderBy(line=>line.Bounds.Top).ToArray();
        if(options.Length<3)return Array.Empty<(string,bool)>();
        int x=Math.Clamp(image.Width/2-(int)(image.Width*.08),0,image.Width-1);
        var shades=options.Select(line=>
        {
            var c=image.GetPixel(x,Math.Clamp(line.Bounds.Top+line.Bounds.Height/2,0,image.Height-1));
            return (c.R+c.G+c.B)/3;
        }).ToArray();
        int brightest=shades.Max(),darkest=shades.Min();
        return options.Select((line,index)=>(line.Text.Trim(),brightest-darkest>=12&&shades[index]==brightest)).ToArray();
    }
    internal static bool IsAllowed(string name)=>Settings.Any(item=>item.Name==name);

    internal static async Task<RobloxSettingsResult> RunAsync(IntPtr window,Action<Keys> sendKey,
        string? label=null,int direction=0,int? targetStep=null)
    {
        if(label=="Developer Console")
        {
            if(window==IntPtr.Zero||NativeMethods.GetForegroundWindow()!=window)
                return new RobloxSettingsResult(false,"Roblox must be focused to open the console.",new Dictionary<string,string>());
            sendKey(Keys.F9);
            return new RobloxSettingsResult(true,"Developer console opened.",new Dictionary<string,string>{{label,"Open"}});
        }
        var session=new Session(window,sendKey);
        bool success=false;
        string message="";
        try
        {
            if(label!=null&&!IsAllowed(label))throw new InvalidOperationException("Unknown settings control: "+label);
            await session.OpenAsync();
            if(label==null)
            {
                await session.ReadAllAsync();
                success=session.Values.Count==Settings.Length;
                message=success?"Roblox settings loaded.":"Some settings could not be read. Selecting a control will locate it again.";
            }
            else
            {
                await session.AdjustAsync(label,direction,targetStep);
                success=true;
                message=label+": "+session.Values[label];
            }
        }
        catch(Exception ex) when(ex is ExternalException or System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException)
        {
            message="Could not update Roblox settings: "+ex.Message;
        }
        finally
        {
            await session.CloseAsync();
        }
        return new RobloxSettingsResult(success,message,new Dictionary<string,string>(session.Values,StringComparer.Ordinal));
    }

    internal static NativeSettingRow? ReadRow(Bitmap image,IReadOnlyList<RobloxSettingsText> lines,string name)
    {
        var match=FindLabel(lines,name,image.Width);
        if(match is not RobloxSettingsText label)return null;
        int centerY=label.Bounds.Top+label.Bounds.Height/2;
        double scale=MenuScale(image.Width,label.Bounds);
        bool slider=name is "Volume" or "Camera Sensitivity" or "Graphics Quality" or "Background transparency" or "Text size";
        Rectangle[] segments=slider?FindSliderSegments(image,label.Bounds,name=="Text size"?3:10):Array.Empty<Rectangle>();
        string? value=null;
        if(segments.Length>0)
        {
            int filled=0;
            foreach(var segment in segments)
            {
                Color color=image.GetPixel(segment.Left+segment.Width/2,segment.Top+segment.Height/2);
                if(Math.Max(color.R,Math.Max(color.G,color.B))>=145)filled++;
            }
            value=filled.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        else if(!slider)
        {
            var candidates=lines.Where(line=>line.Bounds.Left>=image.Width/2-85*scale&&
                line.Bounds.Right<image.Width/2+405*scale&&Math.Abs(line.Bounds.Top+line.Bounds.Height/2-centerY)<30*scale)
                .OrderBy(line=>Math.Abs(line.Bounds.Top+line.Bounds.Height/2-centerY));
            foreach(var candidate in candidates)
            {
                string clean=candidate.Text.Trim();
                string normalized=Normalize(clean);
                if(normalized is "off" or "0ff") {value="Off";break;}
                if(normalized is "on" or "0n") {value="On";break;}
                if(normalized is "automatic" or "manual") {value=normalized=="manual"?"Manual":"Automatic";break;}
                if(clean.Any(char.IsLetter)) {value=clean;break;}
            }
        }
        return new NativeSettingRow(name,label.Bounds,value,segments);
    }

    internal static RobloxSettingsText? FindLabel(IReadOnlyList<RobloxSettingsText> lines,string name,int width)
    {
        string wanted=Normalize(name);
        var candidates=lines.Where(line=>line.Bounds.Left>width/2-890&&line.Bounds.Left<width/2-70).ToArray();
        foreach(var line in candidates)if(Normalize(line.Text)==wanted)return line;
        foreach(var line in candidates)
            if(wanted.Length>=6&&EditDistance(Normalize(line.Text),wanted)<=2)return line;
        return null;
    }

    // Detect ten actual flat bars and their gaps. Sensitivity has a shorter track
    // than Volume; fixed 36-pixel samples incorrectly read its number field.
    internal static Rectangle[] FindSliderSegments(Bitmap image,Rectangle label,int expected=10)
    {
        double scale=MenuScale(image.Width,label);
        int left=Math.Max(0,image.Width/2-(int)(35*scale)),right=Math.Min(image.Width-1,image.Width/2+(int)(365*scale));
        int centerY=label.Top+label.Height/2;
        foreach(int offset in new[]{0,-2,2,-4,4,-6,6,-8,8,-10,10,-12,12,14,16})
        {
            int y=centerY+(int)Math.Round(offset*scale);
            if(y<1||y>=image.Height-1)continue;
            var runs=new List<(int X,int Width)>();
            int start=left;Color prior=image.GetPixel(left,y);
            for(int x=left+1;x<=right;x++)
            {
                Color color=image.GetPixel(x,y);
                if(ColorDistance(color,prior)>5||x==right)
                {
                    int length=x-start;
                    if(length>=12*scale&&length<=(expected==3?145:48)*scale)runs.Add((start,length));
                    start=x;prior=color;
                }
            }
            for(int i=0;i+expected<=runs.Count;i++)
            {
                var sequence=runs.Skip(i).Take(expected).ToArray();
                int typical=sequence[expected/2].Width;
                bool good=true;
                for(int j=0;j<sequence.Length;j++)
                {
                    if(Math.Abs(sequence[j].Width-typical)>5*scale){good=false;break;}
                    if(j>0)
                    {
                        int gap=sequence[j].X-sequence[j-1].X-sequence[j-1].Width;
                        if(gap<Math.Max(1,2*scale-1)||gap>8*scale){good=false;break;}
                    }
                }
                if(good)return sequence.Select(run=>new Rectangle(run.X,y,run.Width,1)).ToArray();
            }
        }
        return Array.Empty<Rectangle>();
    }

    internal static bool IsRowSelected(Bitmap image,NativeSettingRow row)
    {
        double scale=MenuScale(image.Width,row.Label);
        int inside=row.Label.Left-(int)Math.Round(7*scale),outside=row.Label.Left-(int)Math.Round(19*scale);
        int center=row.Label.Top+row.Label.Height/2;
        if(outside<0||inside>=image.Width||center<4||center>=image.Height-4)return false;
        int matches=0;
        foreach(int offset in new[]{-3,0,3})
        {
            Color a=image.GetPixel(inside,center+offset),b=image.GetPixel(outside,center+offset);
            int brighter=(a.R+a.G+a.B-b.R-b.G-b.B)/3;
            if(brighter>=7&&Math.Max(a.R,Math.Max(a.G,a.B))-Math.Min(a.R,Math.Min(a.G,a.B))<18)matches++;
        }
        return matches>=2;
    }

    private static double MenuScale(int width,Rectangle label)=>Math.Clamp((width/2.0-label.Left)/378.0,.65,2.5);
    private static int ColorDistance(Color a,Color b)=>Math.Max(Math.Abs(a.R-b.R),Math.Max(Math.Abs(a.G-b.G),Math.Abs(a.B-b.B)));
    private static string Normalize(string value)
    {
        var result=new StringBuilder(value.Length);
        foreach(char c in value)if(char.IsLetterOrDigit(c))result.Append(char.ToLowerInvariant(c));
        return result.ToString();
    }
    private static int EditDistance(string first,string second)
    {
        if(Math.Abs(first.Length-second.Length)>2)return 3;
        int[] prior=Enumerable.Range(0,second.Length+1).ToArray(),next=new int[second.Length+1];
        for(int i=1;i<=first.Length;i++)
        {
            next[0]=i;
            for(int j=1;j<=second.Length;j++)
                next[j]=Math.Min(Math.Min(next[j-1]+1,prior[j]+1),prior[j-1]+(first[i-1]==second[j-1]?0:1));
            (prior,next)=(next,prior);
        }
        return prior[second.Length];
    }

    // Bright label text is unchanged by the dark selection background. Comparing
    // its mask avoids OCR on every arrow key; scrolling invalidates the cache.
    internal static byte[] LabelMask(Bitmap image)
    {
        int left=Math.Max(0,image.Width/2-900),right=Math.Max(left+1,image.Width/2-80);
        var region=new Rectangle(left,0,right-left,image.Height);
        var bits=image.LockBits(region,ImageLockMode.ReadOnly,PixelFormat.Format24bppRgb);
        try
        {
            var mask=new byte[region.Width*region.Height];
            var row=new byte[region.Width*3];
            for(int y=0;y<region.Height;y++)
            {
                Marshal.Copy(IntPtr.Add(bits.Scan0,y*bits.Stride),row,0,row.Length);
                for(int x=0;x<region.Width;x++)
                    mask[y*region.Width+x]=(byte)(Math.Min(row[x*3],Math.Min(row[x*3+1],row[x*3+2]))>=150?1:0);
            }
            return mask;
        }
        finally{image.UnlockBits(bits);}
    }
    // The downward search limit must not consume the budget for correcting Up.
    // Each direction remains bounded, and LocateAsync verifies the final frame.
    internal static int LimitSettingMove(Keys key,int requested,int downSent,int upSent)=>
        Math.Clamp(requested,0,Math.Max(0,FullSettingsTraversal-(key==Keys.Up?upSent:downSent)));
    internal static (Keys Key,int Count) PlanSettingMove(int target,int selected,int[] visible,Keys? lastDirection)
    {
        // Up is legal only when OCR sees the requested label and the selected
        // row together, with the requested label above the selection.
        if(selected>=0&&visible.Contains(target))
        {
            int distance=target-selected;
            return (distance<0?Keys.Up:Keys.Down,distance==0?0:1);
        }
        // If the requested label is offscreen, follow the normal downward scan.
        // Never infer that it is above from list indices or prior movement.
        return (Keys.Down,selected>=0?5:1);
    }
    private sealed class Frame : IDisposable
    {
        internal readonly Bitmap Image;
        internal readonly Point Origin;
        internal readonly IReadOnlyList<RobloxSettingsText> Lines;
        internal Frame(Bitmap image,Point origin,IReadOnlyList<RobloxSettingsText> lines){Image=image;Origin=origin;Lines=lines;}
        private readonly Dictionary<string,NativeSettingRow?> rows=new();
        internal NativeSettingRow? Row(string label)
        {
            if(!rows.TryGetValue(label,out var row))rows[label]=row=ReadRow(Image,Lines,label);
            return row;
        }
        internal bool IsOnlySelected(NativeSettingRow target)=>
            IsRowSelected(Image,target)&&!NativeLabels.Where(name=>name!=target.Name)
                .Select(Row).Any(row=>row!=null&&FullyVisible(row)&&IsRowSelected(Image,row));
        internal bool IsSettings=>NativeLabels.Count(name=>FindLabel(Lines,name,Image.Width)!=null)>=2;
        internal bool IsMenu=>Lines.Any(line=>Normalize(line.Text).Contains("settings"))&&
            Lines.Any(line=>Normalize(line.Text).Contains("resume")||Normalize(line.Text).Contains("people"));
        internal bool FullyVisible(NativeSettingRow row)
        {
            int top=Lines.Where(line=>Normalize(line.Text).Contains("settings")&&line.Bounds.Top<Image.Height/2)
                .Select(line=>line.Bounds.Bottom+18).DefaultIfEmpty(60).Min();
            int bottom=Lines.Where(line=>Normalize(line.Text).Contains("resume"))
                .Select(line=>line.Bounds.Top-20).DefaultIfEmpty(Image.Height-90).Max();
            return row.Label.Top>=top&&row.Label.Bottom<=bottom;
        }
        internal string Signature=>string.Join("|",NativeLabels.Where(name=>FindLabel(Lines,name,Image.Width)!=null));
        public void Dispose()=>Image.Dispose();
    }

    private sealed class Session
    {
        private readonly IntPtr window;
        private readonly Action<Keys> sendKey;

        private bool opened;
        private bool settingsConfirmed;

        private Frame? pendingFrame;
        private byte[]? layoutMask;
        private Size layoutSize;
        private IReadOnlyList<RobloxSettingsText>? layoutLines;
        internal readonly Dictionary<string,string> Values=new(StringComparer.Ordinal);
        internal Session(IntPtr window,Action<Keys> sendKey){this.window=window;this.sendKey=sendKey;}
        private void CheckFocus()
        {
            if(window==IntPtr.Zero||NativeMethods.IsIconic(window)||NativeMethods.GetForegroundWindow()!=window)
                throw new InvalidOperationException("Roblox lost focus; the macro stopped.");
        }
        private async Task PressAsync(Keys key,int delay=100)
        {
            CheckFocus();sendKey(key);await Task.Delay(delay);
        }
        private async Task<Frame> CaptureAsync(Rectangle? rowRegion=null,bool requireSettings=true,bool reuseLayout=false)
        {
            CheckFocus();
            if(!NativeMethods.GetClientRect(window,out var client)||client.Right<700||client.Bottom<400)
                throw new InvalidOperationException("The Roblox window is too small to read Settings.");
            Point origin=Point.Empty;
            if(!NativeMethods.ClientToScreen(window,ref origin))throw new InvalidOperationException("Could not locate the Roblox window.");
            var bitmap=new Bitmap(client.Right,client.Bottom,PixelFormat.Format24bppRgb);
            try
            {
                using(var graphics=Graphics.FromImage(bitmap))graphics.CopyFromScreen(origin,Point.Empty,bitmap.Size);
                byte[]? mask=rowRegion==null?LabelMask(bitmap):null;
                bool unchanged=reuseLayout&&mask!=null&&layoutMask!=null&&layoutSize==bitmap.Size
                    &&mask.AsSpan().SequenceEqual(layoutMask);
                var lines=unchanged?layoutLines!:rowRegion is Rectangle region
                    ? await RobloxSettingsOcr.ReadRowAsync(bitmap,Rectangle.Intersect(new Rectangle(Point.Empty,bitmap.Size),region))
                    : await RobloxSettingsOcr.ReadSettingsAsync(bitmap);
                if(rowRegion==null&&!unchanged){layoutMask=mask;layoutSize=bitmap.Size;layoutLines=lines;}
                CheckFocus();
                var frame=new Frame(bitmap,origin,lines);

                // Every capture doubles as the active-menu watchdog. Captures
                // happen more often than once per second while the macro runs,
                // so a manually closed Settings menu cancels before more input.
                if(settingsConfirmed&&requireSettings&&rowRegion==null&&!frame.IsSettings)
                {
                    frame.Dispose();
                    throw new InvalidOperationException("Roblox Settings was closed; the macro stopped.");
                }

                foreach(var setting in Settings)
                {
                    var row=frame.Row(setting.Name);
                    if(row?.Value!=null&&frame.FullyVisible(row))Values[setting.Name]=row.Value;
                }
                return frame;
            }
            catch{bitmap.Dispose();throw;}
        }

        internal async Task OpenAsync()
        {
            await EnterSettingsAsync(async key=>
            {
                await PressAsync(key,key==Keys.Escape?200:100);
                if(key==Keys.Escape)opened=true;
            },async ()=>
            {
                var frame=await CaptureAsync();
                if(frame.IsSettings){pendingFrame=frame;return true;}
                frame.Dispose();return false;
            });
            settingsConfirmed=true;
        }
        private async Task<Frame> LocateAsync(string label,bool requireSelected=false)
        {
            pendingFrame?.Dispose();pendingFrame=null;
            int target=Array.IndexOf(NativeLabels,label),downSent=0,upSent=0,reversals=0;
            Keys? lastDirection=null;
            bool waitedForInitialFocus=false;
            while(true)
            {
                Frame? frame=await CaptureAsync();
                try
                {
                    var wanted=frame.Row(label);
                    if(wanted!=null&&frame.FullyVisible(wanted))
                    {
                        if(!requireSelected){var result=frame;frame=null;return result;}
                        if(frame.IsOnlySelected(wanted))
                        {
                            for(int attempt=0;attempt<3;attempt++)
                            {
                                await Task.Delay(90);
                                var next=await CaptureAsync();
                                var confirmed=next.Row(label);
                                bool stable=confirmed!=null&&next.FullyVisible(confirmed)&&next.IsOnlySelected(confirmed)
                                    &&Math.Abs(confirmed.Label.Top-wanted.Label.Top)<=3;
                                frame.Dispose();frame=next;
                                if(stable){var result=frame;frame=null;return result;}
                                if(confirmed!=null)wanted=confirmed;
                            }
                            throw new InvalidOperationException("The target selection did not settle; no setting change was sent.");
                        }
                    }
                    // Read the final scan frame before deciding whether correction is needed.
                    var selected=NativeLabels.Select(frame.Row)
                        .Where(row=>row!=null&&frame.FullyVisible(row)&&IsRowSelected(frame.Image,row)).ToArray();
                    int current=selected.Length==1?Array.IndexOf(NativeLabels,selected[0]!.Name):-1;
                    if(current<0&&wanted!=null&&frame.FullyVisible(wanted)&&!waitedForInitialFocus)
                    {
                        // Settings can draw labels before drawing its initial
                        // keyboard highlight. Recheck without moving off target.
                        waitedForInitialFocus=true;
                        frame.Dispose();frame=null;
                        await Task.Delay(1000);
                        continue;
                    }
                    var visible=NativeLabels.Select((name,index)=>(row:frame.Row(name),index))
                        .Where(item=>item.row!=null&&frame.FullyVisible(item.row)).Select(item=>item.index).ToArray();
                    // The same five-key burst used by ReadAll finds an offscreen
                    // target. Once visible, approach it from observed keyboard focus.
                    var move=PlanSettingMove(target,current,visible,lastDirection);
                    // Up requires actual OCR geometry, not just the known list order.
                    if(move.Key==Keys.Up&&(wanted==null||!frame.FullyVisible(wanted)||selected.Length!=1||
                        wanted.Label.Top>=selected[0]!.Label.Top))
                        throw new InvalidOperationException("OCR did not confirm the target above the selected row; no change was sent.");
                    if(lastDirection.HasValue&&move.Key!=lastDirection.Value)
                    {
                        // Confirm a reversal on a fresh frame before sending it.
                        await Task.Delay(100);
                        using var check=await CaptureAsync();
                        var focus=NativeLabels.Select(check.Row)
                            .Where(row=>row!=null&&check.FullyVisible(row)&&IsRowSelected(check.Image,row)).ToArray();
                        if(focus.Length!=1||Array.IndexOf(NativeLabels,focus[0]!.Name)!=current)
                            throw new InvalidOperationException("Settings focus changed during navigation; no value was changed.");
                        var checkedTarget=check.Row(label);
                        if(move.Key==Keys.Up&&(checkedTarget==null||!check.FullyVisible(checkedTarget)||
                            checkedTarget.Label.Top>=focus[0]!.Label.Top))
                            throw new InvalidOperationException("The requested row moved during confirmation; no value was changed.");
                        if(++reversals>1)throw new InvalidOperationException("Settings navigation did not converge; no value was changed.");
                    }
                    lastDirection=move.Key;
                    int count=LimitSettingMove(move.Key,move.Count,downSent,upSent);
                    if(count==0)break;
                    frame.Dispose();frame=null;
                    for(int step=0;step<count;step++)await PressAsync(move.Key,count>1?8:100);
                    // Roblox animates fast list movement and the labels are blurry
                    // during that animation. OCR only after a full visual settle.
                    // OCR only after either direction has fully repainted.
                    await Task.Delay(1000);
                    if(move.Key==Keys.Up)upSent+=count;else downSent+=count;
                }
                finally{frame?.Dispose();}
            }
            throw new InvalidOperationException("Could not confirm keyboard focus on "+label+"; no change was sent.");
        }
        internal async Task ReadAllAsync()
        {
            // The initial check never seeks a row or sends Up. Capture the
            // opening page, then walk straight down through the whole list.
            await ScanDownAsync(key=>PressAsync(key,8),async ()=>
            {
                using var frame=pendingFrame??await CaptureAsync();
                pendingFrame=null;
            });
        }
        internal async Task AdjustAsync(string label,int direction,int? targetStep)
        {
            if(label=="Graphics Quality")
            {
                using var mode=await LocateAsync("Graphics Mode");
                if(mode.Row("Graphics Mode")?.Value=="Automatic")await AdjustAsync("Graphics Mode",1,null);
            }
            using var before=await LocateAsync(label,true);
            var row=before.Row(label)!;
            if(string.Equals(row.Value,"Unavailable",StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(label+" is unavailable in this Roblox experience.");
            if(row.Value==null)throw new InvalidOperationException("The current value of "+label+" could not be read.");
            var setting=Settings.First(item=>item.Name==label);
            int count=1;
            int sign=direction<0?-1:1;
            int expected=-1;
            string? expectedDropdown=null;
            if(setting.Slider)
            {
                if(!int.TryParse(row.Value,out int current))throw new InvalidOperationException("The "+label+" slider could not be read.");
                expected=Math.Clamp(targetStep??current+sign,setting.Minimum,setting.Maximum);
                count=Math.Abs(expected-current);sign=Math.Sign(expected-current);
                if(count==0){Values[label]=row.Value;return;}
            }
            CheckFocus();
            double scale=MenuScale(before.Image.Width,row.Label);
            var rowRegion=Rectangle.FromLTRB(Math.Max(0,row.Label.Left-25),Math.Max(0,row.Label.Top-(int)(18*scale)),
                Math.Min(before.Image.Width,before.Image.Width/2+(int)(410*scale)),Math.Min(before.Image.Height,row.Label.Bottom+(int)(18*scale)));
            if(setting.Dropdown)
            {
                await PressAsync(Keys.Enter,120);
                if(label=="Maximum Frame Rate")
                {
                    using var popup=await CaptureAsync();
                    var options=ReadFrameRatePopup(popup.Image,popup.Lines);
                    int selected=Array.FindIndex(options,option=>option.Selected);
                    if(selected<0||options.Count(option=>option.Selected)!=1)
                        throw new InvalidOperationException("The frame-rate popup selection could not be confirmed.");
                    int target=Math.Clamp(selected+sign,0,options.Length-1);
                    expectedDropdown=options[target].Text;
                    if(target!=selected)await PressAsync(target>selected?Keys.Down:Keys.Up,100);
                    using var moved=await CaptureAsync();
                    var movedOptions=ReadFrameRatePopup(moved.Image,moved.Lines);
                    if(movedOptions.Count(option=>option.Selected)!=1||!movedOptions.Any(option=>option.Selected&&Normalize(option.Text)==Normalize(expectedDropdown)))
                        throw new InvalidOperationException("The frame-rate popup did not select the requested option.");
                    await PressAsync(Keys.Enter,120);
                }
                else
                {
                    await PressAsync(sign>0?Keys.Down:Keys.Up,60);
                    await PressAsync(Keys.Enter,100);
                }
            }
            else for(int step=0;step<count;step++)await PressAsync(sign>0?Keys.Right:Keys.Left,25);
            await Task.Delay(label=="Fullscreen"?250:40);
            for(int verify=0;verify<3;verify++)
            {
                using var after=await CaptureAsync(label=="Fullscreen"||setting.Dropdown?null:rowRegion);
                if(label=="Maximum Frame Rate"&&ReadFrameRatePopup(after.Image,after.Lines).Length>0){await Task.Delay(40);continue;}
                var result=after.Row(label);
                if(result?.Value!=null&&
                    (setting.Slider?result.Value==expected.ToString():expectedDropdown!=null?Normalize(result.Value)==Normalize(expectedDropdown):result.Value!=row.Value))
                {
                    Values[label]=result.Value;return;
                }
                await Task.Delay(80);
            }
            throw new InvalidOperationException("Roblox did not confirm the requested "+label+" value. No extra adjustment was sent.");
        }

        internal async Task CloseAsync()
        {
            pendingFrame?.Dispose();pendingFrame=null;
            try
            {
                if(opened&&NativeMethods.GetForegroundWindow()==window)
                {
                    using(var frame=await CaptureAsync(requireSettings:false))
                    {
                        if(ReadFrameRatePopup(frame.Image,frame.Lines).Length>0)await PressAsync(Keys.Escape,150);
                        else if(!frame.IsMenu&&!frame.IsSettings)return;
                    }
                    // A successful open owns one native menu, even if a later
                    // OCR frame cannot see its header while scrolling.
                    await PressAsync(Keys.Escape,250);
                    using var closed=await CaptureAsync(requireSettings:false);
                    if(closed.IsMenu||closed.IsSettings)
                    {
                        await Task.Delay(200);
                        using var settled=await CaptureAsync(requireSettings:false);
                        if(settled.IsMenu||settled.IsSettings)await PressAsync(Keys.Escape,250);
                    }

                }
            }
            catch(Exception ex) when(ex is ExternalException or System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException)
            { /* Preserve the original result if the game closed or lost focus. */ }
        }
    }
}
