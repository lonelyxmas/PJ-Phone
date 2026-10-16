namespace PJ_Phone_Core.Sip;

/// <summary>
/// 通话方向
/// </summary>
public enum CallDirection
{
    /// <summary>
    /// 来电
    /// </summary>
    InComing,

    /// <summary>
    /// 去电
    /// </summary>
    OutGoing
}

/// <summary>
/// 通话状态
/// </summary>
public enum CallState
{
    /// <summary>
    /// 未知状态
    /// </summary>
    UnKnown,
    /// <summary>
    /// 正在连接
    /// </summary>
    Connecting,
    /// <summary>
    /// 已接通
    /// </summary>
    Connected,
    /// <summary>
    /// 已挂断
    /// </summary>
    DisConnected,
}