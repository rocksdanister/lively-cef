using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using CefSharp;
using CefSharp.SchemeHandler;
using CefSharp.WinForms;
using System.IO;
using System.Diagnostics;
using CSCore;
using CSCore.DSP;
using CSCore.SoundOut;
using CSCore.SoundIn;
using CSCore.Streams;
using CSCore.Streams.Effects;
using System.Runtime.InteropServices;
using CefSharp.Example.Handlers;
using cefsharptest.Widgets;
using CommandLine;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Drawing;
using System.Web;
using Newtonsoft.Json;
using livelywpf.Helpers;
using livelywpf.Core.API;
using Newtonsoft.Json.Linq;

namespace cefsharptest
{
    public partial class Form1 : Form
    {
        #region init

        private static Form mainForm;
        private enum LinkType
        {
            shadertoy,
            yt,
            online,
            local,
            deviantart
        }
        private LinkType linkType;
        private string originalUrl;
        private string debugPort;
        private string cachePath;
        private int cefVolume;
        private bool enableCSCore = false;
        private bool enableSysInfo = false;
        public static string htmlPath = null;
        public static string livelyPropertyPath = null;
        public static bool livelyPropertyRestoreDisabled = false;
        private string path = null;
        private readonly string[] args = Environment.GetCommandLineArgs();
        private Rectangle preferredWinSize = Rectangle.Empty;

        public static bool VerboseLog = false;

        //hw monitor
        private readonly  HWUsageMonitor hwMonitor = null;

        //cscore
        private WasapiCapture _soundIn;
        private ISoundOut _soundOut;
        private IWaveSource _source;
        private LineSpectrum _lineSpectrum;
        private PitchShifter _pitchShifter;
        private static readonly System.Windows.Forms.Timer wasapiAudioTimer = new System.Windows.Forms.Timer();
        //private static readonly System.Windows.Forms.Timer systemMonitorTimer = new System.Windows.Forms.Timer();
        public static ChromiumWebBrowser chromeBrowser;

        public Form1()
        {
            InitializeComponent();
            this.WindowState = FormWindowState.Normal;
            this.StartPosition = FormStartPosition.Manual;
            //this.Location = new Point(-9999, 0);

            CommandLine.Parser.Default.ParseArguments<Options>(args)
                .WithParsed(RunOptions)
                .WithNotParsed(HandleParseError);

            if (preferredWinSize != Rectangle.Empty)
            {
                this.Size = new Size(preferredWinSize.Width, preferredWinSize.Height);
            }

            mainForm = this;
            ListenToParent(); //stdin listen pipe.

            try
            {
                // livelyPropertyPath loaded from commandline arg.
                WidgetData.LoadLivelyProperties(livelyPropertyPath);
            }
            catch
            {
                //can be non-customisable wp, file missing/corrupt error: skip.
            }

            //Initialize chromium.
            InitializeChromium();

            //Start services after Initialize chromium.
            enableCSCore = linkType == LinkType.local && enableCSCore;
            if (enableCSCore)
            {
                //timer, audio sends audio data etc
                wasapiAudioTimer.Interval = 33; //30fps
                wasapiAudioTimer.Tick += Timer_Tick1;

                try
                {
                    //audio analyser  
                    CSCoreInit();
                    wasapiAudioTimer.Start();
                }
                catch
                {
                    Console.WriteLine("CSCORE error: Failed to start audio capture");
                }
            }

            enableSysInfo = linkType == LinkType.local && enableSysInfo;
            if (enableSysInfo)
            {
                //todo: run this service in main lively pgm instead and pass msg via ipc.
                hwMonitor = new HWUsageMonitor();
                hwMonitor.HWMonitor += Instance_HWMonitor;
                hwMonitor.StartService();
            }
        }

        private void Instance_HWMonitor(object sender, livelywpf.Helpers.HWUsageMonitorEventArgs e)
        {
            try
            {
                if (chromeBrowser.CanExecuteJavascriptInMainFrame) //if js context ready
                {
                    chromeBrowser.ExecuteScriptAsync("livelySystemInformation", JsonConvert.SerializeObject(e, Formatting.Indented));
                }
            }
            catch { }
        }

        #endregion //init

        #region commandline

        class Options
        {
            [Option( "url", 
            Required = true,
            HelpText = "The url/html-file to load.")]
            public string Url { get; set; }

            [Option("property",
            Required = false,
            Default = null,
            HelpText = "LivelyProperties.info filepath (SaveData/wpdata).")]
            public string Properties { get; set; }

