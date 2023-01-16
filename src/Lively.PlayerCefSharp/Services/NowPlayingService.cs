using Lively.PlayerCefSharp.API;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Windows.Media.Control;

namespace Lively.PlayerCefSharp.Services
{
    public sealed class NowPlayingService : INowPlayingService
    {
        public event EventHandler<NowPlayingModel> NowPlayingTrackChanged;
        private readonly Timer _timer; //to avoid GC

        public NowPlayingService()
        {
            _timer = new Timer(async (obj) => NowPlayingTrackChanged?.Invoke(this, await GetCurrentTrackInfo()), null, 2000, 1000);
        }

        public static async Task<NowPlayingModel> GetCurrentTrackInfo()
        {
            var result = new NowPlayingModel();
            try
            {
                var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                var session = manager.GetCurrentSession();
                if (session != null)
                {
                    var media = await session.TryGetMediaPropertiesAsync();
                    if (media != null)
                    {
                        string thumb = null;
                        if (media.Thumbnail != null)
                        {
                            using var ras = await media.Thumbnail.OpenReadAsync();
                            using var stream = ras.AsStream();
                            using var ms = new MemoryStream();
                            ms.Seek(0, SeekOrigin.Begin);
                            stream.CopyTo(ms);
                            var array = ms.ToArray();
                            thumb = Convert.ToBase64String(array);
                        }

                        result.AlbumArtist = media.AlbumArtist;
                        result.AlbumTitle = media.AlbumTitle;
                        result.AlbumTrackCount = media.AlbumTrackCount;
                        result.Artist = media.Artist;
                        result.Genres = media.Genres?.ToList();
                        result.PlaybackType = media.PlaybackType?.ToString();
                        result.Subtitle = media.Subtitle;
                        result.Thumbnail = thumb;
                        result.Title = media.Title;
                        result.TrackNumber = media.TrackNumber;
                    }
                }
            }
            catch (Exception ex)
            {
                Form1.WriteToParent(new LivelyMessageConsole()
                {
                    Category = ConsoleMessageType.error,
                    Message = $"Error ocurred in NowPlaying: {ex.Message}",
                });
            }
            return result;
        }
    }
}
