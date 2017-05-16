//  ---------------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// 
//  The MIT License (MIT)
// 
//  Permission is hereby granted, free of charge, to any person obtaining a copy
//  of this software and associated documentation files (the "Software"), to deal
//  in the Software without restriction, including without limitation the rights
//  to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//  copies of the Software, and to permit persons to whom the Software is
//  furnished to do so, subject to the following conditions:
// 
//  The above copyright notice and this permission notice shall be included in
//  all copies or substantial portions of the Software.
// 
//  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//  IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//  FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//  AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//  LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//  OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
//  THE SOFTWARE.
//  ---------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.UI.Xaml.Controls;
using AnnotatedAudio.Model;
using Windows.UI.Xaml;

namespace AnnotatedAudio.ViewModel
{
    public class PlaybackPositionChangedEventArgs : EventArgs
    {
        public TimeSpan Time { get; set; }
    }
    public class PlaybackCueTriggeredEventArgs : EventArgs
    {
        public TimeSpan Time { get; set; }
        public int EventId { get; set; }
        public int Segment { get; set; }
    }

    public class AudioPlaybackManager : BindableBase, IDisposable
    {
        private const string RECORD_LOG_NAME = "RecordLog";

        private MediaPlayer _mediaPlayer;
        private MediaPlaybackList _playbackList;
        private TimeSpan _newSegmentStartPosition = TimeSpan.Zero;

        public Session CurrentSession { get; set; }

        public Visibility MediaPlayerVisibility
        {
            get
            {
                return _mediaPlayerVisibility;
            }
            set
            {
                SetProperty(ref _mediaPlayerVisibility, value);
            }
        }
        private Visibility _mediaPlayerVisibility;

        private Dictionary<int, List<InkEvent>> EventDictionary = new Dictionary<int, List<InkEvent>>();

        public AudioPlaybackManager(MediaPlayerElement mediaPlayerElement)
        {
            _mediaPlayer = new MediaPlayer();
            mediaPlayerElement.SetMediaPlayer(_mediaPlayer);
            _mediaPlayer.PlaybackSession.SeekCompleted += PlaybackSession_SeekCompleted;
        }

        public int CurrentTrack => _playbackList == null ? 0 : (int)_playbackList.CurrentItemIndex;

        public TimeSpan PlaybackPosition => _mediaPlayer?.PlaybackSession.Position ?? new TimeSpan();

        public async Task<bool> LoadAudio()
        {
            // If there is no audio, collapse the MediaPlayer.
            if (CurrentSession.Segments <= 0)
            {
                MediaPlayerVisibility = Visibility.Collapsed;
                return false;
            }

            try
            {
                var localFolder = Windows.Storage.ApplicationData.Current.LocalFolder;
                var sessionFolder = await localFolder.GetFolderAsync(CurrentSession.Id.ToString());

                _playbackList = new MediaPlaybackList();

                for (var segment = 0; segment < CurrentSession.Segments; segment++)
                {
                    var file = await sessionFolder.GetFileAsync($"{CurrentSession.Id}_{segment}.mp4");

                    if (file == null)
                    {
                        this.LogMessage(RECORD_LOG_NAME, $"LoadAudio() failed because Exception at GetFileFromPathAsync. {file.Path}");
                    }

                    var source = MediaSource.CreateFromStorageFile(file);

                    TimedMetadataTrack metadataTrack = new TimedMetadataTrack(file.Path.ToString(), "en-us", TimedMetadataKind.Data);
                    metadataTrack.Label = "Custom data track";
                    metadataTrack.CueEntered += MetadataTrack_DataCueEntered;
                    source.ExternalTimedMetadataTracks.Add(metadataTrack);

                    var playbackItem = new MediaPlaybackItem(source);
                    playbackItem.TimedMetadataTracks.SetPresentationMode(0, TimedMetadataTrackPresentationMode.ApplicationPresented);
                    _playbackList.Items.Add(playbackItem);
                }

                _mediaPlayer.Source = _playbackList;

            }
            catch (Exception e)
            {
                this.LogMessage(RECORD_LOG_NAME, "LoadAudio() failed because Exception loading files: " + e.Message);

                return false;
            }

            return true;
        }

