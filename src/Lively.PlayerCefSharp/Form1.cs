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
using System.Linq;

namespace Lively.PlayerCefSharp
{
    public partial class Form1 : Form
    {
        #region init

        private enum PageType
        {
            shadertoy,
            yt,
            online,
            local,
        }

        private bool isPaused = false;
        private ChromiumWebBrowser chromeBrowser;
        private StartArgs startArgs;

        private bool initializedServices = false; //delay API init till loaded page
        private IHardwareUsageService hardwareUsageService;
        private IAudioVisualizerService audioVisualizerService;
        private INowPlayingService nowPlayingService;

        public Form1()
        {
            InitializeComponent();
#if DEBUG
            startArgs = new StartArgs
            {
                // .html fullpath
                Url = @"",
                //online or local(file)
                Type = "local",
                // LivelyProperties.json path if any
                Properties = @"",
                SysInfo = false,
                NowPlaying = false,
                AudioVisualizer = false,
            };

            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.WindowState = FormWindowState.Normal;
            this.StartPosition = FormStartPosition.Manual;
            this.Size = new Size(1920, 1080);
            this.ShowInTaskbar = true;
            this.MaximizeBox = true;
            this.MinimizeBox = true;
#endif

#if DEBUG != true
            Parser.Default.ParseArguments<StartArgs>(Environment.GetCommandLineArgs())
                .WithParsed((x) => startArgs = x)
                .WithNotParsed(HandleParseError);

            this.WindowState = FormWindowState.Normal;
            this.StartPosition = FormStartPosition.Manual;
            this.Location = new Point(-9999, 0);

            if (startArgs.Geometry != null)
            {
                var msg = startArgs.Geometry.Split('x');
                if (msg.Length >= 2 && int.TryParse(msg[0], out int width) && int.TryParse(msg[1], out int height))
                {
                    this.Size = new Size(width, height);
                }
            }
#endif

            try
            {
                //CEF init
                InitializeCefSharp();
            }
            finally
            {
#if DEBUG != true
                _ = StdInListener();
#endif
            }
        }

