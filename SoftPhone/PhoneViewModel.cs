using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using PJ.SoftPhoneSdk;
using PJ.SoftPhoneSdk.PjPhone;

namespace SoftPhone
{
    public class PhoneViewModel : BaseViewModel, IDisposable
    {
        private readonly IBrush _red = new ImmutableSolidColorBrush(Colors.Red);
        private readonly IBrush _green = new ImmutableSolidColorBrush(Colors.Green);
        private readonly IBrush _white = new ImmutableSolidColorBrush(Colors.White);

        private const string PickUp = "接听";
        private const string HangUp = "挂断";
        private const string Call = "呼叫";

        private IPhone? _phone;
        private string? _lastCall;
        private bool _isBusy;

        public PhoneViewModel(PhoneProfile profile)
        {
            Profile = profile;
            NumberCommand = new RelayCommand<string>(NumberClick);
            CloseOrOpenCommand = new RelayCommand(CloseOrOpen);
            ActionCommand = new RelayCommand(Action);
            InitPhone();
        }

        void InitPhone()
        {
            ActionLabel = Call;
            IsOnline = false;
            IsLogging = true;
            IsRecordingEnabled = Profile.Record;
            _phone = new PhoneApp();
            _phone.OnCallConnected += _phone_OnCallConnected;
            _phone.OnCallHangup += _phone_OnCallHangup;
            _phone.OnIncomingCall += _phone_OnIncomingCall;
            _phone.OnRegistrationStateChanged += _phone_OnRegistrationStateChanged;
            _phone.Login(Profile.Server!, Profile.Port!.Value, Profile.Number!, Profile.Password!);
            if (Profile.Record)
            {
                _phone.SetRecordingFileDir($"./recording/{Profile.Number}");
            }
        }

        public PhoneProfile Profile { get; }

        public void UpdateProfile(PhoneProfile profile)
        {
            Profile.Name = profile.Name;
            Profile.Number = profile.Number;
            Profile.Password = profile.Password;
            Profile.Server = profile.Server;
            Profile.Port = profile.Port;
            Dispose();
            InitPhone();
        }

        public IPhone SourcePhone => _phone!;

        #region props

        public bool IsOnline
        {
            get => Get<bool>();
            set => Set(value);
        }

        public string? LineStatus
        {
            get => Get<string>();
            set => Set(value);
        }

        public string? PhoneStatus
        {
            get => Get<string>();
            set => Set(value);
        }

        public string? ActionLabel
        {
            get => Get<string>();
            set => Set(value);
        }

        public IBrush? PhoneStatusColor
        {
            get => Get<IBrush>();
            set => Set(value);
        }

        public bool IsLogging
        {
            get => Get<bool>();
            set => Set(value);
        }

        public bool IsRobot
        {
            get => Get<bool>();
            set => Set(value);
        }

        public bool IsRecordingEnabled
        {
            get => Get<bool>();
            set => Set(value);
        }

        public int Duration
        {
            get => Get<int>();
            set => Set(value);
        }


        #endregion

        #region NumberClick

        public ICommand NumberCommand { get; }

        void NumberClick(string? number)
        {
            PhoneStatus += number;
        }

        #endregion

        #region Dispose

        public void Dispose()
        {
            if (_phone != null)
            {
                _phone.OnCallConnected -= _phone_OnCallConnected;
                _phone.OnCallHangup -= _phone_OnCallHangup;
                _phone.OnIncomingCall -= _phone_OnIncomingCall;
                _phone.OnRegistrationStateChanged -= _phone_OnRegistrationStateChanged;

            }

            _phone?.Dispose();
            _phone = null;
        }

        #endregion

        #region CloseOrOpenCommand

        public ICommand CloseOrOpenCommand { get; }

        void CloseOrOpen()
        {
            if (ActionLabel == HangUp) return;
            IsOnline = !IsOnline;
            if (IsOnline)
            {
                InitPhone();
            }
            else
            {
                Dispose();
            }
        }

        #endregion

        #region ActionCommand

        public ICommand ActionCommand { get; }

        void Action()
        {
            switch (ActionLabel)
            {
                case HangUp:
                    _phone?.Hangup();
                    PhoneStatus = "";
                    ActionLabel = Call;
                    PhoneStatusColor = _white;
                    break;
                case Call:
                    if (!string.IsNullOrWhiteSpace(PhoneStatus))
                    {
                        _lastCall = PhoneStatus;
                        _phone?.Call(PhoneStatus);
                        ActionLabel = HangUp;
                        PhoneStatusColor = _red;
                        Duration = 0;
                        _isBusy = true;
                        StartDuration();
                        return;
                    }

                    PhoneStatus = _lastCall;

                    break;
                case PickUp:
                    _phone?.Pickup();
                    break;
            }
        }

        #endregion

        #region Duration

        async void StartDuration()
        {
            try
            {
                while (_isBusy)
                {
                    await Task.Delay(1000);
                    if (_isBusy) Duration += 1;
                }
            }
            catch
            {
                // ignored
            }
        }

        #endregion

        #region phone event

        private void _phone_OnRegistrationStateChanged(bool isOnline, string reason)
        {
            IsLogging = false;
            IsOnline = isOnline;
            LineStatus = reason;
            _isBusy = false;
            Duration = 0;
        }

        private void _phone_OnIncomingCall(string callerNumber)
        {
            ActionLabel = PickUp;
            PhoneStatus = callerNumber;
            PhoneStatusColor = _red;
            Duration = 0;
            _isBusy = true;
            StartDuration();
        }

        private void _phone_OnCallHangup()
        {
            ActionLabel = Call;
            PhoneStatus = "";
            PhoneStatusColor = _white;
            _isBusy = false;
            Duration = 0;
        }

        private void _phone_OnCallConnected()
        {
            ActionLabel = HangUp;
            PhoneStatusColor = _green;
            try
            {
                if (Profile.AutoPlay == null) return;
                if (File.Exists(Profile.AutoPlay))
                {
                    _phone?.Play(Profile.AutoPlay);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"播放录音出错:{ex.Message}");
            }
        }

        #endregion
    }
}
