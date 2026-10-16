using System.Reflection;
using pj;

namespace PJ_Phone_Core.Sip;

/// <summary>
/// SIP电话
/// </summary>
public static class SipPhone
{
    #region 内部变量

    private static readonly object Lock = new object();
    private static readonly List<SipAccount> SipAccounts = new List<SipAccount>();
    private static Endpoint? _endpoint;
    private static int _transportId;
    private static readonly ConsoleLogWriter Logger = new ConsoleLogWriter();

    #endregion

    #region 事件

    /// <summary>
    /// 账号注册状态发生变更
    /// </summary>
    public static event RegistrationStateChanged? OnRegistrationStateChanged;

    /// <summary>
    /// 来电时发生
    /// </summary>
    public static event IncomingCall? OnIncomingCall;

    /// <summary>
    /// 来电遗漏
    /// </summary>
    public static event CallMissed? OnCallMissed;

    /// <summary>
    /// 通话状态变更
    /// </summary>
    public static event CallStateChanged? OnCallStateChanged;

    #endregion

    #region 属性

    /// <summary>
    /// 当前电话是否设置了没有声卡的工作模式
    /// </summary>
    public static bool HasSoundDevice { get; private set; }

    #endregion

    #region 公共方法

    /// <summary>
    /// 采用默认的UDP协议初始化一个本地电话
    /// </summary>
    /// <param name="thread">初始化该电话的线程</param>
    /// <param name="setNoSoundDevice">是否设置为没有声卡的模式，该模式下，将不会通过电脑声卡播放采集声音</param>
    public static void Init(Thread thread, bool setNoSoundDevice)
    {
        lock (Lock)
        {
            if (_endpoint != null) return;
            _endpoint = new Endpoint();
        }

        HasSoundDevice = !setNoSoundDevice;

        // Create Library
        _endpoint.libCreate();

        // Initialize endpoint
        _endpoint.libInit(GetEndpointConfig());

        // Create SIP transport.
        InitTransport();

        // Start the library
        _endpoint.libStart();

        //设置为没有设备
        if (setNoSoundDevice)
        {
            _endpoint.audDevManager().setNullDev();
            _endpoint.utilLogWrite(3, "SipPhone", "当前工作在无声卡模式");
        }

        //ep.libRegisterThread(Assembly.GetEntryAssembly().FullName);
        // Register Thread
        Endpoint
            .instance()
            .libRegisterThread(thread.Name ?? Assembly.GetEntryAssembly()?.FullName);
    }

    /// <summary>
    /// 添加SIP账号
    /// </summary>
    /// <param name="name">登录名称</param>
    /// <param name="password">密码</param>
    /// <param name="host">服务器地址</param>
    /// <param name="port">端口</param>
    public static SipAccount AddSipAccount(string name, string password, string host, int port = 5060)
    {
        SipAccount? account;
        lock (Lock)
        {
            account = SipAccounts.FirstOrDefault(a => a.IsSameTo(name, password, host, port));
            if (account != null) return account;

            account = new SipAccount(name, password, host, port);
            SipAccounts.Add(account);
        }

        HookAccountEvent(account);
        account.Register();

        return account;
    }

    /// <summary>
    /// 清空内部的账号
    /// </summary>
    public static void ClearAccount()
    {
        lock (Lock)
        {
            foreach (var account in SipAccounts.ToArray())
            {
                UnHookAccountEvent(account);
                account.Dispose();
                SipAccounts.Remove(account);
            }
        }
    }

    /// <summary>
    /// 移除指定的账号
    /// </summary>
    /// <param name="name">登录名称</param>
    /// <param name="password">密码</param>
    /// <param name="host">服务器地址</param>
    /// <param name="port">端口</param>
    public static void RemoveAccount(string name, string password, string host, int port = 5060)
    {
        lock (Lock)
        {
            var account = SipAccounts.FirstOrDefault(a => a.IsSameTo(name, password, host, port));
            if (account == null) return;
            UnHookAccountEvent(account);
            account.Dispose();
            SipAccounts.Remove(account);
        }
    }

