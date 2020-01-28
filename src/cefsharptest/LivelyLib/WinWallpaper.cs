using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using cefsharptest.Win32;

namespace cefsharptest.LivelyLib
{
    public static class WinWallpaper
    {
        /// <summary>
        /// Retrieves current wallpaper path, including name.
        /// </summary>
        /// <returns>
        /// Wallpaper path string.
        /// </returns>
        public static string GetWallpaperImagePath()
        {
            const int MAX_PATH = 260;
            RegistryKey currentMachine = Registry.CurrentUser;
            RegistryKey controlPanel = currentMachine.OpenSubKey("Control Panel");
            RegistryKey desktop = controlPanel.OpenSubKey("Desktop");

            string filePath = Convert.ToString(desktop.GetValue("WallPaper"));

            controlPanel.Close();

            if (!System.IO.File.Exists(filePath))
            {
                filePath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\Microsoft\\Windows\\Themes\\CachedFiles";
                if (Directory.Exists(filePath))
                {
                    string[] filePaths = Directory.GetFiles(filePath);
                    if (filePaths.Length > 0)
                    {
                        filePath = filePaths[0];
                    }
                }
                else
                {
                    RegistryKey regKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Internet Explorer\\Desktop\\General\\", false);
                    filePath = regKey.GetValue("WallpaperSource").ToString() + "h";
                    if (!System.IO.File.Exists(filePath))
                    {
                        filePath = new String('\0', MAX_PATH);
                        NativeMethods.SystemParametersInfo((uint)NativeMethods.SPI.SPI_GETDESKWALLPAPER, (UInt32)filePath.Length, filePath, 0);
                        filePath = filePath.Substring(0, filePath.IndexOf('\0'));
                    }
                }
            }

            desktop.Close();
            currentMachine.Close();

            if (System.IO.File.Exists(filePath))
            {
                return filePath;
            }
            else
            {
                //"Failed to retrieve wallpaper image using all methods!");
                return null;
            }

        }

        public static string GetPathOfWallpaper()
        {
            StringBuilder sb = new StringBuilder(500);
            if (!NativeMethods.SystemParametersInfo((uint)NativeMethods.SPI.SPI_GETDESKWALLPAPER, (uint)sb.Capacity, sb, NativeMethods.SPIF.None))
                return "";

            return sb.ToString();
        }
    }
}
