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
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;

namespace AnnotatedAudio.ViewModel
{
    public class SessionPickerViewModel : BindableBase
    {
        public OneDriveManager OneDriveManager { get; set; }
        public Func<Task<Windows.UI.Xaml.Controls.ContentDialogResult>> ConfirmDeletion { get; set; }
        public Func<string, Task<string>> StringInput { get; set; }
        public EventHandler<object> SessionStarted { get; set; }
        public ObservableCollection<Session> Sessions { get; set; } = new ObservableCollection<Session>();

        public Session SelectedSession
        {
            get => (Session)SelectedItem;
            set => SelectedItem = value;
        }
        public object SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (SetProperty(ref _selectedItem, value))
                {
                    OnPropertyChanged(nameof(SelectedSession));
                    var ignoreResult = StartSession.Execute();
                }
            }
        }
        private object _selectedItem;

        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        private string _status;

        public string StatusDetails
        {
            get => _statusDetails;
            set => SetProperty(ref _statusDetails, value);
        }
        private string _statusDetails;

        public DelegateCommand CreateSession
        {
            get
            {
                return _createSession ?? (_createSession = new DelegateCommand(
                    async () =>
                    {
                        var input = await this.StringInput("Please name your new session.");

                        if (input != null && input != "")
                        {
                            Session session = new Session() { Id = Guid.NewGuid(), Name = input, Created = DateTime.Now, Segments = 0 };
                            Sessions.Add(session);
                            SelectedSession = session;

                            await SaveSessionList();
                        }
                    }));
            }
        }
        private DelegateCommand _createSession;

        public DelegateCommand<Guid> DeleteSession
        {
            get
            {
                return _deleteSession ?? (_deleteSession = new DelegateCommand<Guid>(
                    async (id) =>
                    {
                        var result = await ConfirmDeletion();

                        // If the user selected the primary button: "Delete"
                        if (result == Windows.UI.Xaml.Controls.ContentDialogResult.Primary)
                        {
                            var target = Sessions.Where(s => s.Id == id).FirstOrDefault();
                            int targetIndex = Sessions.IndexOf(target);
                            Sessions.Remove(target);

                            // Clean up
                            try
                            {
                                var storageFolder = Windows.Storage.ApplicationData.Current.LocalFolder;

                                var folder = await storageFolder.GetFolderAsync($"{id}");
                                await folder.DeleteAsync();

                                var file = await storageFolder.GetFileAsync($"{id}.zip");
                                await file.DeleteAsync();
                            }
                            catch (Exception)
                            {
                                // Do nothing because there is nothing to clean up.
                            }
                            

                            await SaveSessionList();
                            await OneDriveManager.DeleteFile($"{id}.zip");
                        }

                    }));

            }
        }
        private DelegateCommand<Guid> _deleteSession;

        public DelegateCommand StartSession
        {
            get
            {
                return _startSession ?? (_startSession = new DelegateCommand(
                    async () =>
                    {
                        Status = "Loading your session...";
                        StatusDetails = "Please wait while we load your session...";

                        var selectedSession = ((Session)SelectedSession);

                        var response = await OneDriveManager.DownloadFile($"{selectedSession?.Id}.zip");

                        // If the Session wasn't found on OneDrive, it's a new session, so create a folder.
                        if (!response)
                        {
                            var cd = Windows.Storage.ApplicationData.Current.LocalFolder;
                            try
                            {
                                cd = await cd.CreateFolderAsync(SelectedSession.Id.ToString());
                            }
                            catch (Exception)
                            {
                                // Folder already exists, do nothing.
                            }
                        }

                        try
                        {
                            await SelectedSession.DecompressSession(true);
                            SessionStarted?.Invoke(this, SelectedSession);
                        }
                        catch (IOException)
                        {
                            SessionStarted?.Invoke(this, SelectedSession);
                        }
                        catch (Exception ex)
                        {
                            Status = "Uh-oh :(";
                            StatusDetails = "It looks like there was an error, please tap Start to try again. " +
                                            "If you keep getting this message try sharing the following with your tech-savvy friend.\n"
                                            + ex.Message;
                        }


                    }));

            }
        }
        private DelegateCommand _startSession;

        public async Task Setup(OneDriveManager manager)
        {
            OneDriveManager = manager;

            Status = "Downloading your sessions";
            StatusDetails = "Please wait while your data rains from your cloud...";

            // Download AnnotatedAudio/SessionIds.json from OneDrive and store it 
            // in Windows.Storage.ApplicationData.Current.LocalFolder. If it doesn't exist, do nothing.
            var response = await OneDriveManager.DownloadFile("SessionList.json");

            if (!response)
            {
                Status = "Looks like you are new";
                StatusDetails = "We couldn't find any sessions you created using your Microsoft account. " +
                                "You can create a new session for recording by tapping Create. " +
                                "After it appears in the list below, you can start recording and taking notes " +
                                "by selecting it and tapping Start";
                return;
            }

            try
            {
                Windows.Storage.StorageFolder storageFolder = Windows.Storage.ApplicationData.Current.LocalFolder;

                Windows.Storage.StorageFile metaFile =
                    await storageFolder.GetFileAsync($"SessionList.json");

                var buffer = (await Windows.Storage.FileIO.ReadBufferAsync(metaFile)).ToArray();

                using (var stream = new MemoryStream(buffer))
                {
                    Session[] ids = new Session[0];
                    ids = (Session[])new DataContractJsonSerializer(ids.GetType()).ReadObject(stream);
                    ids.ToList().ForEach(s =>Sessions.Add(new Session()
                        { Id = s.Id, Name = s.Name, Created = s.Created, Segments = s.Segments }));

                    if (ids.Count() <= 0)
                    {
                        Status = "You don't have anything yet";
                        StatusDetails = "We couldn't find any sessions you created using your Microsoft account. " +
                                        "You can create a new session to start recording and taking notes.";
                        return;
                    }

                    Status = "Your recorded sessions";
                    StatusDetails = "You can create a new session for recording, " +
                                    "view and modify an existing session, or delete one.";

                }
            }
            catch (FileNotFoundException)
            {
                Status = "Uh-oh :(";
                StatusDetails = "It looks like there was an error, please tap the back button and try again.";
            }
        }

        public async Task SaveSessionList()
        {
            byte[] arr;
            using (var datastream = new MemoryStream())
            {
                var settings = new DataContractJsonSerializerSettings();
                settings.UseSimpleDictionaryFormat = true;
                new DataContractJsonSerializer(typeof(ObservableCollection<Session>), settings).WriteObject(datastream, Sessions);
                arr = datastream.ToArray();
            }

            // Save async -- save as timestamp.gif
            // Prevent updates to the file until updates are 
            // finalized with call to CompleteUpdatesAsync.
            var fileName = $"SessionList.json";

            // Create sample file; replace if exists.
            var storageFolder =
                Windows.Storage.ApplicationData.Current.LocalFolder;
            var file =
                await storageFolder.CreateFileAsync(fileName,
                    Windows.Storage.CreationCollisionOption.ReplaceExisting);

            Windows.Storage.CachedFileManager.DeferUpdates(file);

            await Windows.Storage.FileIO.WriteBytesAsync(file, arr);

            // Finalize write so other apps can update file.
            var status =
                await Windows.Storage.CachedFileManager.CompleteUpdatesAsync(file);

            if (status == Windows.Storage.Provider.FileUpdateStatus.Complete)
            {
                arr.LogMessage("sessions", "Successfully saved session metadata");

                await OneDriveManager.UploadFile(file, (p) => System.Diagnostics.Debug.WriteLine(p));
            }
            else
            {
                arr.LogMessage("sessions", "Unsuccessfully saved session metadata");
            }
        }
    }
}
