using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;
using CefSharp;
using CefSharp.SchemeHandler;
using CefSharp.WinForms;
using System.IO;
using System.Diagnostics;
using CefSharp.Example.Handlers;
using CommandLine;
using System.Drawing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Drawing.Imaging;
using System.Text;
using Lively.PlayerCefSharp.Services;
using Lively.PlayerCefSharp.API;
using Lively.PlayerCefSharp.Helpers;

namespace Lively.PlayerCefSharp
{
    public partial class Form1 : Form
    {
        #region init

        private enum LinkType
        {
            shadertoy,
            yt,
            online,
            local,
            standalone
        }

        private LinkType linkType;
        private string originalUrl;
        private string debugPort;
        private string cachePath;
        private int cefVolume;
        private bool sysAudioEnabled;
        private bool sysMonitorEnabled;
        public string htmlPath;
        private string livelyPropertyPath;
        private string path;
        private Rectangle preferredWinSize = Rectangle.Empty;
        private JObject livelyPropertyData;
        private bool VerboseLog;
        private bool suspendJsMsg = false;
        private readonly PerfCounterUsage sysMonitor;
        private readonly SystemAudio sysAudio;
        private ChromiumWebBrowser chromeBrowser;

        public Form1()
        {
            InitializeComponent();
            this.WindowState = FormWindowState.Normal;
            this.StartPosition = FormStartPosition.Manual;
            this.Location = new Point(-9999, 0);

            CommandLine.Parser.Default.ParseArguments<StartArgs>(Environment.GetCommandLineArgs())
                .WithParsed(RunOptions)
                .WithNotParsed(HandleParseError);

            if (preferredWinSize != Rectangle.Empty)
            {
                this.Size = new Size(preferredWinSize.Width, preferredWinSize.Height);
            }

            //stdin message pipe
            StdInListener();

            try
            {
                // livelyPropertyPath loaded from commandline arg.
                livelyPropertyData = JsonUtil.Read(livelyPropertyPath);
            }
            catch
            {
                //can be non-customisable wp, file missing/corrupt error: skip.
            }

            //CEF init
            StartCef();

            //Only for local html wallpapers.
            sysMonitorEnabled = linkType == LinkType.local && sysMonitorEnabled;
            sysAudioEnabled = linkType == LinkType.local && sysAudioEnabled;

            if (sysAudioEnabled)
            {
                sysAudio = new SystemAudio();
                sysAudio.AudioData += SysAudio_AudioData;
                sysAudio.Start();
            }

            if (sysMonitorEnabled)
            {
                //todo: run this service in main lively pgm instead and pass msg via ipc.
                sysMonitor = new PerfCounterUsage();
                sysMonitor.HWMonitor += SysMonitor_HardwareUsage;
                sysMonitor.Start();
            }
        }

        private void RunOptions(StartArgs opts)
        {
            path = opts.Url;
            htmlPath = path;
            originalUrl = opts.Url;
            sysAudioEnabled = opts.AudioAnalyse;
            sysMonitorEnabled = opts.SysInfo;
            debugPort = opts.DebugPort;
            cachePath = opts.CachePath;
            cefVolume = opts.Volume;
            VerboseLog = opts.VerboseLog;

            if (opts.Geometry != null)
            {
                var msg = opts.Geometry.Split('x');
                if (msg.Length >= 2 && int.TryParse(msg[0], out int width) && int.TryParse(msg[1], out int height))
                {
                    //todo: send pos also.
                    preferredWinSize = new Rectangle(0, 0, width, height);
                }
            }

            if (opts.Type.Equals("local", StringComparison.OrdinalIgnoreCase))
            {
                linkType = LinkType.local;
            }
            else if (opts.Type.Equals("online", StringComparison.OrdinalIgnoreCase))
            {
                string tmp = null;
                if (LinkUtil.TryParseShadertoy(path, ref tmp))
                {
                    linkType = LinkType.shadertoy;
                    path = tmp;
                }
                else if((tmp = LinkUtil.GetYouTubeVideoIdFromUrl(htmlPath)) != "")
                {
                    linkType = LinkType.yt;
                    path = "https://www.youtube.com/embed/" + tmp +
                        "?version=3&rel=0&autoplay=1&loop=1&controls=0&playlist=" + tmp;
                }
                else
                {
                    linkType = LinkType.online;
                }
            }
            else if (opts.Type.Equals("deviantart", StringComparison.OrdinalIgnoreCase))
            {
                linkType = LinkType.standalone;

                this.FormBorderStyle = FormBorderStyle.Sizable;
                this.WindowState = FormWindowState.Maximized;
                //this.SizeGripStyle = SizeGripStyle.Show;
                this.ShowInTaskbar = true;
                this.MaximizeBox = true;
                this.MinimizeBox = true;
            }
            livelyPropertyPath = opts.Properties;
        }

