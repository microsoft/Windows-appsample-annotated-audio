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
using AnnotatedAudio.ViewModel;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.UI.Input.Inking;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=234238

namespace AnnotatedAudio.View
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class SessionViewer : Page
    {
        public SessionViewModel ViewModel { get; set; } = new SessionViewModel();

        public SessionViewer()
        {
            this.InitializeComponent();

            var items = new List<OptionItem>() {
                new OptionItem { Icon = Symbol.Add, Name = "Add Page", OnClicked = async () =>
                                await ViewModel.AddPage.Execute() },
                new OptionItem { Icon = Symbol.Delete, Name = "Delete Page", OnClicked = async () =>
                                await ViewModel.DeletePage.Execute() },
                new OptionItem { Icon = Symbol.Delete, Name = "Delete Page", OnClicked = async () =>
                                await ViewModel.DeletePage.Execute() }
            };

            HamburgerMenu.OptionsItemsSource = items;

            ink.InkPresenter.InputDeviceTypes =
                Windows.UI.Core.CoreInputDeviceTypes.Mouse |
                Windows.UI.Core.CoreInputDeviceTypes.Pen |
                Windows.UI.Core.CoreInputDeviceTypes.Touch;
        }

        protected async override void OnNavigatedTo(NavigationEventArgs e)
        {
            await ViewModel.Setup((SessionPickerViewModel)e.Parameter, mediaPlayerElement, ink, inkSettings);

            ViewModel.StringInput += async (s) => await Utils.ShowTextboxPrompt
                (this, "Create a new Page", "Please enter a name for your new page.", "Create", "Cancel");

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
        }

        private async void HamburgerMenu_ItemClick(object sender, ItemClickEventArgs e)
        {
            ViewModel.CurrentPage = (SessionPage)e.ClickedItem;
            await ViewModel.PageSelected.Execute();
        }

        private async void HamburgerMenu_OptionsItemClick(object sender, ItemClickEventArgs e)
        {
            await ((OptionItem)e.ClickedItem).OnClicked();
        }
    }
}
