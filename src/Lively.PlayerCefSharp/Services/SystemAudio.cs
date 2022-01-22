using CSCore;
using CSCore.DSP;
using CSCore.SoundIn;
using CSCore.SoundOut;
using CSCore.Streams;
using CSCore.Streams.Effects;
using LivelyCefSharp.Visualization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Lively.PlayerCefSharp.Services
{
    public class SystemAudio : IDisposable
    {
        private WasapiCapture _soundIn;
        private ISoundOut _soundOut;
        private IWaveSource _source;
        private LineSpectrum _lineSpectrum;
        private PitchShifter _pitchShifter;
        private readonly System.Windows.Forms.Timer wasapiAudioTimer;

        public event EventHandler<float[]> AudioData;

        public SystemAudio()
        {
            wasapiAudioTimer = new System.Windows.Forms.Timer
            {
                Interval = 33 //30fps
            };
            wasapiAudioTimer.Tick += AudioTimer;

            try
            {
                InitializeAudio();
            }
            catch(Exception)
            {
                //TODO
            }
        }

        private void InitializeAudio()
        {
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
        private bool disposedValue;
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

        private void AudioTimer(object sender, EventArgs e)
        {
            try
            {
                var fftBuffer = new float[(int)fftSize];
                fftBuffer = _lineSpectrum.livelyGetSystemAudioSpectrum();
                AudioData?.Invoke(this, fftBuffer);
            }
            catch (Exception)
            {
                //TODO
            }
        }

        public void Start()
        {
            wasapiAudioTimer?.Start();
        }

        public void Stop()
        {
            wasapiAudioTimer?.Stop();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    wasapiAudioTimer?.Stop();

                    _soundOut?.Stop();
                    _soundOut?.Dispose();
                    _soundOut = null;

                    _soundIn?.Stop();
                    _soundIn?.Dispose();
                    _soundIn = null;

                    _source?.Dispose();
                    _source = null;

                    _lineSpectrum = null;
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
        }

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~SystemAudio()
        // {
        //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
