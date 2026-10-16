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
    private bool _recordingLocalRingback;
    private bool _recorderCreated;
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
        // 仅在有实际通话媒体状态（EARLY/CALLING/CONNECTING/CONFIRMED）时连接音频与录音：
        //  - EARLY：180/183 早期媒体到达时状态即为 EARLY，必须放开才能录制对方彩铃（183+SDP）。
        //  - NULL/DISCONNECTED：通话不存在或已结束，跳过。
        //  - INCOMING：接听流程（Answer）单独处理，这里跳过避免重复接线。
        if (state == pjsip_inv_state.PJSIP_INV_STATE_NULL ||
            state == pjsip_inv_state.PJSIP_INV_STATE_DISCONNECTED ||
            state == pjsip_inv_state.PJSIP_INV_STATE_INCOMING) return;

        // 若之前已为 180 接通本地回铃音录音，而此刻建立了真实通话媒体（如后续 183 早期媒体），
        // 则回铃音已被真实媒体取代，移除播放设备→录音器链路，避免重复录制扬声器里的媒体声音。
        if (_recordingLocalRingback && audioMedia != null)
        {
            _recordingLocalRingback = false;
            if (_audioMediaRecorder != null && SipPhone.HasSoundDevice)
            {
                var playbackDevMedia = Endpoint.instance().audDevManager().getPlaybackDevMedia();
                playbackDevMedia?.stopTransmit(_audioMediaRecorder);
            }
        }

        // 确保录音器已创建（外呼在 CALLING 时已建，此处兜底；也使来电在 CONFIRMED 时可用）
        EnsureRecorderCreated();

        // 关键：通话音频媒体的录制与本地路由（麦克风/扬声器）必须等通话进入 CONFIRMED 之后再进行。
        // 早期媒体（EARLY，如 183 Session Progress）阶段，音频媒体端口尚未稳定接入会议桥，
        // 此时对 audioMedia 调用 startTransmit 会触发 PJSIP 本地内存访问异常（AccessViolation）。
        // 因此仅当 state == CONFIRMED 时才把通话媒体接到录音器与声音设备。
        // 对方彩铃（彩铃音）若以 180 无 SDP 形式下发，由 onCallState(EARLY) 的本地回铃音链路录制；
        // 若以 183 早期媒体下发，则在 CONFIRMED 之后由本段统一录制（早期几秒可能漏录，但保证不崩溃）。
        if (state != pjsip_inv_state.PJSIP_INV_STATE_CONFIRMED) return;

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
        if (captureDevMedia == null) return;

        //录制本地麦克风的声音
        captureDevMedia.startTransmit(_audioMediaRecorder);

        if (audioMedia == null) return;

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

    /// <summary>
    /// 仅创建一次录音器，避免重复 createRecorder 报错。
    /// </summary>
    private void EnsureRecorderCreated()
    {
        if (_audioMediaRecorder == null || _recorderCreated) return;
        if (!SipPhone.HasSoundDevice) return;
        if (RecordFile == null)
        {
            RecordFile = Path.Combine(_soundDir!, $"{DateTime.Now:HH_mm_ss}_{CallId}.wav");
        }
        _audioMediaRecorder.createRecorder(RecordFile);
        _recorderCreated = true;
    }

    /// <summary>
    /// 当前通话是否已建立活跃音频媒体，用于区分 180 无 SDP（无媒体）与 183 早期媒体（有媒体）。
    /// </summary>
    private bool HasActiveCallMedia()
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
                // 外呼早期媒体：180 Ringing 无 SDP 时，对方回铃音由 PJSIP 直接经
                // 本地播放设备（扬声器）输出，通话媒体流尚未建立。
                // 此时把播放设备媒体接到录音器，即可录到本地回铃音。
                // 若是 183 Session Progress 带 SDP（早期媒体/对方彩铃），通话音频媒体已建立，
                // 会经 onCallMediaState→ConnectAudioDevice 录制，这里不要重复接播放设备。
                // 来电（InComing）不会在此产生回铃音，必须排除，否则会误接本地扬声器声音。
                if (Direction == CallDirection.OutGoing &&
                    _audioMediaRecorder != null &&
                    SipPhone.HasSoundDevice &&
                    !HasActiveCallMedia() &&
                    !_recordingLocalRingback)
                {
                    Console.WriteLine($"连接本地回铃音录音 {CallId} ...");
                    var playbackDevMedia = Endpoint.instance().audDevManager().getPlaybackDevMedia();
                    playbackDevMedia?.startTransmit(_audioMediaRecorder);
                    _recordingLocalRingback = true;
                }
                break;
            case pjsip_inv_state.PJSIP_INV_STATE_CONNECTING:
                State = CallState.Connecting;
                break;
            case pjsip_inv_state.PJSIP_INV_STATE_CONFIRMED:
                State = CallState.Connected;
                // 关闭本地回铃音录音链路：进入正式通话后，对方声音已通过通话媒体流录制，
                // 播放设备里不再有回铃音，移除该链路以免重复录制。
                if (_recordingLocalRingback)
                {
                    _recordingLocalRingback = false;
                    if (_audioMediaRecorder != null && SipPhone.HasSoundDevice)
                    {
                        var playbackDevMedia = Endpoint.instance().audDevManager().getPlaybackDevMedia();
                        playbackDevMedia?.stopTransmit(_audioMediaRecorder);
                    }
                }
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