namespace PJ_Phone_Core.Sip;

/// <summary>
/// 账号注册状态发生变更
/// </summary>
/// <param name="account">发生变更的账号</param>
/// <param name="message">状态消息，NULL表示成功</param>
public delegate void RegistrationStateChanged(SipAccount account, string message);

/// <summary>
/// 来电时触发的事件
/// </summary>
/// <param name="account">来电账号</param>
/// <param name="call">来电信息</param>
public delegate void IncomingCall(SipAccount account, SipCall call);

/// <summary>
/// 当产生新的呼叫时候触发
/// </summary>
/// <param name="account">账号</param>
/// <param name="call">电话</param>
public delegate void Calling(SipAccount account, SipCall call);

/// <summary>
/// 通话遗漏
/// </summary>
/// <param name="account">账号</param>
/// <param name="call">电话</param>
public delegate void CallMissed(SipAccount account, SipCall call);

/// <summary>
/// 通话状态发生变化
/// </summary>
/// <param name="account">账号</param>
/// <param name="call">电话</param>
public delegate void CallStateChanged(SipAccount account, SipCall call);