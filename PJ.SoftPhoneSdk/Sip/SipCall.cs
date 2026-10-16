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
    private bool _recorderCreated;
    private bool _recordingLocalRingback;


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
        TryStartTransmit(_audioMediaPlayer, audioMedia);

        // 如果启用了录音，也将播放器传输到录音器（录制播放的声音）
        if (_recordingFilePath != null)
        {
            EnsureRecorderCreated();
            TryStartTransmit(_audioMediaPlayer, _audioMediaRecorder);
        }
    }

    #endregion

    #region 私有方法

    /// <summary>
    /// 连接通话音频媒体与录音器、本机声音设备
    /// </summary>
    /// <param name="audioMedia">通话音频媒体</param>
    /// <param name="state">当前通话状态</param>
    private void ConnectAudioDevice(AudioMedia? audioMedia, pjsip_inv_state state)
    {
        // 仅在通话真实存在（EARLY/CALLING/CONNECTING/CONFIRMED）时接线：
        //  - EARLY：180/183 早期媒体阶段，对方彩铃在此阶段下发，必须放行才能录到彩铃。
        //  - NULL/DISCONNECTED：通话不存在或已结束，跳过。
        //  - INCOMING：接听流程（Answer）单独处理，这里跳过以免重复接线。
        if (state == pjsip_inv_state.PJSIP_INV_STATE_NULL ||
            state == pjsip_inv_state.PJSIP_INV_STATE_DISCONNECTED ||
            state == pjsip_inv_state.PJSIP_INV_STATE_INCOMING) return;

        EnsureRecorderCreated();

        // 对方彩铃在 EARLY 阶段下发，此时通话音频媒体尚未进入 CONFIRMED。
        // 对尚未接入会议桥的媒体端口调用 startTransmit 会触发 PJSIP 本地内存访问异常，
        // 因此只有在彩铃确实通过通话媒体流下发（媒体已 ACTIVE）时才录制通话媒体。
        if (_recordingFilePath != null && audioMedia != null && IsMediaActive())
        {
            _logger.Debug(Thread.CurrentThread.ManagedThreadId, Thread.CurrentThread.Name,
                $"连接录音文件 {state} {CallId}...");
            //audioMedia 对方的声音，录制他
            TryStartTransmit(audioMedia, _audioMediaRecorder);
        }

        if (!SipPhone.HasSoundDevice) return;
        //本地回放与采集
        var audDevManager = Endpoint.instance().audDevManager();
        var captureDevMedia = audDevManager.getCaptureDevMedia();
        if (audioMedia == null || captureDevMedia == null) return;

        //录制本地麦克风的声音
        if (_recordingFilePath != null)
        {
            TryStartTransmit(captureDevMedia, _audioMediaRecorder);
        }

        // 麦克风 → 通话媒体（对方听到本机声音）
        TryStartTransmit(captureDevMedia, audioMedia);

        // 通话媒体 → 扬声器（本机听到对方声音）
        TryStartTransmit(audioMedia, captureDevMedia);
    }

    /// <summary>
    /// 建立音频传输链路，已存在相同链路时跳过。
    /// 早期媒体、CONNECTING、CONFIRMED 等阶段可能多次进入本流程，
    /// 重复 startTransmit 会让 PJSIP 抛出端口已连接的异常。
    /// </summary>
    /// <param name="from">音频来源</param>
    /// <param name="to">音频目标</param>
    private static void TryStartTransmit(AudioMedia from, AudioMedia to)
    {
        var targetPortId = to.getPortId();
        if (from.getPortInfo().listeners.Any(listener => listener == targetPortId)) return;
        from.startTransmit(to);
    }

    /// <summary>
    /// 确保录音器已经创建，且只创建一次，避免重复 createRecorder 报错
    /// </summary>
    private void EnsureRecorderCreated()
    {
        if (_recorderCreated) return;
        if (_recordingFilePath == null) return;
        _recorderCreated = true;
        _logger.Debug(Thread.CurrentThread.ManagedThreadId, Thread.CurrentThread.Name,
            $"正在写入录音文件 {_recordingFilePath} ...");
        _audioMediaRecorder.createRecorder(_recordingFilePath);
    }

    /// <summary>
    /// 当前通话是否已经存在活跃的音频媒体，用于区分 180 无SDP（无媒体）与 183 早期媒体/彩铃（有媒体）
    /// </summary>
    private bool IsMediaActive()
    {
        var ci = getInfo();
        if (ci?.media == null) return false;
        for (var i = 0; i < ci.media.Count; i++)
        {
            var m = ci.media[i];
            if (m.type == pjmedia_type.PJMEDIA_TYPE_AUDIO &&
                m.status == pjsua_call_media_status.PJSUA_CALL_MEDIA_ACTIVE) return true;
        }

        return false;
    }

    /// <summary>
    /// 录制本机扬声器里的声音（本地回铃音）。180 Ringing 不带SDP时，对方彩铃/回铃音
    /// 不经通话媒体流下发，而是由 PJSIP 直接输出到本机播放设备，只有接上播放设备才能录到。
    /// </summary>
    private void StartRecordingLocalRingback()
    {
        if (_recordingFilePath == null) return;
        if (!SipPhone.HasSoundDevice) return;
        if (_recordingLocalRingback) return;

        var playbackDevMedia = Endpoint.instance().audDevManager().getPlaybackDevMedia();
        if (playbackDevMedia == null) return;

        _logger.Debug(Thread.CurrentThread.ManagedThreadId, Thread.CurrentThread.Name,
            $"连接本地回铃音录音 {CallId} ...");
        TryStartTransmit(playbackDevMedia, _audioMediaRecorder);
        _recordingLocalRingback = true;
    }

    /// <summary>
    /// 停止录制本机扬声器里的声音，避免进入正式通话后被重复录制
    /// </summary>
    private void StopRecordingLocalRingback()
    {
        if (!_recordingLocalRingback) return;
        _recordingLocalRingback = false;
        if (_recordingFilePath == null) return;
        if (!SipPhone.HasSoundDevice) return;

        var playbackDevMedia = Endpoint.instance().audDevManager().getPlaybackDevMedia();
        playbackDevMedia?.stopTransmit(_audioMediaRecorder);
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
                EnsureRecorderCreated();
                break;
            case pjsip_inv_state.PJSIP_INV_STATE_EARLY:
                EnsureRecorderCreated();
                // 外呼早期媒体阶段，对方彩铃在这里下发：
                //  - 180 Ringing 不带SDP：彩铃/回铃音由 PJSIP 直接输出到本机播放设备，
                //    通话媒体流尚未建立，只能接上播放设备才能录到。
                //  - 183 Session Progress 带SDP（早期媒体/彩铃）：通话音频媒体已建立，
                //    由 onCallMediaState→ConnectAudioDevice 录制，此处不接播放设备，避免重复录。
                // 来电（InComing）在此阶段不会产生彩铃，必须排除，否则会误录本机扬声器声音。
                if (Direction == CallDirection.OutGoing && !IsMediaActive())
                {
                    StartRecordingLocalRingback();
                }

                break;
            case pjsip_inv_state.PJSIP_INV_STATE_CONNECTING:
                State = CallState.Connecting;
                break;
            case pjsip_inv_state.PJSIP_INV_STATE_CONFIRMED:
                State = CallState.Connected;
                // 进入正式通话后，对方声音已通过通话媒体流录制，断开本地回铃音录音链路，
                // 避免扬声器声音被重复录进文件
                StopRecordingLocalRingback();
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
                // 通话媒体已激活，回铃音已被真实媒体取代，移除播放设备→录音器链路
                StopRecordingLocalRingback();

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