        private void HandleParseError(IEnumerable<Error> errs)
        {
            WriteToParent(new LivelyMessageConsole()
            {
                Category = ConsoleMessageType.error,
                Message = $"Error parsing cmdline args: {errs.First()}",
            });
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
        public async Task StdInListener()
        {
            try
            {
                await Task.Run(async () =>
                {
                    while (true) // Loop runs only once per line received
                    {
                        string text = await Console.In.ReadLineAsync();
                        if (startArgs.VerboseLog)
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
                                        if (chromeBrowser.CanExecuteJavascriptInMainFrame && startArgs.PauseEvent && !isPaused) //if js context ready
                                        {
                                            chromeBrowser.ExecuteScriptAsync("livelyWallpaperPlaybackChanged",
                                                JsonConvert.SerializeObject(new WallpaperPlaybackState() { IsPaused = true }),
                                                Formatting.Indented);
                                        }
                                        isPaused = true;
                                        break;
                                    case MessageType.cmd_resume:
                                        if (chromeBrowser.CanExecuteJavascriptInMainFrame && isPaused)
                                        {
                                            if (startArgs.PauseEvent)
                                            {
                                                chromeBrowser.ExecuteScriptAsync("livelyWallpaperPlaybackChanged",
                                                    JsonConvert.SerializeObject(new WallpaperPlaybackState() { IsPaused = false }),
                                                    Formatting.Indented);
                                            }

                                            if (startArgs.NowPlaying)
                                            {
                                                //update media state
                                                chromeBrowser.ExecuteScriptAsync("livelyCurrentTrack", JsonConvert.SerializeObject(nowPlayingService?.CurrentTrack, Formatting.Indented));
                                            }
                                        }
                                        isPaused = false;
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
                                            await CaptureScreenshot(scr.FilePath, scr.Format);
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
                                        var filePath = Path.Combine(Path.GetDirectoryName(startArgs.Url), fd.Value);
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
                                            RestoreLivelyProperties(startArgs.Properties);
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
                Application.Exit();
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            audioVisualizerService?.Dispose();
            hardwareUsageService?.Stop();
            nowPlayingService?.Stop();
            chromeBrowser?.Dispose();
            Cef.Shutdown();
        }

        public static void WriteToParent(IpcMessage obj)
        {
#if DEBUG != true
            Console.WriteLine(JsonConvert.SerializeObject(obj));
#endif
            Debug.WriteLine(JsonConvert.SerializeObject(obj));
        }

        #endregion //ipc

        #region cef

        /// <summary>
        /// starts up & loads cef instance.
        /// </summary>
        public void InitializeCefSharp()
        {
            CefSettings settings = new CefSettings();
            //ref: https://magpcss.org/ceforum/apidocs3/projects/(default)/_cef_browser_settings_t.html#universal_access_from_file_urls
            //settings.CefCommandLineArgs.Add("allow-universal-access-from-files", "1"); //UNSAFE, Testing Only!
            if (startArgs.Volume == 0)
            {
                settings.CefCommandLineArgs.Add("--mute-audio", "1");
            }
            //auto-play video without it being muted (default cef behaviour is overriden.)
            settings.CefCommandLineArgs.Add("autoplay-policy", "no-user-gesture-required");
            settings.LogFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                "Lively Wallpaper", "Cef", "logfile.txt");
            //settings.BackgroundColor = Cef.ColorSetARGB(255, 43, 43, 43);
            if (!string.IsNullOrWhiteSpace(startArgs.DebugPort) && int.TryParse(startArgs.DebugPort, out int value))
                settings.RemoteDebuggingPort = value;

            if (!string.IsNullOrWhiteSpace(startArgs.CachePath))
            {
                settings.CachePath = startArgs.CachePath;
            }
            else
            {
                //Creates GPUCache regardless even if disk CachePath is not set!
                settings.CefCommandLineArgs.Add("--disable-gpu-shader-disk-cache");
            }

            PageType pageType = default;
            string path = startArgs.Url;
            if (startArgs.Type.Equals("local", StringComparison.OrdinalIgnoreCase))
            {
                pageType = PageType.local;
            }
            else if (startArgs.Type.Equals("online", StringComparison.OrdinalIgnoreCase))
            {
                string tmp = null;
                if (LinkUtil.TryParseShadertoy(startArgs.Url, ref tmp))
                {
                    pageType = PageType.shadertoy;
                    path = tmp;
                }
                else if ((tmp = LinkUtil.GetYouTubeVideoIdFromUrl(path)) != "")
                {
                    pageType = PageType.yt;
                    path = "https://www.youtube.com/embed/" + tmp +
                        "?version=3&rel=0&autoplay=1&loop=1&controls=0&playlist=" + tmp;
                }
                else
                {
                    pageType = PageType.online;
                }
            }

            switch (pageType)
            {
                case PageType.shadertoy:
                    {
                        Cef.Initialize(settings);
                        chromeBrowser = new ChromiumWebBrowser(string.Empty);
                        chromeBrowser.LoadHtml(path);
                    }
                    break;
                case PageType.yt:
                    {
                        Cef.Initialize(settings);
                        chromeBrowser = new ChromiumWebBrowser(string.Empty);
                        chromeBrowser.Load(path);
                    }
                    break;
                case PageType.local:
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
                case PageType.online:
                    {
                        Cef.Initialize(settings);
                        chromeBrowser = new ChromiumWebBrowser(path);
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

        private void ChromeBrowser_ConsoleMessage(object sender, ConsoleMessageEventArgs e)
        {
            WriteToParent(new LivelyMessageConsole()
            {
                Category = ConsoleMessageType.console,
                Message =$"{e.Message}, source: {e.Source} ({e.Line})",
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

            RestoreLivelyProperties(startArgs.Properties);
            WriteToParent(new LivelyMessageWallpaperLoaded() { Success = true });

            if (!initializedServices)
            {
                initializedServices = true;
                if (startArgs.AudioVisualizer)
                {
                    audioVisualizerService = new AudioVisualizerService();
                    audioVisualizerService.AudioDataAvailable += (s, e) =>
                    {
                        try
                        {
                            if (isPaused)
                                return;

                            if (chromeBrowser.CanExecuteJavascriptInMainFrame) //if js context ready
                            {
                                ExecuteScriptFunctionAsync("livelyAudioListener", e);
                            }
                        }
                        catch (Exception)
                        {
                            //TODO
                        }
                    };
                    audioVisualizerService.Start();
                }

                if (startArgs.NowPlaying)
                {
                    nowPlayingService = new NpsmNowPlayingService();
                    nowPlayingService.NowPlayingTrackChanged += (s, e) => {
                        try
                        {
                            if (isPaused)
                                return;

                            if (chromeBrowser.CanExecuteJavascriptInMainFrame) //if js context ready
                            {
                                chromeBrowser.ExecuteScriptAsync("livelyCurrentTrack", JsonConvert.SerializeObject(e, Formatting.Indented));
                            }
                        }
                        catch (Exception ex)
                        {
                            //TODO

                        }
                    };
                    nowPlayingService.Start();
                }

                if (startArgs.SysInfo)
                {
                    hardwareUsageService = new PerfCounterUsageService();
                    hardwareUsageService.HWMonitor += (s, e) =>
                    {
                        try
                        {
                            if (isPaused)
                                return;

                            if (chromeBrowser.CanExecuteJavascriptInMainFrame) //if js context ready
                            {
                                chromeBrowser.ExecuteScriptAsync("livelySystemInformation", JsonConvert.SerializeObject(e, Formatting.Indented));
                            }
                        }
                        catch
                        {
                            //TODO
                        }
                    };
                    hardwareUsageService.Start();
                }
            }
        }

        private void RestoreLivelyProperties(string path)
        {
            try
            {
                if (path == null)
                    return;

                if (chromeBrowser.CanExecuteJavascriptInMainFrame) //if js context ready
                {
                    foreach (var item in JsonUtil.ReadJObject(path))
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
                                var filePath = Path.Combine(Path.GetDirectoryName(startArgs.Url), item.Value["folder"].ToString(), item.Value["value"].ToString());
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
            if (startArgs.Type.Equals("local", StringComparison.OrdinalIgnoreCase) || e.ErrorCode == CefErrorCode.Aborted || e.ErrorCode == (CefErrorCode)(-27))//e.ErrorCode == CefErrorCode.NameNotResolved || e.ErrorCode == CefErrorCode.InternetDisconnected   || e.ErrorCode == CefErrorCode.NetworkAccessDenied || e.ErrorCode == CefErrorCode.NetworkIoSuspended)
            {
                //ignoring some error's.
                return;
            }
            chromeBrowser.LoadHtml(@"<head> <meta charset=""utf - 8""> <title>Error</title>  <style>
            * { line-height: 1.2; margin: 0; } html { display: table; font-family: sans-serif; height: 100%; text-align: center; width: 100%; } body { background-color: #252525; display:
            table-cell; vertical-align: middle; margin: 2em auto; } h1 { color: #e5e5e5; font-size: 2em; font-weight: 400; } p { color: #cccccc; margin: 0 auto; width: 280px; } .url{color: #e5e5e5; position: absolute; margin: 16px; right: 0; top: 0; } @media only
            screen and (max-width: 280px) { body, p { width: 95%; } h1 { font-size: 1.5em; margin: 0 0 0.3em; } } </style></head><body><div class=""url"">" + startArgs.Url + "</div> <h1>Unable to load webpage :'(</h1> <p>" + e.ErrorText + "</p></body></html>");
            //chromeBrowser.LoadHtml(@"<body style=""background-color:black;""><h1 style = ""color:white;"">Error Loading webpage:" + e.ErrorText + "</h1></body>");            
        }

        #endregion //cef

        #region helpers

        private async Task CaptureScreenshot(string filePath, ScreenshotFormat format)
        {
            if (chromeBrowser is null)
                return;

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

        /// <summary>
        /// Supports arrays
        /// </summary>
        /// <param name="functionName"></param>
        /// <param name="parameters"></param>
        private void ExecuteScriptFunctionAsync(string functionName, params object[] parameters)
        {
            var script = new StringBuilder();
            script.Append(functionName);
            script.Append("(");
            for (int i = 0; i < parameters.Length; i++)
            {
                script.Append(JsonConvert.SerializeObject(parameters[i]));
                if (i < parameters.Length - 1)
                {
                    script.Append(", ");
                }
            }
            script.Append(");");
            chromeBrowser?.ExecuteScriptAsync(script.ToString());
        }

        #endregion //helpers
    }
}