            [Option("type",
            Required = true,
            HelpText = "LinkType class.")]
            public string Type { get; set; }

            [Option("display",
            Required = true,
            HelpText = "Wallpaper running display.")]
            public string DisplayDevice { get; set; }

            [Option("geometry",
            Required = false,
            HelpText = "Window size (WxH).")]
            public string Geometry { get; set; }

            [Option("audio",
            Default = false,
            HelpText = "Analyse system audio(visualiser data.)")]
            public bool AudioAnalyse { get; set; }

            [Option("debug",
            Required = false,
            HelpText = "Debugging port")]
            public string DebugPort { get; set; }

            [Option("cache",
            Required = false,
            HelpText = "disk cache path")]
            public string CachePath { get; set; }

            [Option("volume",
            Required = false,
            Default = 100,
            HelpText = "Audio volume")]
            public int Volume { get; set; }

            [Option("system-information",
            Default = false,
            Required = false,
            HelpText = "Lively hw monitor api")]
            public bool SysInfo { get; set; }

            [Option("verbose-log",
            Required = false,
            HelpText = "Verbose Logging")]
            public bool VerboseLog { get; set; }
        }

        private void RunOptions(Options opts)
        {
            path = opts.Url;
            htmlPath = path;
            originalUrl = opts.Url;
            enableCSCore = opts.AudioAnalyse;
            enableSysInfo = opts.SysInfo;
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
                string ytVideoId;
                if (path.Contains("shadertoy.com/view"))
                {
                    linkType = LinkType.shadertoy;
                    path = ShadertoyURLtoEmbedLink(path);
                }
                else if((ytVideoId = GetYouTubeVideoIdFromUrl(htmlPath)) != "")
                {
                    linkType = LinkType.yt;
                    path = "https://www.youtube.com/embed/" + ytVideoId +
                        "?version=3&rel=0&autoplay=1&loop=1&controls=0&playlist=" + ytVideoId;
                }
                else
                {
                    linkType = LinkType.online;
                }
            }
            else if (opts.Type.Equals("deviantart", StringComparison.OrdinalIgnoreCase))
            {
                linkType = LinkType.deviantart;

                this.FormBorderStyle = FormBorderStyle.Sizable;
                this.WindowState = FormWindowState.Maximized;
                //this.SizeGripStyle = SizeGripStyle.Show;
                this.ShowInTaskbar = true;
                this.MaximizeBox = true;
                this.MinimizeBox = true;
            }

            //LivelyPropertiesInit(opts)
            livelyPropertyPath = opts.Properties;
        }

        private void HandleParseError(IEnumerable<Error> errs)
        {
            if (Application.MessageLoop)
                Application.Exit();
            else
                Environment.Exit(1);
        }

        #endregion //commandline

        #region ipc