        public bool LoadAudioCues(List<InkEvent> events)
        {

            if (CurrentSession.Segments == 0)
            {
                this.LogMessage(RECORD_LOG_NAME, "LoadAudioCues() failed because No audio segments");
                return false;
            }

            if (_playbackList == null)
            {
                this.LogMessage(RECORD_LOG_NAME, "LoadAudioCues() failed because MediaPlaybackList is null.");
                return false;
            }

            foreach (var ev in events)
            {
                if (ev.SegmentId >= CurrentSession.Segments)
                {
                    this.LogMessage(RECORD_LOG_NAME, "LoadAudioCues() failed because event segment ID out of range: " + ev.SegmentId);
                }

                string data = "Cue data";
                byte[] bytes = new byte[data.Length];
                System.Buffer.BlockCopy(data.ToCharArray(), 0, bytes, 0, bytes.Length);
                IBuffer buffer = bytes.AsBuffer();

                var cue = new DataCue { Data = buffer, Id = ev.Id.ToString(), StartTime = ev.TimeHappened };
                var playbackItem = _playbackList.Items[ev.SegmentId];
                var track = playbackItem.TimedMetadataTracks[0];               

                try
                {
                    track.AddCue(cue);
                }
                catch (Exception e)
                {
                    this.LogMessage(RECORD_LOG_NAME, "LoadAudioCues() failed because Exception loading cue: " + e.Message);
                }
            }

            return true;
        }

        public void Play(TimeSpan playbackPosition, uint segmentIndex)
        {
            var playbackList = (MediaPlaybackList)_mediaPlayer.Source;
            var items = playbackList.Items;

            if (playbackList.CurrentItemIndex == segmentIndex)
            {
                _mediaPlayer.PlaybackSession.Position = playbackPosition;
                _mediaPlayer.Play();
            }
            else
            {
                _newSegmentStartPosition = playbackPosition;
                playbackList.CurrentItemChanged += PlaybackList_CurrentItemChanged;
                playbackList.MoveTo(segmentIndex);
            }
        }

        private void PlaybackList_CurrentItemChanged(MediaPlaybackList sender, CurrentMediaPlaybackItemChangedEventArgs args)
        {
            _mediaPlayer.PlaybackSession.Position = _newSegmentStartPosition;
            _newSegmentStartPosition = TimeSpan.Zero;

            var playbackList = _mediaPlayer.Source as MediaPlaybackList;
            playbackList.CurrentItemChanged -= PlaybackList_CurrentItemChanged;

            if (_mediaPlayer.PlaybackSession.PlaybackState != MediaPlaybackState.Playing)
            {
                _mediaPlayer.Play();
            }
        }

        private void MetadataTrack_DataCueEntered(TimedMetadataTrack sender, MediaCueEventArgs args)
        {
            var playbackList = _mediaPlayer.Source as MediaPlaybackList;
            DataCue cue = (DataCue)args.Cue;

            OnPlaybackCueTriggered(new PlaybackCueTriggeredEventArgs()
            {
                Segment = (int)playbackList.CurrentItemIndex,
                EventId = Convert.ToInt32(cue.Id),
                Time = cue.StartTime
            });
        }

        public event EventHandler<PlaybackCueTriggeredEventArgs> PlaybackCueTriggered;

        protected virtual void OnPlaybackCueTriggered(PlaybackCueTriggeredEventArgs e)
        {
            PlaybackCueTriggered?.Invoke(this, e);
        }

        private void PlaybackSession_SeekCompleted(MediaPlaybackSession sender, object args)
        {
            OnPlaybackPositionChanged(new PlaybackPositionChangedEventArgs()
            { Time = _mediaPlayer.PlaybackSession.Position });
        }

        public event EventHandler<PlaybackPositionChangedEventArgs> PlaybackPositionChanged;

        protected virtual void OnPlaybackPositionChanged(PlaybackPositionChangedEventArgs e) =>
            PlaybackPositionChanged?.Invoke(this, e);

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                _mediaPlayer.Dispose();
                _mediaPlayer = null;

                disposedValue = true;
            }
        }

        ~AudioPlaybackManager()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
             GC.SuppressFinalize(this);
        }
        #endregion
    }
}