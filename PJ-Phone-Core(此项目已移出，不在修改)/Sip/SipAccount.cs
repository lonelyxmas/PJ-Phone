using pj;

namespace PJ_Phone_Core.Sip;

/// <summary>
/// SIP账号
/// </summary>
public class SipAccount : Account
{
    #region 内部变量

    private readonly object _lock = new object();
    private readonly string _name;
    private readonly string _password;
    private readonly string _host;
    private readonly int _port;
    private SipCall? _lastCall;
    private string? _soundStoreDir;

    #endregion

    #region 事件

    /// <summary>
    /// 注册状态发生变更
    /// </summary>
    public event EventHandler<string>? OnRegistrationStateChanged;

    /// <summary>
    /// 当产生通话时发生
    /// </summary>
    public event EventHandler<SipCall>? OnCalling;

    /// <summary>
    /// 呼叫状态发生变更
    /// </summary>
    public event EventHandler<SipCall>? OnCallStateChanged;

    /// <summary>
    /// 来电漏接，在本地忙的情况下，来电被自动拒接时发生，此时 SipCall 已经挂断
    /// </summary>
    public event EventHandler<SipCall>? OnCallMissed;

    #endregion

    #region 方法

    /// <summary>
    /// 新建一个SIP账号
    /// </summary>
    /// <param name="name">登录名</param>
    /// <param name="password">密码</param>
    /// <param name="host">服务器地址</param>
    /// <param name="port">服务器端口</param>
    public SipAccount(string name, string password, string host, int port)
    {
        Name = _name = name;
        _password = password;
        _host = host;
        _port = port;
    }

    /// <summary>
    /// 设置录音文件存放目录，会自动在下面按照日期建立子目录
    /// </summary>
    /// <param name="dir">目录地址</param>
    public SipAccount SetRecordStoreDir(string dir)
    {
        if (!Directory.Exists(dir)) throw new DirectoryNotFoundException(nameof(dir));
        _soundStoreDir = dir;
        return this;
    }

    /// <summary>
    /// 设置别名
    /// </summary>
    /// <param name="name"></param>
    public SipAccount SetDisplayName(string name)
    {
        Name = name;
        return this;
    }

    /// <summary>
    /// 确定指定的属性是否和本账号同源
    /// </summary>
    /// <param name="name">登录名</param>
    /// <param name="password">密码</param>
    /// <param name="host">服务器地址</param>
    /// <param name="port">服务器端口</param>
    /// <returns></returns>
    public bool IsSameTo(string name, string password, string host, int port)
    {
        return _name == name && _password == password && _host == host && _port == port;
    }

    /// <summary>
    /// 注册账号
    /// </summary>
    public void Register()
    {
        var acfg = new AccountConfig
        {
            idUri = $"sip:{_name}@{_host}",
            regConfig =
            {
                registrarUri = $"sip:{_host}:{_port}",
                retryIntervalSec = 3,
                timeoutSec = 3
            }
        };
        var cred = new AuthCredInfo("digest", "*", _name, 0, _password);
        acfg.sipConfig.authCreds.Add(cred);

        create(acfg);
    }

    /// <summary>
    /// 呼叫目标号码
    /// </summary>
    /// <param name="dstNumber"></param>
    public SipCall? Call(string dstNumber)
    {
        if (!IsRegistrationOk) throw new InvalidOperationException("尚未注册成功，无法发起呼叫！");
        lock (_lock)
        {
            if (_lastCall != null) return null;
            _lastCall = new SipCall(this, CallDirection.OutGoing, _soundStoreDir);
        }

        Console.WriteLine(
            $"=========> 正在发起呼叫[{Thread.CurrentThread.Name ?? $"{Thread.CurrentThread.ManagedThreadId}"}] :{_lastCall.CallId} -> {dstNumber}");

        OnCalling?.Invoke(this, _lastCall);
        HookCall(_lastCall);
        _lastCall.makeCall($"sip:{dstNumber}@{_host}", new CallOpParam(true));
        return _lastCall;
    }

    /// <summary>
    /// 挂断当前通话
    /// </summary>
    public void Hangup()
    {
        lock (_lock)
        {
            if (_lastCall == null) return;
            UnHookCall(_lastCall);
            try
            {
                _lastCall.Hangup();
                _lastCall.Dispose();
            }
            finally
            {
                _lastCall = null;
            }
        }
    }

    #endregion

    #region override

    public override void onRegState(OnRegStateParam prm)
    {
        base.onRegState(prm);
        OnRegistrationStateChanged?.Invoke(this, IsRegistrationOk ? string.Empty : prm.reason);
    }

    public override void onIncomingCall(OnIncomingCallParam prm)
    {
        base.onIncomingCall(prm);
        lock (_lock)
        {
            //本地忙时，来电直接挂断
            var call = new SipCall(this, CallDirection.InComing, prm.callId, _soundStoreDir);
            HookCall(call);
            if (_lastCall != null)
            {
                call.Hangup();
                OnCallMissed?.Invoke(this, call);
                UnHookCall(call);
                call.Dispose();
                return;
            }

            _lastCall = call;
        }
        OnCalling?.Invoke(this, _lastCall);
    }

    #endregion

    #region 属性

    /// <summary>
    /// 注册是否正常
    /// </summary>
    public bool IsRegistrationOk
    {
        get
        {
            var info = getInfo();
            return info.regStatus == pjsip_status_code.PJSIP_SC_OK;
        }
    }

    /// <summary>
    /// 账号别名
    /// </summary>
    public string Name { get; private set; }

    #endregion

    #region 私有方法

    void HookCall(SipCall call)
    {
        call.OnCallStateChanged += Call_OnCallStateChanged;
    }

    void UnHookCall(SipCall call)
    {
        call.OnCallStateChanged -= Call_OnCallStateChanged;
    }

    private void Call_OnCallStateChanged(object? sender, CallState e)
    {
        if(!(sender is SipCall call))return;
        OnCallStateChanged?.Invoke(this, call);
        if (call.State != CallState.DisConnected) return;
        lock (_lock)
        {
            if (_lastCall?.CallId == call.CallId)
            {
                call.Dispose();
                _lastCall = null;
            }
        }
    }

    #endregion
}