        private void HandleParseError(IEnumerable<Error> errs)
        {
            if (Application.MessageLoop)
                Application.Exit();
            else
                Environment.Exit(1);
        }

        #endregion //init

        #region ipc

        public class WallpaperPlaybackState
        {
            public bool IsPaused { get; set; }
        }

        /// <summary>
        /// std I/O redirect, used to communicate with lively. 
        /// </summary>
        public async void StdInListener()
        {
            try
            {
                await Task.Run(async () =>
                {
                    while (true) // Loop runs only once per line received
                    {
                        string text = await Console.In.ReadLineAsync();
                        if (VerboseLog)
                        {
                            Console.WriteLine(text);
                        }

                        if (string.IsNullOrEmpty(text))
                        {
                            //When the redirected stream is closed, a null line is sent to the event handler. 
                            break;
                        }
                        else
                        {
                            try
                            {
                                var close = false;
                                var obj = JsonConvert.DeserializeObject<IpcMessage>(text, new JsonSerializerSettings() { Converters = { new IpcMessageConverter() } });
                                switch (obj.Type)
                                {
                                    case MessageType.cmd_reload:
                                        chromeBrowser?.Reload(true);
                                        break;
                                    case MessageType.cmd_suspend:
                                        suspendJsMsg = true;
                                        /*
                                        if (chromeBrowser.CanExecuteJavascriptInMainFrame) //if js context ready
                                        {
                                            chromeBrowser.ExecuteScriptAsync("livelyWallpaperPlaybackChanged", JsonConvert.SerializeObject(new WallpaperPlaybackState() { IsPaused = true }), Formatting.Indented);
                                        }
                                        */
                                        break;
                                    case MessageType.cmd_resume:
                                        suspendJsMsg = false;
                                        /*
                                        if (chromeBrowser.CanExecuteJavascriptInMainFrame) //if js context ready
                                        {
                                            chromeBrowser.ExecuteScriptAsync("livelyWallpaperPlaybackChanged", JsonConvert.SerializeObject(new WallpaperPlaybackState() { IsPaused = false }), Formatting.Indented);
                                        }
                                        */
                                        break;
                                    case MessageType.cmd_volume:
                                        var vc = (LivelyVolumeCmd)obj;
                                        chromeBrowser.GetBrowserHost()?.SetAudioMuted(vc.Volume == 0);
                                        break;
                                    case MessageType.cmd_screenshot:
                                        var success = true;
                                        var scr = (LivelyScreenshotCmd)obj;
                                        try
                                        {
                                            await CaptureScreenshot(chromeBrowser, scr.FilePath, scr.Format);
                                        }
                                        catch (Exception ie)
                                        {
                                            success = false;
                                            WriteToParent(new LivelyMessageConsole()
                                            {
                                                Category = ConsoleMessageType.error,
                                                Message = $"Screenshot capture fail: {ie.Message}"
                                            });
                                        }
                                        finally
                                        {
                                            WriteToParent(new LivelyMessageScreenshot()
                                            {
                                                FileName = Path.GetFileName(scr.FilePath),
                                                Success = success
                                            });      
                                        }
                                        break;
                                    case MessageType.lp_slider:
                                        var sl = (LivelySlider)obj;
                                        chromeBrowser.ExecuteScriptAsync("livelyPropertyListener", sl.Name, sl.Value);
                                        break;
                                    case MessageType.lp_textbox:
                                        var tb = (LivelyTextBox)obj;
                                        chromeBrowser.ExecuteScriptAsync("livelyPropertyListener", tb.Name, tb.Value);
                                        break;
                                    case MessageType.lp_dropdown:
                                        var dd = (LivelyDropdown)obj;
                                        chromeBrowser.ExecuteScriptAsync("livelyPropertyListener", dd.Name, dd.Value);
                                        break;
                                    case MessageType.lp_cpicker:
                                        var cp = (LivelyColorPicker)obj;
                                        chromeBrowser.ExecuteScriptAsync("livelyPropertyListener", cp.Name, cp.Value);
                                        break;
                                    case MessageType.lp_chekbox:
                                        var cb = (LivelyCheckbox)obj;
                                        chromeBrowser.ExecuteScriptAsync("livelyPropertyListener", cb.Name, cb.Value);
                                        break;
                                    case MessageType.lp_fdropdown:
                                        var fd = (LivelyFolderDropdown)obj;
                                        var filePath = Path.Combine(Path.GetDirectoryName(htmlPath), fd.Value);
                                        if (File.Exists(filePath))
                                        {
                                            chromeBrowser.ExecuteScriptAsync("livelyPropertyListener",
                                            fd.Name,
                                            fd.Value);
                                        }
                                        else
                                        {
                                            chromeBrowser.ExecuteScriptAsync("livelyPropertyListener",
                                            fd.Name,
                                            null); //or custom msg
                                        }
                                        break;
                                    case MessageType.lp_button:
                                        var btn = (LivelyButton)obj;
                                        if (btn.IsDefault)
                                        {
                                            try
                                            {
                                                //load new file.
                                                livelyPropertyData = JsonUtil.Read(livelyPropertyPath);
                                                //restore new property values.
                                                RestoreLivelyPropertySettings();
                                            }
                                            catch (Exception ex)
                                            {
                                                MessageBox.Show(ex.ToString(), "Lively Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                            }
                                        }
                                        else
                                        {
                                            chromeBrowser.ExecuteScriptAsync("livelyPropertyListener", btn.Name, true);
                                        }
                                        break;
                                    case MessageType.lsp_perfcntr:
                                        if (chromeBrowser.CanExecuteJavascriptInMainFrame) //if js context ready
                                        {
                                            chromeBrowser.ExecuteScriptAsync("livelySystemInformation", JsonConvert.SerializeObject(((LivelySystemInformation)obj).Info, Formatting.Indented));
                                        }
                                        break;
                                    case MessageType.lsp_nowplaying:
                                        if (chromeBrowser.CanExecuteJavascriptInMainFrame)
                                        {
                                            chromeBrowser.ExecuteScriptAsync("livelyCurrentTrack", JsonConvert.SerializeObject(((LivelySystemNowPlaying)obj).Info, Formatting.Indented));
                                        }
                                        break;
                                    case MessageType.cmd_close:
                                        close = true;
                                        break;
                                }

                                if (close)
                                {
                                    break;
                                }
                            }
                            catch (Exception e)
                            {
                                WriteToParent(new LivelyMessageConsole()
                                {
                                    Category = ConsoleMessageType.error,
                                    Message = $"Ipc parse error: {e.Message}"
                                });
                            }
                        }
                    }
                });
            }
            catch (Exception e)
            {
                WriteToParent(new LivelyMessageConsole()
                {
                    Category = ConsoleMessageType.error,
                    Message = $"Ipc stdin error: {e.Message}",
                });
            }
            finally
            {
                sysAudio?.Dispose();
                sysMonitor?.Stop();
                chromeBrowser?.Dispose();
                Cef.Shutdown();
                Application.Exit();
            }
        }

        public static void WriteToParent(IpcMessage obj)
        {
            Console.WriteLine(JsonConvert.SerializeObject(obj));
        }

        #endregion //ipc

        #region cef

        /// <summary>
        /// starts up & loads cef instance.
        /// </summary>
        public void StartCef()
        {
            CefSettings settings = new CefSettings();
            //ref: https://magpcss.org/ceforum/apidocs3/projects/(default)/_cef_browser_settings_t.html#universal_access_from_file_urls
            //settings.CefCommandLineArgs.Add("allow-universal-access-from-files", "1"); //UNSAFE, Testing Only!
            if (cefVolume == 0)
            {
                settings.CefCommandLineArgs.Add("--mute-audio", "1");
            }
            //auto-play video without it being muted (default cef behaviour is overriden.)
            settings.CefCommandLineArgs.Add("autoplay-policy", "no-user-gesture-required");
            settings.LogFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                "Lively Wallpaper", "Cef", "logfile.txt");
            //settings.BackgroundColor = Cef.ColorSetARGB(255, 43, 43, 43);
            if (!string.IsNullOrWhiteSpace(debugPort))
            {
                //example-port: 8088
                if (int.TryParse(debugPort, out int value))
                    settings.RemoteDebuggingPort = value;
            }

            if (!string.IsNullOrWhiteSpace(cachePath))
            {
                settings.CachePath = cachePath;
            }
            else
            {
                //Creates GPUCache regardless even if disk CachePath is not set!
                settings.CefCommandLineArgs.Add("--disable-gpu-shader-disk-cache");
            }

            switch (linkType)
            {
                case LinkType.shadertoy:
                    {
                        Cef.Initialize(settings);
                        chromeBrowser = new ChromiumWebBrowser(string.Empty);
                        chromeBrowser.LoadHtml(path);
                    }
                    break;
                case LinkType.yt:
                    {
                        Cef.Initialize(settings);
                        chromeBrowser = new ChromiumWebBrowser(string.Empty);
                        chromeBrowser.Load(path);
                    }
                    break;
                case LinkType.local:
                    {
                        settings.RegisterScheme(new CefCustomScheme
                        {
                            SchemeName = "localfolder",
                            IsFetchEnabled = true,
                            //DomainName = "html",//Path.GetFileName(path),//"cefsharp",
                            SchemeHandlerFactory = new FolderSchemeHandlerFactory
                            (
                                rootFolder: Path.GetDirectoryName(path),
                                hostName: Path.GetFileName(path),
                                    defaultPage: Path.GetFileName(path)//"index.html" // will default to index.html
                            )

                        });
                        path = "localfolder://" + Path.GetFileName(path);

                        Cef.Initialize(settings);
                        chromeBrowser = new ChromiumWebBrowser(path);
                    }
                    break;
                case LinkType.online:
                    {
                        Cef.Initialize(settings);
                        chromeBrowser = new ChromiumWebBrowser(path);
                    }
                    break;
                case LinkType.standalone:
                    {
                        Cef.Initialize(settings);
                        chromeBrowser = new ChromiumWebBrowser(path)
                        {
                            DownloadHandler = new DownloadHandler()
                        };
                    }
                    break;
            }

            //cef right click contextmenu disable.
            chromeBrowser.MenuHandler = new CefMenuHandler();
            //disable links starting in new cef window.
            chromeBrowser.LifeSpanHandler = new CefPopUpHandle();

            this.Controls.Add(chromeBrowser);
            chromeBrowser.Dock = DockStyle.Fill;

            chromeBrowser.IsBrowserInitializedChanged += ChromeBrowser_IsBrowserInitializedChanged1;
            chromeBrowser.LoadingStateChanged += ChromeBrowser_LoadingStateChanged;
            chromeBrowser.LoadError += ChromeBrowser_LoadError;
            chromeBrowser.TitleChanged += ChromeBrowser_TitleChanged;
            chromeBrowser.ConsoleMessage += ChromeBrowser_ConsoleMessage;
        }

        private void SysAudio_AudioData(object sender, float[] fftBuffer)
        {
            if (suspendJsMsg)
                return;

            try
            {
                if (chromeBrowser.CanExecuteJavascriptInMainFrame) //if js context ready
                {
                    if (fftBuffer != null)
                    {
                        ExecuteScriptAsync(chromeBrowser, "livelyAudioListener", fftBuffer);
                    }
                }
            }
            catch (Exception)
            {
                //TODO
            }
        }

        private void SysMonitor_HardwareUsage(object sender, HWUsageMonitorEventArgs e)
        {
            if (suspendJsMsg)
                return;

            try
            {
                if (chromeBrowser.CanExecuteJavascriptInMainFrame) //if js context ready
                {
                    chromeBrowser.ExecuteScriptAsync("livelySystemInformation", JsonConvert.SerializeObject(e, Formatting.Indented));
                }
            }
            catch 
            {
                //TODO
            }
        }

        private void ChromeBrowser_ConsoleMessage(object sender, ConsoleMessageEventArgs e)
        {
            WriteToParent(new LivelyMessageConsole()
            {
                Category = ConsoleMessageType.console,
                Message = e.Message
            });
        }

        private void ChromeBrowser_TitleChanged(object sender, TitleChangedEventArgs e)
        {
            this.Invoke((MethodInvoker)(() => this.Text = e.Title));
        }

        private void ChromeBrowser_LoadingStateChanged(object sender, LoadingStateChangedEventArgs e)
        {
            if (e.IsLoading)
            {
                return;
            }

            if (livelyPropertyData != null)
            {
                RestoreLivelyPropertySettings();
            }
            WriteToParent(new LivelyMessageWallpaperLoaded() { Success = true });
        }

        private void RestoreLivelyPropertySettings()
        {
            try
            {
                if (chromeBrowser.CanExecuteJavascriptInMainFrame) //if js context ready
                {
                    foreach (var item in livelyPropertyData)
                    {
                        string uiElementType = item.Value["type"].ToString();
                        if (!uiElementType.Equals("button", StringComparison.OrdinalIgnoreCase) && !uiElementType.Equals("label", StringComparison.OrdinalIgnoreCase))
                        {
                            if (uiElementType.Equals("dropdown", StringComparison.OrdinalIgnoreCase))
                            {
                                chromeBrowser.ExecuteScriptAsync("livelyPropertyListener", item.Key, (int)item.Value["value"]);
                            }
                            else if (uiElementType.Equals("slider", StringComparison.OrdinalIgnoreCase))
                            {
                                chromeBrowser.ExecuteScriptAsync("livelyPropertyListener", item.Key, (double)item.Value["value"]);
                            }
                            else if (uiElementType.Equals("folderDropdown", StringComparison.OrdinalIgnoreCase))
                            {
                                var filePath = Path.Combine(Path.GetDirectoryName(htmlPath), item.Value["folder"].ToString(), item.Value["value"].ToString());
                                if (File.Exists(filePath))
                                {
                                    chromeBrowser.ExecuteScriptAsync("livelyPropertyListener",
                                    item.Key,
                                    Path.Combine(item.Value["folder"].ToString(), item.Value["value"].ToString()));
                                }
                                else
                                {
                                    chromeBrowser.ExecuteScriptAsync("livelyPropertyListener",
                                    item.Key,
                                    null); //or custom msg
                                }
                            }
                            else if (uiElementType.Equals("checkbox", StringComparison.OrdinalIgnoreCase))
                            {
                                chromeBrowser.ExecuteScriptAsync("livelyPropertyListener", item.Key, (bool)item.Value["value"]);
                            }
                            else if (uiElementType.Equals("color", StringComparison.OrdinalIgnoreCase) || uiElementType.Equals("textbox", StringComparison.OrdinalIgnoreCase))
                            {
                                chromeBrowser.ExecuteScriptAsync("livelyPropertyListener", item.Key, (string)item.Value["value"]);
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private void ChromeBrowser_IsBrowserInitializedChanged1(object sender, EventArgs e)
        {
            //sends cefsharp handle to lively. (this is a subprocess of this application, so simply searching process.mainwindowhandle won't help.)
            WriteToParent(new LivelyMessageHwnd()
            {
                Hwnd = chromeBrowser.GetBrowser().GetHost().GetWindowHandle().ToInt32()
            });
        }

        private void ChromeBrowser_LoadError(object sender, LoadErrorEventArgs e)
        {
            Debug.WriteLine("Error Loading Page:-" + e.ErrorText);  //ERR_BLOCKED_BY_RESPONSE, likely missing audio/video codec error for youtube.com?
            if (linkType == LinkType.local || e.ErrorCode == CefErrorCode.Aborted || e.ErrorCode == (CefErrorCode)(-27))//e.ErrorCode == CefErrorCode.NameNotResolved || e.ErrorCode == CefErrorCode.InternetDisconnected   || e.ErrorCode == CefErrorCode.NetworkAccessDenied || e.ErrorCode == CefErrorCode.NetworkIoSuspended)
            {
                //ignoring some error's.
                return;
            }
            chromeBrowser.LoadHtml(@"<head> <meta charset=""utf - 8""> <title>Error</title>  <style>
            * { line-height: 1.2; margin: 0; } html { display: table; font-family: sans-serif; height: 100%; text-align: center; width: 100%; } body { background-color: #252525; display:
            table-cell; vertical-align: middle; margin: 2em auto; } h1 { color: #e5e5e5; font-size: 2em; font-weight: 400; } p { color: #cccccc; margin: 0 auto; width: 280px; } .url{color: #e5e5e5; position: absolute; margin: 16px; right: 0; top: 0; } @media only
            screen and (max-width: 280px) { body, p { width: 95%; } h1 { font-size: 1.5em; margin: 0 0 0.3em; } } </style></head><body><div class=""url"">" + originalUrl + "</div> <h1>Unable to load webpage :'(</h1> <p>" + e.ErrorText + "</p></body></html>");
            //chromeBrowser.LoadHtml(@"<body style=""background-color:black;""><h1 style = ""color:white;"">Error Loading webpage:" + e.ErrorText + "</h1></body>");            
        }

        #endregion //cef

        #region helpers

        public async Task CaptureScreenshot(ChromiumWebBrowser chromeBrowser, string filePath, ScreenshotFormat format)
        {
            CefSharp.DevTools.DevToolsExtensions.CaptureFormat captureFormat = CefSharp.DevTools.DevToolsExtensions.CaptureFormat.png;
            switch (format)
            {
                case ScreenshotFormat.jpeg:
                    captureFormat = CefSharp.DevTools.DevToolsExtensions.CaptureFormat.jpeg;
                    break;
                case ScreenshotFormat.png:
                    captureFormat = CefSharp.DevTools.DevToolsExtensions.CaptureFormat.png;
                    break;
                case ScreenshotFormat.webp:
                    captureFormat = CefSharp.DevTools.DevToolsExtensions.CaptureFormat.webp;
                    break;
                case ScreenshotFormat.bmp:
                    // Not supported by cef
                    captureFormat = CefSharp.DevTools.DevToolsExtensions.CaptureFormat.png;
                    break;
            }
            byte[] imageBytes = await CefSharp.DevTools.DevToolsExtensions.CaptureScreenShotAsPng(chromeBrowser, captureFormat);

            switch (format)
            {
                case ScreenshotFormat.jpeg:
                case ScreenshotFormat.png:
                case ScreenshotFormat.webp:
                    {
                        // Write to disk
                        File.WriteAllBytes(filePath, imageBytes);
                    }
                    break;
                case ScreenshotFormat.bmp:
                    {
                        // Convert byte[] to Image
                        using var ms = new MemoryStream(imageBytes, 0, imageBytes.Length);
                        using var image = Image.FromStream(ms, true);
                        image.Save(filePath, ImageFormat.Bmp);
                    }
                    break;
            }
        }

        //original ref: https://github.com/cefsharp/CefSharp/pull/1372/files
        /// <summary>
        /// Modified for passing array to js.
        /// </summary>
        void ExecuteScriptAsync(ChromiumWebBrowser chromeBrowser, string methodName, float[] args)
        {
            var stringBuilder = new StringBuilder();
            stringBuilder.Append(methodName);
            stringBuilder.Append("([");

            for (int i = 0; i < args.Length; i++)
            {
                stringBuilder.Append(args[i]);
                stringBuilder.Append(",");
            }

            //Remove the trailing comma
            stringBuilder.Remove(stringBuilder.Length - 2, 2);

            stringBuilder.Append("]);");
            var script = stringBuilder.ToString();

            chromeBrowser.ExecuteScriptAsync(script);
        }

        #endregion //helpers
    }
}
