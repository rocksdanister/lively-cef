using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

namespace cefsharptest
{

    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            try
            {
                bool onlyInstance = false;
                Mutex mutex = new Mutex(true, "LIVELY:DESKTOPWALLPAPERSYSTEM", out onlyInstance);
                if (!onlyInstance)
                {

                }
                else
                {
                    MessageBox.Show("Lively is not running, Exiting!", "Cef: Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Environment.Exit(1); //msgloop not ready 
                }

            }
            catch (AbandonedMutexException e)
            {
                System.Diagnostics.Debug.WriteLine(e.Message);
            }

            try
            {
                //Deleting old CEF logfile if any.
                File.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Lively Wallpaper", "Cef", "logfile.txt"));
            }
            catch { }

            //cscore is culture sensitive?, fix?
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
        }
    }
}
