// Copyright © 2013 The CefSharp Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style license that can be found in the LICENSE file.

using System;
using System.Diagnostics;
using System.IO;

namespace CefSharp.Example.Handlers
{
    public class DownloadHandler : IDownloadHandler
    {
        public event EventHandler<DownloadItem> OnBeforeDownloadFired;

        public event EventHandler<DownloadItem> OnDownloadUpdatedFired;

        public bool CanDownload(IWebBrowser chromiumWebBrowser, IBrowser browser, string url, string requestMethod)
        {
            return true;
        }

        public void OnBeforeDownload(IWebBrowser chromiumWebBrowser, IBrowser browser, DownloadItem downloadItem, IBeforeDownloadCallback callback)
        {
            OnBeforeDownloadFired?.Invoke(this, downloadItem);

            if (!callback.IsDisposed)
            {
                using (callback)
                {
                    //callback.Continue(downloadItem.SuggestedFileName, showDialog: true);
                    string livelyDir = Path.Combine(Directory.GetParent(Directory.GetParent(Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory).ToString()).ToString()).ToString(), "tmpdata");
                    Directory.CreateDirectory(Path.Combine(livelyDir, "wpdownloads"));
                    livelyDir = Path.Combine(livelyDir, "wpdownloads");

                    callback.Continue( Path.Combine(livelyDir, downloadItem.SuggestedFileName), showDialog: false);
                }
            }
        }

        public void OnDownloadUpdated(IWebBrowser chromiumWebBrowser, IBrowser browser, DownloadItem downloadItem, IDownloadItemCallback callback)
        {
            OnDownloadUpdatedFired?.Invoke(this, downloadItem);
            //Debug.WriteLine(downloadItem.Id +" " + downloadItem.ReceivedBytes + " " +downloadItem.TotalBytes);
            if (downloadItem.IsCancelled)
            {

            }
            else if (downloadItem.IsComplete )
            {
                //Console.WriteLine("LOADWP" + downloadItem.FullPath);
                //System.Windows.Forms.MessageBox.Show(downloadItem.FullPath);
            }
            /*
            _bar.Dispatcher.Invoke(new Action(() => {
                Debug.Print("{0}/{1} bytes", downloadItem.ReceivedBytes, downloadItem.TotalBytes);

                _bar.Maximum = downloadItem.TotalBytes;
                _bar.Value = downloadItem.ReceivedBytes;
            }));
            */
        }
    }
}
