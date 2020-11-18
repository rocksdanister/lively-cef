using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace cefsharptest.Widgets
{
    public class LauncherData
    {
        private string app;
        public LauncherData(string app)
        {
            this.app = app;
        }
        public void handleLauncherAction(string msg)
        {
            Debug.WriteLine("Launcher action invoked");

            if (msg == "app1")
            {
                ProcessStartInfo start = new ProcessStartInfo
                {
                    FileName = app,
                    WorkingDirectory = Path.GetDirectoryName(app)
                };

                Process process = new Process
                {
                    StartInfo = start,
                    EnableRaisingEvents = true
                };

                process.Start();
            }
        }
    }
}
