using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Lively.PlayerCefSharp.Services
{
    public interface IAudioVisualizerService : IDisposable
    {
        event EventHandler<double[]> AudioDataAvailable;
        void Start();
        void Stop();
    }
}
