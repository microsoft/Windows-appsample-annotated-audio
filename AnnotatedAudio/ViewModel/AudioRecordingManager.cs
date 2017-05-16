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
using System.Threading.Tasks;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Storage;
using Windows.Media.Devices;
using AnnotatedAudio.Model;
using Windows.UI.Xaml;

namespace AnnotatedAudio.ViewModel
{
    public class AudioRecordingManager : BindableBase
    {
        private const string RECORD_LOG_NAME = "RecordLog";

        private MediaCapture _mediaCapture;
        private LowLagMediaRecording _lowLagMediaRecording;
        private TimeSpan _accumulatedTrackRecordingTime = new TimeSpan();
        private DateTime _recordingResumed;
        private DispatcherTimer _timer = new DispatcherTimer() { Interval = TimeSpan.FromSeconds(1) };
        
        public bool IsRecording
        {
            get => _isRecording;
            private set
            {
                SetProperty(ref _isRecording, value);
                OnPropertyChanged(nameof(CurrentTrackRecordingTimeFormatted));
            } 
        }
        private bool _isRecording = false;

        
        public bool IsPaused
        {
            get => _isPaused;
            private set => SetProperty(ref _isPaused, value);
        }
        private bool _isPaused = false;

        public TimeSpan CurrentTrackRecordingTime
        {
            get => DateTime.Now.Subtract(_recordingResumed).Add(_accumulatedTrackRecordingTime);
        }
        private TimeSpan _currentTrackRecordingTime = new TimeSpan();

        public string CurrentTrackRecordingTimeFormatted
        {
            get => (IsRecording ? $"Recording... " : "") + _currentTrackRecordingTimeFormatted;
            set => SetProperty(ref _currentTrackRecordingTimeFormatted, value);
        }
        private string _currentTrackRecordingTimeFormatted = "";

        private void Timer_Tick(object sender, object e)
        {
            CurrentTrackRecordingTimeFormatted =
                        String.Format("{0:D2}:{1:D2}:{2:D2}",
                        CurrentTrackRecordingTime.Hours,
                        CurrentTrackRecordingTime.Minutes,
                        CurrentTrackRecordingTime.Seconds);
        }

        public int CurrentlyRecordingTrackNumber
        {
            get
            {
                return _currentlyRecordingTrackNumber;
            }
            set
            {
                SetProperty(ref _currentlyRecordingTrackNumber, value, "CurrentlyRecordingTrackNumber");
            }
        }
        private int _currentlyRecordingTrackNumber = 0;

        public static async Task<AudioRecordingManager> CreateAsync()
        {
            var manager = new AudioRecordingManager();
            await manager.InitializeAsync();
            return manager;
        }

        private async Task InitializeAsync()
        {
            this.LogMessage(RECORD_LOG_NAME, "InitializeAsync() called.");

            var settings = new MediaCaptureInitializationSettings
            {
                StreamingCaptureMode = StreamingCaptureMode.Audio
            };

            try
            {
                _mediaCapture = new MediaCapture();
                await _mediaCapture.InitializeAsync(settings);

                _mediaCapture.Failed += MediaCapture_Failed;
                _mediaCapture.RecordLimitationExceeded += MediaCapture_RecordLimitationExceeded;
            }
            catch(Exception ex)
            {
                this.LogMessage(RECORD_LOG_NAME, "InitalizeAsync() failed." + ex.Message);
            }

            _timer.Tick += Timer_Tick;
        }

        private void MediaCapture_RecordLimitationExceeded(MediaCapture sender)
        {
            this.LogMessage(RECORD_LOG_NAME, "RecordLimitationExceeded");
            throw new NotImplementedException();
        }

        private void MediaCapture_Failed(MediaCapture sender, MediaCaptureFailedEventArgs errorEventArgs)
        {
            this.LogMessage(RECORD_LOG_NAME, "MediaCapture_Failed");
            throw new NotImplementedException();
        }

        public Session CurrentSession { get;  set; }

        public async Task<bool> Record()
        {
            this.LogMessage(RECORD_LOG_NAME, "Record() called.");

            if (IsRecording)
            {
                return IsPaused ? await ResumeRecording() : false;
            }
            else
            {
                return (await StartNewRecording()) ? (IsRecording = true && true) : false;
            }
        }

        public async Task<bool> Pause()
        {
            this.LogMessage(RECORD_LOG_NAME, "Pause() called.");

            if (IsRecording == false || IsPaused == true)
            {
                return false;
            }

            if(_mediaCapture == null || _lowLagMediaRecording == null)
            {
                return false;
            }

            var pauseResult = await _lowLagMediaRecording.PauseWithResultAsync(MediaCapturePauseBehavior.ReleaseHardwareResources);
            var logThisInfo = pauseResult.RecordDuration;

            IsPaused = true;

            _accumulatedTrackRecordingTime.Add(CurrentTrackRecordingTime);

            _timer.Stop();

            return true;

        }

        public async Task<bool> Stop()
        {
            this.LogMessage(RECORD_LOG_NAME, "Stop() called.");

            if (IsRecording == false)
            {
                return false;
            }

            if (_mediaCapture == null || _lowLagMediaRecording == null)
            {
                return false;
            }

            var stopResult = await _lowLagMediaRecording.StopWithResultAsync();
            var logThisInfo = stopResult.RecordDuration;

            await _lowLagMediaRecording.FinishAsync();
            IsRecording = false;

            _accumulatedTrackRecordingTime = new TimeSpan();

            _timer.Stop();

            return true;
        }

        private async Task<bool> ResumeRecording()
        {
            if (IsRecording == false || IsPaused == false)
            {
                this.LogMessage(RECORD_LOG_NAME, "ResumeRecording() failed because IsRecording or IsPaused is false.");
                return false;
            }

            if (_mediaCapture == null || _lowLagMediaRecording == null)
            {
                this.LogMessage(RECORD_LOG_NAME, "ResumeRecording() failed because MediaCapture or LowLagMediaRecording is null.");
                return false;
            }

            await _lowLagMediaRecording.ResumeAsync();
            _recordingResumed = DateTime.Now;
            _timer.Start();

            return true;
        }

        private async Task<bool> StartNewRecording()
        {
            CurrentlyRecordingTrackNumber = CurrentSession.Segments;

            var segmentFilename = $"{CurrentSession.Id}_{CurrentlyRecordingTrackNumber}.mp4";

            var localFolder = Windows.Storage.ApplicationData.Current.LocalFolder;

            StorageFile file = null;
            try
            {
                var sessionFolder = await localFolder.GetFolderAsync(CurrentSession.Id.ToString());
                file = await sessionFolder.CreateFileAsync(segmentFilename, CreationCollisionOption.FailIfExists);
            }
            catch(Exception e)
            {
                this.LogMessage(RECORD_LOG_NAME, $"StartNewRecording() failed because Exception at CreateFileAsync. {e.Message}");
                return false;
            }

            try
            {
                _lowLagMediaRecording = await _mediaCapture.PrepareLowLagRecordToStorageFileAsync(
                    MediaEncodingProfile.CreateM4a(AudioEncodingQuality.Auto), file);
            }
            catch(Exception)
            {
                return false;
            }
            
            await _lowLagMediaRecording.StartAsync();

            _recordingResumed = DateTime.Now;
            _currentTrackRecordingTime = TimeSpan.Zero;

            _timer.Start();

            this.LogMessage(RECORD_LOG_NAME, "StartNewRecording. Filename: " + segmentFilename);

            CurrentSession.Segments++;
            return true;
        }
    }
}
