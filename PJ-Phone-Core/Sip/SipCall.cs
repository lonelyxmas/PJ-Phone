using pj;

namespace PJ_Phone_Core.Sip;

/// <summary>
/// 呼叫信息
/// </summary>
public class SipCall : Call
{
    private readonly string? _soundDir;
    private readonly AudioMediaRecorder? _audioMediaRecorder;
    private readonly AutoResetEvent _hangupResetEvent = new AutoResetEvent(false);
    private bool _isDisposed;
    private readonly object _lock = new object();

    #region 事件

    /// <summary>
    /// 呼叫状态发生变更
    /// </summary>
    public event EventHandler<CallState>? OnCallStateChanged;

    #endregion

    #region 构造函数

    /// <summary>
    /// 新建一个通话
    /// </summary>
    /// <param name="account">账号</param>
    /// <param name="direction">通话方向</param>
    /// <param name="soundDir">录音存放位置，null则不存储</param>
    internal SipCall(SipAccount account, CallDirection direction, string? soundDir = null) : base(account)
    {
        _soundDir = soundDir;
        Direction = direction;
        if (soundDir == null) return;
        _audioMediaRecorder = new AudioMediaRecorder();
        _soundDir = InitStoreDir(soundDir);
    }

    /// <summary>
    /// 新建一个通话
    /// </summary>
    /// <param name="account">账号</param>
    /// <param name="direction">通话方向</param>
    /// <param name="id">通话编号</param>
    /// <param name="soundDir">录音存放位置，null则不存储</param>
    internal SipCall(SipAccount account, CallDirection direction, int id, string? soundDir = null) : base(account,
        id)
    {
        _soundDir = soundDir;
        Direction = direction;
        CallId = getInfo().callIdString;
        if (soundDir == null) return;
        _audioMediaRecorder = new AudioMediaRecorder();
        _soundDir = InitStoreDir(soundDir);
    }

    #endregion

    #region 属性

    /// <summary>
    /// 通话方向
    /// </summary>
    public CallDirection Direction { get; }

    /// <summary>
    /// 通话状态
    /// </summary>
    public CallState State { get; private set; } = CallState.UnKnown;

    /// <summary>
    /// 获取通话编号
    /// </summary>
    public string? CallId { get; private set; }

    /// <summary>
    /// 获取当前呼叫对应的录音文件，可能为NULL，表示不用录音
    /// </summary>
    public string? RecordFile { get; private set; }

    #endregion

    #region 方法

    /// <summary>
    /// 挂断电话
    /// </summary>
    public void Hangup()
    {
        if (_isDisposed) return;
        var op = new CallOpParam(true)
        {
            statusCode = pjsip_status_code.PJSIP_SC_DECLINE
        };
        hangup(op);
        Dispose();
    }

    /// <summary>
    /// 接起电话
    /// </summary>
    public void Answer()
    {
        if (_isDisposed) return;
        var ci = getInfo();
        var state = ci.state;
        if (state != pjsip_inv_state.PJSIP_INV_STATE_INCOMING) return;

        var op = new CallOpParam(true)
        {
            statusCode = pjsip_status_code.PJSIP_SC_OK
        };
        answer(op);

        var cmiv = getInfo().media;
        for (var i = 0; i < cmiv.Count; i++)
        {
            var cmi = cmiv[i];
            if (cmi.type != pjmedia_type.PJMEDIA_TYPE_AUDIO) continue;
            ConnectAudioDevice(getAudioMedia(i), state);
        }
    }

    /// <summary>
    /// 同步等，直到电话挂断
    /// </summary>
    /// <param name="millisecondsTimeout">等待超时时长：毫秒</param>
    public bool WaitForHangup(int millisecondsTimeout)
    {
        return _hangupResetEvent.WaitOne(millisecondsTimeout);
    }

    #endregion

    #region 私有方法