    /// <summary>
    /// 关闭电话
    /// </summary>
    public static void ShutDown()
    {
        if (_endpoint == null) return;
        _endpoint.hangupAllCalls();

        if (_transportId >= 0)
        {
            _endpoint.transportClose(_transportId);
        }

        lock (Lock)
        {
            foreach (var account in SipAccounts)
            {
                UnHookAccountEvent(account);
                account.Dispose();
            }
        }

        _endpoint.libDestroy();
        _endpoint.Dispose();
    }

    /// <summary>
    /// 获取所有已经注册了的账号
    /// </summary>
    /// <returns></returns>
    public static SipAccount[] GetAllAccounts()
    {
        lock (Lock)
        {
            return SipAccounts.ToArray();
        }
    }

    /// <summary>
    /// 获取第一个或默认的账号
    /// </summary>
    /// <returns></returns>
    public static SipAccount? GetFirstOrDefaultAccount()
    {
        lock (Lock)
        {
            return SipAccounts.FirstOrDefault();
        }
    }

    #endregion

    #region 私有方法

    static EpConfig GetEndpointConfig()
    {
        var epConfig = new EpConfig
        {
            uaConfig = {maxCalls = int.MaxValue},
            medConfig = {sndClockRate = 16000, noVad = true, ecTailLen = 0, hasIoqueue = true},
            /*
             * Level 0 Display fatal error only.
             * Level 1 Display error messages and more severe verbosity level only.
             * Level 2 Display Warning messages and more severe verbosity level only.
             * Level 3 Info verbosity (normally used by applications).
             * Level 4 Important PJSIP events.
             * Level 5 Detailed PJSIP events.
             * Level 6 Very detailed PJLIB events.
             */
            logConfig =
            {
                level = 5,
                //consoleLevel = 2,
                //filename = Path.Combine("PJ.Cti.DialTest.Host.logs", $"pjsip-{DateTime.Now:yyyy-MM-dd}.log")
                writer = Logger
            }
        };
        return epConfig;
    }

    static void InitTransport()
    {
        var sipTpConfig = new TransportConfig();
        var random = new Random();
        var randomPort = random.Next(5070, 65534);
        sipTpConfig.port = (uint) randomPort; //5060;
        _transportId = _endpoint!.transportCreate(pjsip_transport_type_e.PJSIP_TRANSPORT_UDP, sipTpConfig);
    }

    static void HookAccountEvent(SipAccount account)
    {
        account.OnRegistrationStateChanged += Account_OnRegistrationStateChanged;
        account.OnCalling += Account_OnCalling;
        account.OnCallMissed += Account_OnCallMissed;
        account.OnCallStateChanged += Account_OnCallStateChanged;
    }

    private static void Account_OnCallStateChanged(object? sender, SipCall e)
    {
        OnCallStateChanged?.Invoke((SipAccount)sender!, e);
    }

    private static void Account_OnCallMissed(object? sender, SipCall e)
    {
        OnCallMissed?.Invoke((SipAccount)sender!, e);
    }

    private static void Account_OnCalling(object? sender, SipCall e)
    {
        if (e.Direction == CallDirection.InComing)
        {
            OnIncomingCall?.Invoke((SipAccount)sender!, e);
        }
    }

    static void UnHookAccountEvent(SipAccount account)
    {
        account.OnRegistrationStateChanged -= Account_OnRegistrationStateChanged;
        account.OnCalling -= Account_OnCalling;
        account.OnCallMissed -= Account_OnCallMissed;
        account.OnCallStateChanged -= Account_OnCallStateChanged;
    }

    private static void Account_OnRegistrationStateChanged(object? sender, string e)
    {
        OnRegistrationStateChanged?.Invoke((SipAccount)sender!, e);
    }

    #endregion
}