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

using AnnotatedAudio.Model;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Input.Inking;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace AnnotatedAudio.ViewModel
{
    public class SessionViewModel : BindableBase
    {
        public ObservableCollection<SessionPage> Pages { get; } = new ObservableCollection<SessionPage>();
        public Func<string, Task<string>> StringInput { get; set; }
        public SessionPickerViewModel SessionPicker { get; set; }
        public InkCanvas Canvas { get; set; }
        public InkToolbar CanvasToolbar { get; set; }

        private InkStrokeContainer _container = new InkStrokeContainer();
        private ObservableCollection<InkEvent> _events = new ObservableCollection<InkEvent>();
        private Dictionary<int, InkStroke> _strokes = new Dictionary<int, InkStroke>();

        public AudioPlaybackManager PlaybackManager
        {
            get => _playbackManager;
            set => SetProperty(ref _playbackManager, value);
        }
        private AudioPlaybackManager _playbackManager;

        public AudioRecordingManager RecordingManager
        {
            get => _recordingManager;
            set
            {
                if (SetProperty(ref _recordingManager, value))
                {
                    Record.RaiseCanExecuteChanged();
                    Stop.RaiseCanExecuteChanged();
                }
            }
        }
        private AudioRecordingManager _recordingManager;

        public SolidColorBrush RecordingButtonForeground
        {
            get => RecordingManager != null && RecordingManager.IsRecording ? new SolidColorBrush(Colors.Red) : new SolidColorBrush(Colors.Black);
        }

        public object CurrentPage
        {
            get => _currentPage;
            set
            {
                if (SetProperty(ref _currentPage, value))
                {
                    DeletePage.RaiseCanExecuteChanged();
                }
            }
        }
        private object _currentPage;

        public DelegateCommand PageSelected
        {
            get
            {
                return _pageSelected ?? (_pageSelected = new DelegateCommand(
                    async () =>
                    {
                        await RefreshInkAsync();
                    }));

            }
        }
        private DelegateCommand _pageSelected;

        public DelegateCommand AddPage
        {
            get
            {
                return _addPage ?? (_addPage = new DelegateCommand(
                    async () =>
                    {
                        var input = await this.StringInput("Please name your new session.");

                        if (input != null && input != "")
                        {
                            SessionPage page = new SessionPage();
                            page.PageName = input;

                            page.PageId = Pages.Count;
                            Pages.Add(page);
                            CurrentPage = page;
                            await PageSelected.Execute();
                        }
                    }));

            }
        }
        private DelegateCommand _addPage;

        public DelegateCommand DeletePage
        {
            get
            {
                return _deletePage ?? (_deletePage = new DelegateCommand(
                    async () =>
                    {
                        // If there's only one page, don't delete it.
                        if (Pages.Count > 1)
                        {
                            var target = (SessionPage)CurrentPage;
                            int targetIndex = Pages.IndexOf(target);
                            Pages.Remove(target);

                            // Calculate the new selected index.
                            int newIndex = Math.Max(Math.Min(targetIndex, Pages.Count - 1), 0);

                            // Attempt to set the SelectedSession.
                            try
                            {
                                CurrentPage = Pages[newIndex];
                                await PageSelected.Execute();
                            }
                            catch (ArgumentOutOfRangeException)
                            {
                                ; // Do nothing, because the list is empty.
                            }
                        }
                    }, () => CurrentPage != null));

            }
        }
        private DelegateCommand _deletePage;

        public DelegateCommand Record
        {
            get
            {
                return _record ?? (_record = new DelegateCommand(
                    async () =>
                    {
                        if (!RecordingManager.IsRecording)
                        {
                            await RecordingManager.Record();
                            Record.RaiseCanExecuteChanged();
                            Stop.RaiseCanExecuteChanged();
                            UpdateInkingMode();
                            OnPropertyChanged(nameof(RecordingButtonForeground));
                        }

                    }, () => RecordingManager != null && !RecordingManager.IsRecording));

            }
        }
        private DelegateCommand _record;


        public DelegateCommand Stop
        {
            get
            {
                return _stop ?? (_stop = new DelegateCommand(
                    async () =>
                    {
                        if (RecordingManager.IsRecording)
                        {
                            await RecordingManager.Stop();
                            await SessionPicker.SaveSessionList();
                            Record.RaiseCanExecuteChanged();
                            Stop.RaiseCanExecuteChanged();

                            UpdateInkingMode();
                            OnPropertyChanged(nameof(RecordingButtonForeground));

                            // Re-load the audio and cues for playback, because a new track was recorded.
                            await PlaybackManager.LoadAudio();
                            PlaybackManager.LoadAudioCues(_events.Where(f => f.GetType() == (new InkEvent()).GetType()).Select(f => f as InkEvent).ToList());

                            // Compress session and upload to OneDrive.
                            await SessionPicker.SelectedSession.CompressSession(false);
                            await SessionPicker.OneDriveManager.UploadFile(
                                await Windows.Storage.ApplicationData.Current.LocalFolder.GetFileAsync($"{SessionPicker.SelectedSession.Id.ToString()}.zip"), null);
                        }

                    }, () => RecordingManager != null && RecordingManager.IsRecording));

            }
        }
        private DelegateCommand _stop;

        private void UpdateInkingMode()
        {
            if (!RecordingManager.IsRecording)
            {
                Canvas.InkPresenter.InputProcessingConfiguration.Mode = InkInputProcessingMode.None;
                CanvasToolbar.ActiveTool = null;
                CanvasToolbar.IsEnabled = false;
            }
            else
            {
                Canvas.InkPresenter.InputProcessingConfiguration.Mode = InkInputProcessingMode.Inking;
                CanvasToolbar.IsEnabled = true;
                CanvasToolbar.ActiveTool = CanvasToolbar.GetToolButton(InkToolbarTool.BallpointPen);
            }
        }

        public async Task Setup(SessionPickerViewModel SessionPicker, MediaPlayerElement media, InkCanvas ink, InkToolbar inkSettings)
        {
            this.SessionPicker = SessionPicker;
            Canvas = ink;
            CanvasToolbar = inkSettings;

            // Load pages and attach an event handler.
            await LoadPagesAsync();
            Pages.CollectionChanged += async (s, e) => await SaveList<SessionPage>(Pages, "PageMeta.json");

            // Create a new "Introduction" page if the list is empty.
            if (Pages.Count <= 0)
            {
                Pages.Add(new SessionPage() { PageId = 0, PageName = "Introduction" });
            }

            CurrentPage = Pages.FirstOrDefault();

            // Load ink strokes and attach an event handler.
            await LoadInkAsync();
            _events.CollectionChanged += async (s, e) => await SaveList<InkEvent>(_events, "InkMeta.json");

            // Load Audio manager.
            RecordingManager = await AudioRecordingManager.CreateAsync();
            RecordingManager.CurrentSession = SessionPicker.SelectedSession;
            PlaybackManager = new AudioPlaybackManager(media);
            PlaybackManager.CurrentSession = SessionPicker.SelectedSession;

            // Load playback manager with existing audio.
            await PlaybackManager.LoadAudio();
            PlaybackManager.PlaybackPositionChanged += async (s, ev) =>
            {
                await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, 
                    () => SetStrokeHighlighting(ev.Time, PlaybackManager.CurrentTrack));
            };

            // Load audio cues and the event handler.
            PlaybackManager.LoadAudioCues(_events.Where(f => f.GetType() == (new InkEvent()).GetType()).Select(f => f as InkEvent).ToList());
            PlaybackManager.PlaybackCueTriggered += SetPlaybackToStrokeTime;

            // Attach event handler for when an ink stroke is tapped in review mode.
            Canvas.InkPresenter.UnprocessedInput.PointerPressed += SelectInkStroke;
;
            // Attach event handler for dynamically saving strokes added
            Canvas.InkPresenter.StrokesCollected += CollectInkStroke;

            // Write mode must be toggled after load to ensure that writing is not enabled before recording has been activated.
            UpdateInkingMode();
        }

        private async void SetPlaybackToStrokeTime(object sender, PlaybackCueTriggeredEventArgs ev)
        {
            await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
            {
                // Get the last stroke that was made in the current track between playback poisition and 00:00:00.
                var lastStroke = _events.Where(e => e.SegmentId == ev.Segment && e.TimeHappened <= ev.Time)
                .OrderBy(e => e.TimeHappened)
                .LastOrDefault();

                // Automatically switch pages, if the ink event above triggered in a different page.
                if (CurrentPage != null & ((SessionPage)CurrentPage).PageId != lastStroke.PageId)
                {
                    CurrentPage = Pages.Where(p => p.PageId == lastStroke.PageId).FirstOrDefault();
                    await PageSelected.Execute();
                }
                else
                    SetStrokeHighlighting(ev.Time, ev.Segment);

            });
        }

        private void SelectInkStroke(InkUnprocessedInput ink, PointerEventArgs e)
        {
            var start = e.CurrentPoint.Position;
            start.X -= 7;
            start.Y -= 7;

            var end = e.CurrentPoint.Position;
            end.X += 7;
            end.Y += 7;

            ink.InkPresenter.StrokeContainer.SelectWithLine(start, end);

            var stroke = ink.InkPresenter.StrokeContainer.GetStrokes().Where(str => str.Selected).FirstOrDefault();

            // If a stroke wasn't found within 7 pixels of a click, try within 14 instead.
            if (stroke == null)
            {
                start.X += 14;
                end.X -= 14;
                ink.InkPresenter.StrokeContainer.SelectWithLine(start, end);
                stroke = ink.InkPresenter.StrokeContainer.GetStrokes().Where(str => str.Selected).FirstOrDefault();
            }

            // If a stroke was found within 7 or 14 pixels, set the selected stroke and start playing audio from the point the stroke was made.
            if (stroke != null)
            {
                stroke.Selected = false;
                var evt = _events.Where(i => i.GetType() == typeof(InkEvent) && ((InkEvent)i).Id == stroke.CustomHashCode()).FirstOrDefault();
                PlaybackManager.Play(evt.TimeHappened, (uint)evt.SegmentId);
                SetStrokeHighlighting(evt.TimeHappened, evt.SegmentId);
            }
        }

        private async void CollectInkStroke(object sender, InkStrokesCollectedEventArgs e)
        {
            InkEvent inkEvent = new InkEvent();
            var stroke = e.Strokes.First().Clone();
            _container.Clear();
            _container.AddStroke(stroke);

            System.Diagnostics.Debug.WriteLine(Windows.Storage.ApplicationData.Current.LocalFolder.Path);

            // Save async -- save as timestamp.gif
            // Prevent updates to the file until updates are 
            // finalized with call to CompleteUpdatesAsync.
            string fileName = $"stroke{_events.Count}.gif";

            // Create sample file; replace if exists.
            Windows.Storage.StorageFile file =
                await(await SessionPicker.SelectedSession.GetFolder()).CreateFileAsync(fileName,
                    Windows.Storage.CreationCollisionOption.ReplaceExisting);

            Windows.Storage.CachedFileManager.DeferUpdates(file);
            // Open a file stream for writing.
            using (IRandomAccessStream stream = await file.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite))
            {
                // Write the ink strokes to the output stream.
                using (IOutputStream outputStream = stream.GetOutputStreamAt(0))
                {
                    await _container.SaveAsync(outputStream);
                    await outputStream.FlushAsync();
                }
            }

            // Finalize write so other apps can update file.
            Windows.Storage.Provider.FileUpdateStatus status =
                await Windows.Storage.CachedFileManager.CompleteUpdatesAsync(file);

            if (status == Windows.Storage.Provider.FileUpdateStatus.Complete)
            {
                inkEvent.TimeHappened = RecordingManager.CurrentTrackRecordingTime;
                inkEvent.FileName = file.Name;
                inkEvent.Id = stroke.CustomHashCode();
                inkEvent.SegmentId = RecordingManager.CurrentlyRecordingTrackNumber;
                inkEvent.PageId = ((SessionPage)CurrentPage).PageId;
                inkEvent.LogMessage("inking", "Successfully saved an inking file.");
            }
            else
            {
                inkEvent.LogMessage("inking", "Unsuccessfully saved an inking file.");
            }

            _events.Add(inkEvent);
        }

        private void SetStrokeHighlighting(TimeSpan pivotTime, int pivotTrack)
        {
            var strokesToUpdate = Canvas.InkPresenter.StrokeContainer.GetStrokes();
            foreach (InkStroke update in strokesToUpdate)
            {
                foreach (var i in _events)
                {
                    if (i.GetType() == (new InkEvent()).GetType())
                    {
                        if (update.CustomHashCode() == (i as InkEvent).Id)
                        {
                            InkDrawingAttributes updateAttr = update.DrawingAttributes;
                            int diff = TimeSpan.Compare(i.TimeHappened, pivotTime);
                            if (pivotTrack > i.SegmentId)
                            {
                                updateAttr.Color = Windows.UI.Colors.CornflowerBlue;
                                update.DrawingAttributes = updateAttr;
                            }

                            else if ((diff > 0) | (i.SegmentId > pivotTrack))
                            {
                                updateAttr.Color = Windows.UI.Colors.Black;
                                update.DrawingAttributes = updateAttr;
                            }

                            else
                            {
                                updateAttr.Color = Windows.UI.Colors.CornflowerBlue;
                                update.DrawingAttributes = updateAttr;
                            }
                        }
                    }
                }
            }
        }

        private async Task LoadPagesAsync()
        {
            try
            {
                var folder = await SessionPicker.SelectedSession.GetFolder();

                Windows.Storage.StorageFile sampleFile =
                    await folder.GetFileAsync($"PageMeta.json");

                var buffer = (await Windows.Storage.FileIO.ReadBufferAsync(sampleFile)).ToArray();


                using (var stream = new MemoryStream(buffer))
                {
                    SessionPage[] pages = new SessionPage[0];
                    pages = (SessionPage[])new DataContractJsonSerializer(pages.GetType()).ReadObject(stream);
                    pages.ToList<SessionPage>().ForEach(p => Pages.Add(p));
                }
            }
            catch (Exception)
            {

            }
        }

        private async Task RefreshInkAsync()
        {
            Canvas.InkPresenter.StrokeContainer.Clear();
            _strokes.Clear();

            foreach (var i in _events.Where(e => CurrentPage != null && e.PageId == ((SessionPage)CurrentPage).PageId))
            {
                var folder = await SessionPicker.SelectedSession.GetFolder();

                // If the event is an ink event, we need to load it.
                if (i.GetType() == (new InkEvent()).GetType())
                {
                    Windows.Storage.StorageFile file =
                        await folder.GetFileAsync($"{(i as InkEvent).FileName}");

                    using (var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.Read))
                    {
                        using (var inputStream = stream.GetInputStreamAt(0))
                        {
                            await _container.LoadAsync(inputStream);
                            var stroke = _container.GetStrokes().First().Clone();
                            Canvas.InkPresenter.StrokeContainer.AddStroke(stroke);

                            _strokes.Add(stroke.CustomHashCode(), stroke);
                        }
                    }
                }
            }

            SetStrokeHighlighting(PlaybackManager.PlaybackPosition, PlaybackManager.CurrentTrack);
        }

        private async Task LoadInkAsync()
        {
            try
            {
                var folder = await SessionPicker.SelectedSession.GetFolder();

                Windows.Storage.StorageFile sampleFile =
                    await folder.GetFileAsync($"InkMeta.json");

                var buffer = (await Windows.Storage.FileIO.ReadBufferAsync(sampleFile)).ToArray();


                using (var stream = new MemoryStream(buffer))
                {
                    InkEvent[] events = new InkEvent[0];
                    events = (InkEvent[])new DataContractJsonSerializer(events.GetType()).ReadObject(stream);
                    _events = new ObservableCollection<InkEvent>(events);
                }

                await RefreshInkAsync();
            }
            catch (Exception)
            {
                return;
            }
        }

        private async Task SaveList<T>(ObservableCollection<T> list, string fileName)
        {
            byte[] arr;
            using (var datastream = new MemoryStream())
            {
                var settings = new DataContractJsonSerializerSettings();
                settings.UseSimpleDictionaryFormat = true;
                new DataContractJsonSerializer(list.GetType(), settings).WriteObject(datastream, list);
                arr = datastream.ToArray();
            }

            var folder = await SessionPicker.SelectedSession.GetFolder();

            // Create sample file; replace if exists.
            var file =
                await folder.CreateFileAsync(fileName,
                    Windows.Storage.CreationCollisionOption.ReplaceExisting);

            Windows.Storage.CachedFileManager.DeferUpdates(file);

            await Windows.Storage.FileIO.WriteBytesAsync(file, arr);

            // Finalize write so other apps can update file.
            var status =
                await Windows.Storage.CachedFileManager.CompleteUpdatesAsync(file);

            if (status == Windows.Storage.Provider.FileUpdateStatus.Complete)
            {
                arr.LogMessage("inking", "Successfully saved to the inking metadata");
            }
            else
            {
                arr.LogMessage("inking", "Unsuccessfully saved to the inking metadata");
            }
        }

    }
}