    string InitStoreDir(string storeDir)
    {
        var dir = Path.Combine(storeDir, $"{DateTime.Now:yyyy-MM-dd}");
        if (Directory.Exists(dir)) return dir;
        Directory.CreateDirectory(dir);
        return dir;
    }

    private void ConnectAudioDevice(AudioMedia? audioMedia, pjsip_inv_state state)
    {
        //if (state == pjsip_inv_state.PJSIP_INV_STATE_DISCONNECTED ||
        //    state == pjsip_inv_state.PJSIP_INV_STATE_NULL) return;

        if (state != pjsip_inv_state.PJSIP_INV_STATE_CONNECTING &&
            state != pjsip_inv_state.PJSIP_INV_STATE_CALLING) return;

        //录音
        if (_audioMediaRecorder != null)
        {
            Console.WriteLine($"连接录音文件 {state} {CallId}...");
            audioMedia?.startTransmit(_audioMediaRecorder);
        }

        if (!SipPhone.HasSoundDevice) return;
        //本地回放与采集
        var audDevManager = Endpoint.instance().audDevManager();
        var captureDevMedia = audDevManager.getCaptureDevMedia();
        if (audioMedia == null || captureDevMedia == null) return;



        // This will connect the sound device/mic to the call audio media
        captureDevMedia.startTransmit(audioMedia);

        // And this will connect the call audio media to the sound device/speaker
        audioMedia.startTransmit(captureDevMedia);


        if (captureDevMedia.getPortInfo().listeners.Count(s => s == audioMedia.getPortId()) < 1)
        {
            captureDevMedia.startTransmit(audioMedia);
        }

        if (audioMedia.getPortInfo().listeners.Count(s => s == captureDevMedia.getPortId()) < 1)
        {
            audioMedia.startTransmit(captureDevMedia);
        }
    }

    #endregion

    #region override

    public override void onCallState(OnCallStateParam prm)
    {
        base.onCallState(prm);
        if (_isDisposed) return;
        var ci = getInfo();
        if (ci == null) return;

        CallId = getInfo().callIdString;

        switch (ci.state)
        {
            case pjsip_inv_state.PJSIP_INV_STATE_CALLING:
                if (_audioMediaRecorder != null)
                {
                    Console.WriteLine($"正在创建录音文件 {CallId} ...");
                    RecordFile = Path.Combine(_soundDir!, $"{DateTime.Now:HH_mm_ss}_{CallId}.wav");
                    _audioMediaRecorder.createRecorder(RecordFile);
                }

                break;
            case pjsip_inv_state.PJSIP_INV_STATE_CONNECTING:
                State = CallState.Connecting;
                break;
            case pjsip_inv_state.PJSIP_INV_STATE_CONFIRMED:
                State = CallState.Connected;
                break;
            case pjsip_inv_state.PJSIP_INV_STATE_DISCONNECTED:
                State = CallState.DisConnected;
                Dispose();
                break;
        }

        OnCallStateChanged?.Invoke(this, State);
    }

    public override void onCallMediaState(OnCallMediaStateParam prm)
    {
        base.onCallMediaState(prm);
        if (_isDisposed) return;
        var ci = getInfo();
        if (ci == null)
            return;

        var cmiv = ci.media;

        for (var i = 0; i < cmiv.Count; i++)
        {
            var cmi = cmiv[i];
            if (cmi.status == pjsua_call_media_status.PJSUA_CALL_MEDIA_ACTIVE ||
                cmi.status == pjsua_call_media_status.PJSUA_CALL_MEDIA_REMOTE_HOLD)
            {

                var am = getAudioMedia(i); //TODO AudioMedia.typecastFromMedia(getMedia((uint)i));

                ConnectAudioDevice(am, ci.state);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        lock (_lock)
        {
            if (_isDisposed) return;
            _isDisposed = true;
        }

        _hangupResetEvent.Set();
        _hangupResetEvent.Dispose();
        _audioMediaRecorder?.Dispose();
        base.Dispose(disposing);
    }

    #endregion

}