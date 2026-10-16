using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using PJ.SoftPhoneSdk.PjPhone;
using SoftPhone.Automation;

namespace SoftPhone
{
    public partial class MainWindow : Window
    {
        public const string ProfileDir = "profiles";

        public MainWindow()
        {
            InitializeComponent();
            PhoneApp.Init(!AppConfig.Instance.NoSoundDevice);
        }

        public IEnumerable<PhoneProfile> Profiles
        {
            get
            {
                foreach (var child in PhoneViewContainer.Children)
                {
                    if (child is PhoneView { Profile: not null } pv)
                    {
                        yield return pv.Profile;
                    }
                }
            }
        }

        public PhoneViewModel? GetPhone(string number)
        {
            foreach (var child in PhoneViewContainer.Children)
            {
                if (child is PhoneView { DataContext: PhoneViewModel pvm } && pvm.Profile.Number == number)
                {
                    return pvm;
                }
            }

            return null;
        }

        #region load

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            if (!Directory.Exists(ProfileDir)) Directory.CreateDirectory(ProfileDir);
            var allProfiles = Directory
                .GetFiles(ProfileDir, "*.json")
                .OrderBy(Path.GetFileNameWithoutExtension);
            foreach (var file in allProfiles)
            {
                LoadProfile(file);
            }

            RestoreSize();
        }

        async void RestoreSize()
        {
            try
            {
                var hasSet = false;
                try
                {
                    var state = Path.Combine(ProfileDir, ".state");
                    var lines = await File.ReadAllLinesAsync(state);
                    if (lines.Length == 0) return;
                    var wh = lines[0].Split(',');
                    if (!double.TryParse(wh[0], out var w) ||
                        !double.TryParse(wh[1], out var h)) return;
                    Width = w;
                    Height = h;
                    hasSet = true;
                }
                catch
                {
                    // ignored
                }
                finally
                {
                    if (!hasSet) SizeToContent = SizeToContent.WidthAndHeight;
                }
            }
            catch
            {
                // ignored
            }
        }

        void LoadProfile(string file)
        {
            try
            {
                var profile = JsonSerializer.Deserialize<PhoneProfile>(File.ReadAllText(file));
                if (profile == null) return;

                var phoneView = new PhoneView(profile);
                phoneView.OnRemoveRequest += Pv_OnRemoveRequest;
                PhoneViewContainer.Children.Add(phoneView);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"加载电话配置{file}出错:{ex.Message}");
            }
        }

        #endregion

        #region 增加

        private void AddNew_OnClick(object? sender, RoutedEventArgs e)
        {
            new EditProfile(profile =>
            {
                var pv = new PhoneView(profile);
                pv.OnRemoveRequest += Pv_OnRemoveRequest;
                PhoneViewContainer.Children.Add(pv);
            }).ShowDialog(this);
        }

        private void Pv_OnRemoveRequest(object? sender, EventArgs e)
        {
            if (sender is not PhoneView pv) return;
            var profileFile = Path.Combine(ProfileDir, $"{pv.Profile!.Server}@{pv.Profile.Number}.json");
            File.Delete(profileFile);
            PhoneViewContainer.Children.Remove(pv);
        }

        #endregion

        #region 清空

        private async void CleanAll_OnClick(object? sender, RoutedEventArgs e)
        {
            var box = MessageBoxManager
                .GetMessageBoxStandard("确认",
                    "确定要清空所有话机配置吗？",
                    ButtonEnum.YesNo,
                    MsBox.Avalonia.Enums.Icon.Question);
            var result = await box.ShowAsync();
            if (result == ButtonResult.No) return;
            Directory.Delete(ProfileDir, true);
            Directory.CreateDirectory(ProfileDir);
            foreach (var child in PhoneViewContainer.Children)
            {
                if (child is PhoneView pv)
                {
                    pv.Dispose();
                }
            }

            PhoneViewContainer.Children.Clear();
        }

        #endregion

        #region 退出

        private void Exit_OnClick(object? sender, RoutedEventArgs e)
        {
            Close();
        }

        #endregion

        #region 设置

        private void Setting_OnClick(object? sender, RoutedEventArgs e)
        {
            new Setting().ShowDialog(this);
        }

        #endregion

        #region 自动化

        private void AutoCall_OnClick(object? sender, RoutedEventArgs e)
        {
            new AutoCallWindow().ShowDialog(this);
        }

        private AutomationManager? _callManager;
        private AutomationManager? _pickManager;

        private void StartAutomation_OnClick(object? sender, RoutedEventArgs e)
        {
            StartAutomationMenuItem.IsVisible = false;
            _callManager = new AutomationManager("Call", true, this);
            _pickManager = new AutomationManager("Pickup", false, this);
            _pickManager.Start();
            _callManager.Start();
            StopAutomationMenuItem.IsVisible = true;
        }

        private void StopAutomation_OnClick(object? sender, RoutedEventArgs e)
        {
            StopAutomationMenuItem.IsVisible = false;
            _callManager?.Stop();
            _pickManager?.Stop();
            _callManager = null;
            _pickManager = null;
            StartAutomationMenuItem.IsVisible = true;
        }

        #endregion

        private void About_OnClick(object? sender, RoutedEventArgs e)
        {
            new AboutWindow().ShowDialog(this);
        }

        private async void Window_OnClosing(object? sender, WindowClosingEventArgs e)
        {
            try
            {
                if (Design.IsDesignMode) return;
                var ss = $"{Width},{Height}";
                var state = Path.Combine(ProfileDir, ".state");
                await File.WriteAllTextAsync(state, ss);
            }
            catch
            {
                // ignored
            }
        }
    }
}