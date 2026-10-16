using PJ_Phone_Core.Sip;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PJ_Phone_Core;

internal class PhoneApp(EasyArgument argument)
{
    #region static

    public static PhoneApp? Current { get; private set; }

    public static void Run(EasyArgument argument)
    {
        Current = new PhoneApp(argument);
        Current.Init();
    }

    #endregion


    void Init()
    {
        SipPhone.Init(Thread.CurrentThread, argument.Get<bool>("--sound"));
        SipPhone.OnRegistrationStateChanged += SipPhone_OnRegistrationStateChanged;
        SipPhone.OnIncomingCall += SipPhone_OnIncomingCall;
        SipPhone.OnCallStateChanged += SipPhone_OnCallStateChanged;
        SipPhone.ClearAccount();
        var account = SipPhone
            .AddSipAccount(argument.Get<string>("--number")!,
                argument.Get<string>("--password")!,
                argument.Get<string>("--server")!,
                argument.Get<int>("--port"))
            .SetDisplayName(argument.Get<string>("--number")!);
        var recordingDir = argument.Get<string>("--recording");
        if (string.IsNullOrWhiteSpace(recordingDir)) return;
        if (!Directory.Exists(recordingDir)) Directory.CreateDirectory(recordingDir);
        account.SetRecordStoreDir(recordingDir);
    }

    void SipPhone_OnCallStateChanged(SipAccount account, SipCall call)
    {
        throw new NotImplementedException();
    }

    void SipPhone_OnIncomingCall(SipAccount account, SipCall call)
    {
        throw new NotImplementedException();
    }
    void SipPhone_OnRegistrationStateChanged(SipAccount account, string message)
    {
        throw new NotImplementedException();
    }
}