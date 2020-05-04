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
            //Simple check.
            //checks if mutex is up, if it is not up, continue execution
            //as per this solution https://stackoverflow.com/a/6392264
            Mutex mutex = new System.Threading.Mutex(false, "LIVELY:DESKTOPWALLPAPERSYSTEM");
            try
            {
                if (mutex.WaitOne(0, false))
                {
                    MessageBox.Show("Lively is not running, Exiting!", "Cef: Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Environment.Exit(1); //msgloop not ready 
                }
            }
            finally
            {
                if (mutex != null)
                {
                    mutex.Close();
                    mutex = null;
                }
            }

            //deleting old CEF logfile.
            try
            {
                File.Delete(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug.log"));
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
