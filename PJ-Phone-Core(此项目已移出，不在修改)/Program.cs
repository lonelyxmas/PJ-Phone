using System.Net;
using PJ_Phone_Core;

Console.WriteLine("Copyright @ 2025 Sichuan PinJie Technology Co., Ltd");
Console.WriteLine("");
var argument = new EasyArgument();
argument
    .Config("--sound", str => string.Equals("y", str, StringComparison.OrdinalIgnoreCase), b =>
    {
        b.SetDesc("set has sound device on this computer");
    })
    .Config("--number", s => s, b =>
    {
        b.Required().SetDesc("the ext number to login").SetValidator(s => !string.IsNullOrWhiteSpace(s));
    })
    .Config("--password", s => s, b =>
    {
        b.Required().SetDesc("the password that login to server").SetValidator(s => !string.IsNullOrWhiteSpace(s));
    })
    .Config("--server", s => s, b =>
    {
        b.Required().SetDesc("the server ip").SetValidator(s => IPAddress.TryParse(s, out _));
    })
    .Config("--port", s => int.Parse(s!), b =>
    {
        b.Required().SetDesc("the server port, default is 5060").SetValidator(s => int.TryParse(s, out _));
    })
    .Config("--recording", s => s, b =>
    {
        b.SetDesc("the recording file save dir");
    });

var passInOk = argument
    .Build(args)
    .IsValid();

if (!passInOk) return;

Console.WriteLine("phone is starting...");
PhoneApp.Run(argument);