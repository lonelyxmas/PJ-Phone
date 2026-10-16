using pj;

namespace PJ.SoftPhoneSdk.Sip;

/// <summary>
/// 呼叫信息
/// </summary>
class SipCall : Call
{
    private readonly IPhoneLogger _logger;
    private readonly AudioMediaRecorder _audioMediaRecorder;
    private readonly AutoResetEvent _hangupResetEvent = new AutoResetEvent(false);
    private readonly object _lock = new object();

    private AudioMediaPlayer? _audioMediaPlayer;
    private bool _isDisposed;
    private string? _recordingFilePath;


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
    /// <param name="logger"></param>
    internal SipCall(SipAccount account, CallDirection direction, IPhoneLogger logger) : base(account)
    {
        _logger = logger;
        Direction = direction;
        _audioMediaRecorder = new AudioMediaRecorder();
    }

    /// <summary>
    /// 新建一个通话
    /// </summary>
    /// <param name="account">账号</param>
    /// <param name="direction">通话方向</param>
    /// <param name="id">通话编号</param>
    /// <param name="logger"></param>
    internal SipCall(SipAccount account, CallDirection direction, int id, IPhoneLogger logger) : base(account,
        id)
    {
        _logger = logger;
        Direction = direction;
        CallId = getInfo().callIdString;
        _audioMediaRecorder = new AudioMediaRecorder();
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
    /// 当前通话录音文件
    /// </summary>
    public string? RecordingFile => _recordingFilePath;

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

    /// <summary>
    /// 设置录音文件位置
    /// </summary>
    /// <param name="filePath">录音文件路径,null表示不录音，默认不录音</param>
    public void SetRecordingFile(string? filePath)
    {
        _recordingFilePath = filePath;
    }

    /// <summary>
    /// 向当前通话播放媒体文件
    /// </summary>
    /// <param name="media">媒体文件地址<br/>
    /// 格式要求：<br/>
    ///  - 采样率：16000 Hz<br/>
    ///  - 声道数：1（单声道）<br/>
    ///  - 编码：16-bit PCM<br/>
    ///  - 格式：标准 WAV 文件<br/>
    /// <code>ffmpeg -i input.mp3 -ar 16000 -ac 1 -f wav output.wav</code>
    /// </param>
    public void Play(string media)
    {
        if (_isDisposed) return;

        // 获取通话信息
        var ci = getInfo();
        if (ci == null) return;
        if (ci.state != pjsip_inv_state.PJSIP_INV_STATE_CONFIRMED) return;

        // 查找音频媒体
        AudioMedia? audioMedia = null;
        for (var i = 0; i < ci.media.Count; i++)
        {
            var cmi = ci.media[i];
            if (cmi.type != pjmedia_type.PJMEDIA_TYPE_AUDIO) continue;
            audioMedia = getAudioMedia(i);
            break;
        }

        if (audioMedia == null) return;

        // 创建播放器
        _audioMediaPlayer = new AudioMediaPlayer();
        _audioMediaPlayer.createPlayer(media);

        // 将播放器传输到通话音频媒体（发送给对方）
        _audioMediaPlayer.startTransmit(audioMedia);

        // 如果启用了录音，也将播放器传输到录音器（录制播放的声音）
        _audioMediaPlayer.startTransmit(_audioMediaRecorder);
    }

    #endregion

    #region 私有方法

    private void ConnectAudioDevice(AudioMedia? audioMedia, pjsip_inv_state state)
    {
        //if (state == pjsip_inv_state.PJSIP_INV_STATE_DISCONNECTED ||
        //    state == pjsip_inv_state.PJSIP_INV_STATE_NULL) return;

        if (state != pjsip_inv_state.PJSIP_INV_STATE_CONNECTING &&
            state != pjsip_inv_state.PJSIP_INV_STATE_CALLING) return;

        if (_recordingFilePath != null)
        {
            //录音：同时录制通话对方的声音和本地麦克风的声音
            _logger.Debug(Thread.CurrentThread.ManagedThreadId, Thread.CurrentThread.Name,
                $"连接录音文件 {state} {CallId}...");
            //audioMedia 对方的声音，录制他
            audioMedia?.startTransmit(_audioMediaRecorder);
        }

        if (!SipPhone.HasSoundDevice) return;
        //本地回放与采集
        var audDevManager = Endpoint.instance().audDevManager();
        var captureDevMedia = audDevManager.getCaptureDevMedia();
        if (audioMedia == null || captureDevMedia == null) return;

        //录制本地麦克风的声音
        captureDevMedia.startTransmit(_audioMediaRecorder);

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
                if (_recordingFilePath != null)
                {
                    _logger.Debug(Thread.CurrentThread.ManagedThreadId, Thread.CurrentThread.Name,
                        $"正在写入录音文件 {_recordingFilePath} ...");
                    _audioMediaRecorder.createRecorder(_recordingFilePath);
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
        _audioMediaRecorder.Dispose();
        _audioMediaPlayer?.Dispose();
        base.Dispose(disposing);
    }

    #endregion
}