        /// <summary>
        /// std I/O redirect, used to communicate with lively. 
        /// todo:- rewrite with named pipes.
        /// </summary>
        public async static void ListenToParent()
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
                            Console.WriteLine("Ipc msg: {0}", text);
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
                                        chromeBrowser.Reload(true);
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
                                                WidgetData.LoadLivelyProperties(livelyPropertyPath);
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
                                Console.WriteLine("Ipc parse error {0}", e.Message);
                            }
                        }
                    }
                });
            }
            catch (Exception e)
            {
                Console.WriteLine("Ipc stdin error {0}", e.Message);
            }
            finally
            {
                if (chromeBrowser != null)
                {
                    StopTimer();
                    chromeBrowser.Dispose();
                    Cef.Shutdown();
                }
                Application.Exit();
            }
        }

        //ref: https://github.com/rocksdanister/lively/issues/20
        private static async Task PlaybackWallpaperIPC(string val)
        {

            var msg = val.Split(' ');
            if (msg.Length < 2)
                return;

            //int id;
            if (msg[1].Equals("play", StringComparison.OrdinalIgnoreCase))
            {
                //id = await chromeBrowser.ExecuteDevToolsMethodAsync(0, "Page.setWebLifecycleState", new Dictionary<string, object> {{ "state", "active" }});
                await chromeBrowser.ExecuteDevToolsMethodAsync(0, "Debugger.resume");
            }
            else if (msg[1].Equals("pause", StringComparison.OrdinalIgnoreCase))
            {
                //id = await chromeBrowser.ExecuteDevToolsMethodAsync(0, "Page.setWebLifecycleState", new Dictionary<string, object> {{ "state", "frozen" }});
                await chromeBrowser.ExecuteDevToolsMethodAsync(0, "Debugger.pause");
            }
        }

        /// <summary>
        /// String Contains method with StringComparison property.
        /// </summary>
        /// <param name="str"></param>
        /// <param name="substring"></param>
        /// <param name="comp"></param>
        /// <returns></returns>
        public static bool Contains(String str, String substring,
                                    StringComparison comp)
        {
            if (substring == null | str == null)
                throw new ArgumentNullException("string",
                                             "substring/string cannot be null.");
            else if (!Enum.IsDefined(typeof(StringComparison), comp))
                throw new ArgumentException("comp is not a member of StringComparison",
                                         "comp");

            return str.IndexOf(substring, comp) >= 0;
        }

        #endregion //ipc

        #region cef

        /// <summary>
        /// starts up & loads cef instance.
        /// </summary>
        public void InitializeChromium()
        {
            CefSettings settings = new CefSettings();
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

            if (linkType == LinkType.local)
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
            }

            //ref: https://magpcss.org/ceforum/apidocs3/projects/(default)/_cef_browser_settings_t.html#universal_access_from_file_urls
            //settings.CefCommandLineArgs.Add("allow-universal-access-from-files", "1"); //UNSAFE, Testing Only!
            if(cefVolume == 0)
            {
                settings.CefCommandLineArgs.Add("--mute-audio", "1");
            }
            //auto-play video without it being muted (default cef behaviour is overriden.)
            settings.CefCommandLineArgs.Add("autoplay-policy", "no-user-gesture-required");

            if (linkType == LinkType.deviantart)
            {
                //System.IO.Directory.CreateDirectory(AppDomain.CurrentDomain.BaseDirectory + "//LivelyCefCache");
                settings.CachePath = AppDomain.CurrentDomain.BaseDirectory + "//LivelyCefCache";
            }
            Cef.Initialize(settings);

            if (linkType == LinkType.shadertoy)
            {
                chromeBrowser = new ChromiumWebBrowser(String.Empty);
                chromeBrowser.LoadHtml(path);
            }
            else if(linkType == LinkType.yt)
            {
                chromeBrowser = new ChromiumWebBrowser(String.Empty);
                chromeBrowser.Load(path);
            }
            else
            {
                chromeBrowser = new ChromiumWebBrowser(path);
                if (linkType == LinkType.deviantart)
                {
                    chromeBrowser.DownloadHandler = new DownloadHandler();
                }

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
            Console.WriteLine(e.Message);
        }

        private void ChromeBrowser_TitleChanged(object sender, TitleChangedEventArgs e)
        {
            mainForm.Invoke((MethodInvoker)(() => mainForm.Text = e.Title));
        }

        private void ChromeBrowser_LoadingStateChanged(object sender, LoadingStateChangedEventArgs e)
        {
            if (WidgetData.liveyPropertiesData == null || e.IsLoading)
            {
                return;
            }
            RestoreLivelyPropertySettings();
        }

        private static void RestoreLivelyPropertySettings()
        {
            try
            {
                if (chromeBrowser.CanExecuteJavascriptInMainFrame) //if js context ready
                {
                    foreach (var item in WidgetData.liveyPropertiesData)
                    {
                        string uiElementType = item.Value["type"].ToString();
                        if (!uiElementType.Equals("button", StringComparison.OrdinalIgnoreCase) && !uiElementType.Equals("label", StringComparison.OrdinalIgnoreCase))
                        {
                            if (uiElementType.Equals("dropdown", StringComparison.OrdinalIgnoreCase))
                            {
                                Form1.chromeBrowser.ExecuteScriptAsync("livelyPropertyListener", item.Key, (int)item.Value["value"]);
                            }
                            else if (uiElementType.Equals("slider", StringComparison.OrdinalIgnoreCase))
                            {
                                Form1.chromeBrowser.ExecuteScriptAsync("livelyPropertyListener", item.Key, (double)item.Value["value"]);
                            }
                            else if (uiElementType.Equals("folderDropdown", StringComparison.OrdinalIgnoreCase))
                            {
                                var filePath = Path.Combine(Path.GetDirectoryName(htmlPath), item.Value["folder"].ToString(), item.Value["value"].ToString());
                                if (File.Exists(filePath))
                                {
                                    Form1.chromeBrowser.ExecuteScriptAsync("livelyPropertyListener",
                                    item.Key,
                                    Path.Combine(item.Value["folder"].ToString(), item.Value["value"].ToString()));
                                }
                                else
                                {
                                    Form1.chromeBrowser.ExecuteScriptAsync("livelyPropertyListener",
                                    item.Key,
                                    null); //or custom msg
                                }
                            }
                            else if (uiElementType.Equals("checkbox", StringComparison.OrdinalIgnoreCase))
                            {
                                Form1.chromeBrowser.ExecuteScriptAsync("livelyPropertyListener", item.Key, (bool)item.Value["value"]);
                            }
                            else if (uiElementType.Equals("color", StringComparison.OrdinalIgnoreCase) || uiElementType.Equals("textbox", StringComparison.OrdinalIgnoreCase))
                            {
                                Form1.chromeBrowser.ExecuteScriptAsync("livelyPropertyListener", item.Key, (string)item.Value["value"]);
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
            Console.WriteLine("HWND" + chromeBrowser.GetBrowser().GetHost().GetWindowHandle());
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

        /// <summary>
        /// Converts shadertoy.com url to embed link: fullscreen, muted audio.
        /// </summary>
        /// <param name="shadertoylink"></param>
        /// <returns>shadertoy embed url</returns>
        private string ShadertoyURLtoEmbedLink(string shadertoylink)
        {
            if (!shadertoylink.Contains("https://"))
                shadertoylink = "https://" + path;

            shadertoylink = shadertoylink.Replace("view/", "embed/");

            string text = @"<!DOCTYPE html><html lang=""en"" dir=""ltr""> <head> <meta charset=""utf - 8""> 
                    <title>Digital Brain</title> <style media=""screen""> iframe { position: fixed; width: 100%; height: 100%; top: 0; right: 0; bottom: 0;
                    left: 0; z-index; -1; pointer-events: none;  } </style> </head> <body> <iframe width=""640"" height=""360"" frameborder=""0"" 
                    src=" + shadertoylink + @"?gui=false&t=10&paused=false&muted=true""></iframe> </body></html>";
            // WriteAllText creates a file, writes the specified string to the file,
            // and then closes the file.    You do NOT need to call Flush() or Close().
            //System.IO.File.WriteAllText(AppDomain.CurrentDomain.BaseDirectory + @"\\shadertoy_url.html", text);
            return text;
        }

        #endregion //cef

        #region cef audio

        private void Timer_Tick1(object sender, EventArgs e)
        {
            try
            {
                if (chromeBrowser.CanExecuteJavascriptInMainFrame) //if js context ready
                {
                    if (enableCSCore)
                    {
                        var fftBuffer = new float[(int)fftSize];
                        fftBuffer = _lineSpectrum.livelyGetSystemAudioSpectrum();
                        if(fftBuffer != null)
                            ExecuteScriptAsync("livelyAudioListener", fftBuffer);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Audio-timer error: " + ex.Message);
            }
        }

        //original ref: https://github.com/cefsharp/CefSharp/pull/1372/files
        /// <summary>
        /// Modified for passing array to js.
        /// </summary>
        void ExecuteScriptAsync(string methodName, float[] args)
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

        public void DebugSpectrum()
        {
            var fftBuffer = new float[(int)fftSize];
            if (spectrumProvider.GetFftData(fftBuffer, this))
            {
                // System.Diagnostics.Debug.WriteLine(fftBuffer);
                foreach (var item in fftBuffer)
                {
                    System.Diagnostics.Debug.Write(item * 10 + " ");
                }
                System.Diagnostics.Debug.WriteLine("End");
            }
        }

        #endregion //cef audio

        #region cscore
        private void CSCoreInit()
        {
            //StopCSCore();
            //open the default device 
            _soundIn = new WasapiLoopbackCapture(100, new WaveFormat(48000, 24, 2));

            //Our loopback capture opens the default render device by default so the following is not needed
            //_soundIn.Device = MMDeviceEnumerator.DefaultAudioEndpoint(DataFlow.Render, Role.Console);
            _soundIn.Initialize();

            var soundInSource = new SoundInSource(_soundIn);
            ISampleSource source = soundInSource.ToSampleSource().AppendSource(x => new PitchShifter(x), out _pitchShifter);

            SetupSampleSource(source);

            // We need to read from our source otherwise SingleBlockRead is never called and our spectrum provider is not populated
            byte[] buffer = new byte[_source.WaveFormat.BytesPerSecond / 2];
            soundInSource.DataAvailable += (s, aEvent) =>
            {
                int read;
                while ((read = _source.Read(buffer, 0, buffer.Length)) > 0) ;
            };


            //play the audio
            _soundIn.Start();
        }

        BasicSpectrumProvider spectrumProvider;
        const FftSize fftSize = FftSize.Fft128; //128 sample values, higher values heavy idle cpu usage.
        /// <summary>
        /// 
        /// </summary>
        /// <param name="aSampleSource"></param>
        private void SetupSampleSource(ISampleSource aSampleSource)
        {
            //create a spectrum provider which provides fft data based on some input            
            spectrumProvider = new BasicSpectrumProvider(aSampleSource.WaveFormat.Channels,
                aSampleSource.WaveFormat.SampleRate, fftSize);


            //linespectrum and voiceprint3dspectrum used for rendering some fft data
            //in oder to get some fft data, set the previously created spectrumprovider 
            _lineSpectrum = new LineSpectrum(fftSize)
            {
                SpectrumProvider = spectrumProvider,
                UseAverage = true,
                BarCount = 128,
                BarSpacing = 2,
                IsXLogScale = true,
                ScalingStrategy = ScalingStrategy.Sqrt,
                MaximumFrequency = 20000,
                MinimumFrequency = 20,
                
            };
            /*
            _voicePrint3DSpectrum = new VoicePrint3DSpectrum(fftSize)
            {
                SpectrumProvider = spectrumProvider,
                UseAverage = true,
                PointCount = 200,
                IsXLogScale = true,
                ScalingStrategy = ScalingStrategy.Sqrt
            };
            */

            //the SingleBlockNotificationStream is used to intercept the played samples
            var notificationSource = new SingleBlockNotificationStream(aSampleSource);
            //pass the intercepted samples as input data to the spectrumprovider (which will calculate a fft based on them)
            notificationSource.SingleBlockRead += (s, a) => spectrumProvider.Add(a.Left, a.Right);

            _source = notificationSource.ToWaveSource(16);

        }

        private static void StopTimer()
        {
            if (wasapiAudioTimer != null)
                wasapiAudioTimer.Stop();
        }

        private void StopCSCore()
        {

            if (_soundOut != null)
            {
                _soundOut.Stop();
                _soundOut.Dispose();
                _soundOut = null;
            }
            if (_soundIn != null)
            {
                _soundIn.Stop();
                _soundIn.Dispose();
                _soundIn = null;
            }
            if (_source != null)
            {
                _source.Dispose();
                _source = null;
            }

            if (_lineSpectrum != null)
            {
                _lineSpectrum = null;
            }
        }

        #endregion //cscore

        #region window

        private void Form1_Load(object sender, EventArgs e)
        {

        }

        private void Form1_FormClosing_1(object sender, FormClosingEventArgs e)
        {
            if (enableCSCore)
            {
                StopCSCore(); 
            }

            if(enableSysInfo)
            {
                hwMonitor.HWMonitor -= Instance_HWMonitor;
                hwMonitor.StopService();
            }
        }

        private void Form1_Shown(object sender, EventArgs e)
        {
            this.Hide();
        }

        #endregion //window

        #region helpers

        //ref: https://stackoverflow.com/questions/39777659/extract-the-video-id-from-youtube-url-in-net
        private static string GetYouTubeVideoIdFromUrl(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
            {
                try
                {
                    uri = new UriBuilder("http", url).Uri;
                }
                catch
                {
                    // invalid url
                    return "";
                }
            }

            string host = uri.Host;
            string[] youTubeHosts = { "www.youtube.com", "youtube.com", "youtu.be", "www.youtu.be" };
            if (!youTubeHosts.Contains(host))
                return "";

            var query = HttpUtility.ParseQueryString(uri.Query);
            if (query.AllKeys.Contains("v"))
            {
                return Regex.Match(query["v"], @"^[a-zA-Z0-9_-]{11}$").Value;
            }
            else if (query.AllKeys.Contains("u"))
            {
                // some urls have something like "u=/watch?v=AAAAAAAAA16"
                return Regex.Match(query["u"], @"/watch\?v=([a-zA-Z0-9_-]{11})").Groups[1].Value;
            }
            else
            {
                // remove a trailing forward space
                var last = uri.Segments.Last().Replace("/", "");
                if (Regex.IsMatch(last, @"^v=[a-zA-Z0-9_-]{11}$"))
                    return last.Replace("v=", "");

                string[] segments = uri.Segments;
                if (segments.Length > 2 && segments[segments.Length - 2] != "v/" && segments[segments.Length - 2] != "watch/")
                    return "";

                return Regex.Match(last, @"^[a-zA-Z0-9_-]{11}$").Value;
            }
        }

        #endregion //helpers

    }
}
