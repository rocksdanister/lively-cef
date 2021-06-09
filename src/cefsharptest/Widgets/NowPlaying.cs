using System;

namespace livelywpf.Helpers
{
    public class NowPlayingEventArgs : EventArgs
    {
        /// <summary>
        /// Song title.
        /// </summary>
        public string Title { get; set; }
        /// <summary>
        /// Song artist.
        /// </summary>
        public string Artist { get; set; }
    }
}
