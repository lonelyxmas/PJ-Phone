using System.Diagnostics;
using System.Text.RegularExpressions;
using PJ.SoftPhoneSdk.Sip;

namespace PJ.SoftPhoneSdk.PjPhone;

/// <summary>
/// 基于IctBaden.pjsua的电话实现
/// </summary>
public class PhoneApp : IPhone
{
    #region static

    /// <summary>
    /// 初始化
    /// </summary>
    /// <param name="hasSoundDevice">设置当前设备是否有音频设备</param>
    /// <param name="logger">日志对象，如果不传或传NULL，将使用内部默认日志对象</param>
    public static void Init(bool hasSoundDevice, IPhoneLogger? logger = null)
    {
        SipPhone.Init(Thread.CurrentThread, !hasSoundDevice, logger);
    }

    internal static readonly Regex NumberRegex = new Regex("\"\\d+\"");

    #endregion

    private SipAccount? _account;
    private SipCall? _currentCall;

    #region IPhone

    /// <inheritdoc />
    public void Dispose()
    {
        if (_account == null) return;
        _account.OnRegistrationStateChanged -= _account_OnRegistrationStateChanged;
        _account.OnCallMissed -= _account_OnCallMissed;
        _account.OnCalling -= _account_OnCalling;
        _account.OnCallStateChanged -= _account_OnCallStateChanged;
        SipPhone.RemoveAccount(_account);
    }

    /// <inheritdoc />
    public void Login(string server, int port, string number, string password)
    {
        _account = SipPhone
            .AddSipAccount(number, password, server, port);
        _account.OnRegistrationStateChanged += _account_OnRegistrationStateChanged;
        _account.OnCallMissed += _account_OnCallMissed;
        _account.OnCalling += _account_OnCalling;
        _account.OnCallStateChanged += _account_OnCallStateChanged;
    }

    /// <inheritdoc />
    public void SetRecordingFileDir(string dir)
    {
        if (_account == null) throw new InvalidOperationException("必须先调用login方法");
        _account.SetRecordStoreDir(dir);
    }


    /// <inheritdoc />
    public string? Name
    {
        get => _account?.Name;
        set => _account?.SetDisplayName(value!);
    }

    /// <inheritdoc />
    public string? Call(string number)
    {
        return _account?.Call(number)?.RecordingFile;
    }

    /// <inheritdoc />
    public void Play(string audioFile)
    {
        _currentCall?.Play(audioFile);
    }

    /// <inheritdoc />
    public void Hangup()
    {
        _account?.Hangup();
        OnCallHangup?.Invoke();
    }

    /// <inheritdoc />
    public void Pickup()
    {
        _account?.Pickup();
    }

    /// <inheritdoc />
    public event RegistrationStateChanged? OnRegistrationStateChanged;

    /// <inheritdoc />
    public event IncomingCall? OnIncomingCall;

    /// <inheritdoc />
    public event CallConnected? OnCallConnected;

    /// <inheritdoc />
    public event CallHangup? OnCallHangup;

    #endregion

    #region event handler

    private void _account_OnCallStateChanged(object? sender, SipCall e)
    {
        Trace.WriteLine($"call {e.CallId} status -> {e.State}");
        switch (e.State)
        {
            case CallState.Connected:
                _currentCall = e;
                OnCallConnected?.Invoke();
                break;
            case CallState.DisConnected:
                _currentCall = null;
                OnCallHangup?.Invoke();
                break;
        }
    }

    private void _account_OnCalling(object? sender, SipCall e)
    {
        if (e.Direction != CallDirection.InComing) return;
        var number = NumberRegex.Match(e.getInfo().remoteUri).Value;
        OnIncomingCall?.Invoke(number.Replace("\"", ""));
    }

    private void _account_OnCallMissed(object? sender, SipCall e)
    {

    }

    private void _account_OnRegistrationStateChanged(object? sender, string e)
    {
        OnRegistrationStateChanged?
            .Invoke(_account!.IsRegistrationOk, _account.IsRegistrationOk ? "在线" : e);
    }

    #endregion
}