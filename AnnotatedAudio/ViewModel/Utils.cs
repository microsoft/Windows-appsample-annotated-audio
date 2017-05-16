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

using AnnotatedAudio.View;
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.UI.Input.Inking;

namespace AnnotatedAudio.ViewModel
{
    /// <summary>
    /// This class contains a set of utility extension functions that are used throughout the app.
    /// </summary>
    public static class Utils
    {
        /// <summary>
        /// Generates a unique hashcode for InkStroke based on the stroke points.
        /// </summary>
        public static int CustomHashCode(this InkStroke stroke)
        {
            int code = 0;

            foreach (var point in stroke.GetInkPoints().Select(i => i.Position))
            {
                code += (int)(point.X / 10);
                code += (int)(point.Y / 10);
            }

            return code;
        }

        /// <summary>
        /// Shows a generic informational message to the user.
        /// </summary>
        public static async void ShowMessage(this Windows.UI.Xaml.Controls.Page parent, string message)
        {
            var dialog = new Windows.UI.Popups.MessageDialog(message);
            var result = await dialog.ShowAsync();
        } 

        /// <summary>
        /// Shows a generic prompt for user input.
        /// </summary>
        public static async Task<string> ShowTextboxPrompt
            (this Windows.UI.Xaml.Controls.Page parent, string title,
            string message, string primaryText, string secondaryText)
        {
            var dialog = new UserPrompt(primaryText, secondaryText, title, message, true);
            var result = await dialog.ShowAsync();
            return result == Windows.UI.Xaml.Controls.ContentDialogResult.Primary ? dialog.Input : null;
        }

        public static async Task<Windows.UI.Xaml.Controls.ContentDialogResult> ShowConfirmPrompt
            (this Windows.UI.Xaml.Controls.Page parent, string title,
            string message, string primaryText, string secondaryText)
        {
            var dialog = new UserPrompt(primaryText, secondaryText, title, message, false);
            var result = await dialog.ShowAsync();
            return result;
        }

        /// <summary>
        /// Generic logging method that saves a log message to a file with the given name.
        /// </summary>
        public static async void LogMessage(this object obj, string name, string message)
        {
            string log = $"{DateTime.Now}::{obj.ToString()}::{message}\r\n";
            // Create sample file; replace if exists.
            var storageFolder =
                Windows.Storage.ApplicationData.Current.LocalFolder;
            var file =
                await storageFolder.CreateFileAsync(name + ".txt",
                    Windows.Storage.CreationCollisionOption.OpenIfExists);


            Windows.Storage.CachedFileManager.DeferUpdates(file);

            try
            {
                await Windows.Storage.FileIO.AppendTextAsync(file, log);

                var status = await Windows.Storage.CachedFileManager.CompleteUpdatesAsync(file);

                if (status == Windows.Storage.Provider.FileUpdateStatus.Complete)
                {
                    System.Diagnostics.Debug.WriteLine($"Successfully logged: {log} to {file.Path}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Unsuccessfully logged: {log} to {file.Path}");
                }
            }
            catch(Exception)
            {
                System.Diagnostics.Debug.WriteLine($"The log file is read-only right now, check if it's opened by another process.");
            }
           

        }

        public static async Task<T> RunTaskAsync<T>(this CoreDispatcher dispatcher,
        Func<Task<T>> func, CoreDispatcherPriority priority = CoreDispatcherPriority.Normal)
        {
            var taskCompletionSource = new TaskCompletionSource<T>();
            await dispatcher.RunAsync(priority, async () =>
            {
                try
                {
                    taskCompletionSource.SetResult(await func());
                }
                catch (Exception ex)
                {
                    taskCompletionSource.SetException(ex);
                }
            });
            return await taskCompletionSource.Task;
        }

        // There is no TaskCompletionSource<void> so we use a bool that we throw away.
        public static async Task RunTaskAsync(this CoreDispatcher dispatcher,
            Func<Task> func, CoreDispatcherPriority priority = CoreDispatcherPriority.Normal) =>
            await RunTaskAsync(dispatcher, async () => { await func(); return false; }, priority);
    }
}
