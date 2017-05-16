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

using AnnotatedAudio.ViewModel;
using System;
using System.Threading.Tasks;
using Windows.Security.Authentication.Web.Core;
using Windows.Security.Credentials;
using Windows.Storage;
using Windows.UI.ApplicationSettings;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using System.Diagnostics;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace AnnotatedAudio
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private OneDriveManager OdManager = new OneDriveManager();

        public MainPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            Frame rootFrame = Window.Current.Content as Frame;

            if (rootFrame != null && rootFrame.CanGoBack)
            {
                // Show UI in title bar if opted-in and in-app backstack is not empty.
                SystemNavigationManager.GetForCurrentView().AppViewBackButtonVisibility =
                    AppViewBackButtonVisibility.Visible;
            }
            else
            {
                // Remove the UI from the title bar if in-app back stack is empty.
                SystemNavigationManager.GetForCurrentView().AppViewBackButtonVisibility =
                    AppViewBackButtonVisibility.Collapsed;
            }

            AccountsSettingsPane.GetForCurrentView().AccountCommandsRequested += BuildPaneAsync;
        }

        private async void Session_Click(object sender, RoutedEventArgs e)
        {
            string silentToken = await GetMsaTokenSilentlyAsync();

            if (silentToken != null)
            {
                OdManager.Token = silentToken;
                this.Frame.Navigate(typeof(AnnotatedAudio.View.SessionPicker), OdManager);
            }
            else
            {
                Debug.WriteLine("accountsettingspane show");
                AccountsSettingsPane.Show();
            }
        }


        private async Task<string> GetMsaTokenSilentlyAsync()
        {
            // Recall the provider Id and account Id from the app's storage
            string providerId = ApplicationData.Current.RoamingSettings.Values["CurrentUserProviderId"]?.ToString();
            string accountId = ApplicationData.Current.RoamingSettings.Values["CurrentUserId"]?.ToString();

            if (null == providerId || null == accountId)
            {
                return null;
            }

            WebAccountProvider provider = await WebAuthenticationCoreManager.FindAccountProviderAsync(providerId);
            WebAccount account = await WebAuthenticationCoreManager.FindAccountAsync(provider, accountId);
            
            if (account == null)
            {
                return null;
            }

            var request = new WebTokenRequest(provider, "wl.basic");

            // We already have the web account, so we can call GetTokenSilentlyAsync instead of RequestTokenAsync.
            WebTokenRequestResult result = await WebAuthenticationCoreManager.GetTokenSilentlyAsync(request, account);

            // Unable to get a token silently - you'll need to show the UI
            if (result.ResponseStatus == WebTokenRequestStatus.UserInteractionRequired)
            {
                
                return null;
            }
            // Success
            else if (result.ResponseStatus == WebTokenRequestStatus.Success)
            {
                return result.ResponseData[0].Token;
            }
            // Other error 
            else
            {
                return null;
            }
        }

        private async void BuildPaneAsync(AccountsSettingsPane s, AccountsSettingsPaneCommandsRequestedEventArgs e)
        {
            var deferral = e.GetDeferral();
            Debug.WriteLine("building accounts settings pane");
            var msaProvider = await WebAuthenticationCoreManager.FindAccountProviderAsync("https://login.microsoft.com", "consumers");
            var command = new WebAccountProviderCommand(msaProvider, RequestNewMsaTokenAsync);
            e.WebAccountProviderCommands.Add(command);

            deferral.Complete();
        }

        private async void RequestNewMsaTokenAsync(WebAccountProviderCommand command)
        {
            WebTokenRequest request = new WebTokenRequest(command.WebAccountProvider, "wl.signin, wl.basic, onedrive.readwrite");
            WebTokenRequestResult result = await WebAuthenticationCoreManager.RequestTokenAsync(request);

            if (result.ResponseStatus == WebTokenRequestStatus.Success)
            {
                Debug.WriteLine("token request response is success");
                string token = result.ResponseData[0].Token;
                WebAccount account = result.ResponseData[0].WebAccount;

                ApplicationData.Current.RoamingSettings.Values["CurrentUserProviderId"] = account.WebAccountProvider.Id;
                ApplicationData.Current.RoamingSettings.Values["CurrentUserId"] = account.Id;

                OdManager.Token = token;

                this.Frame.Navigate(typeof(AnnotatedAudio.View.SessionPicker), OdManager);
            }
            else
            {
                this.ShowMessage("There was an error logging you in");
            }
        }
    }
}
