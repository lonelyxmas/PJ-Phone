using System.Diagnostics;
using System.Reflection;

namespace PJ_Phone_Core;

internal class EasyArgument
{
    private readonly Dictionary<string, IArgumentConfig> _argumentConfigs = new(StringComparer.OrdinalIgnoreCase);
    private bool _isDebuggerModel;

    public EasyArgument Config<T>(string name, Func<string?, T?> parser,
        Action<IArgumentConfigBuilder>? argumentBuilder = null)
    {
        var ac = new ArgumentConfig<T>(parser);
        argumentBuilder?.Invoke(ac);
        _argumentConfigs[name] = ac;
        return this;
    }

    public EasyArgument Build(string[] arguments)
    {
        _isDebuggerModel = arguments.Contains("/debug");
        for (var i = 0; i < arguments.Length; i++)
        {
            if (!_argumentConfigs.TryGetValue(arguments[i], out var config)) continue;
            var next = i + 1;
            if (next >= arguments.Length) continue;
            if (_argumentConfigs.ContainsKey(arguments[next])) continue; //下一个值本身也是参数名称
            config.SetInput(arguments[next]);
        }

        return this;
    }

    public bool IsValid()
    {
        var isValid = true;
        foreach (var config in _argumentConfigs)
        {
            if (config.Value is { IsRequired: true, IsInputSet: false })
            {
                isValid = false;
                Console.WriteLine($"× {config.Value.Desc ?? config.Key} is required.");
                continue;
            }

            if (config.Value.Validate()) continue;

            Console.WriteLine($"× {config.Value.Desc ?? config.Key} is invalid.");
            isValid = false;
        }

        if (isValid) return true;

        Console.WriteLine("");
        Console.WriteLine("-----");
        Console.WriteLine($"Usage {Path.GetFileNameWithoutExtension(Assembly.GetEntryAssembly()!.Location)}");
        var max = _argumentConfigs.Max(a => a.Key.Length);
        foreach (var config in _argumentConfigs)
        {
            Console.WriteLine(
                $"  {config.Key.PadRight(max, ' ')} {(config.Value.IsRequired ? "[required]" : "[optional]")} {config.Value.Desc}");
        }

        return false;
    }

    public T? Get<T>(string name)
    {
        return _argumentConfigs.TryGetValue(name, out var config) ? (T?)config.Parse() : default;
    }

    public void Debug()
    {
#if DEBUG
        if (_isDebuggerModel)
        {
            Debugger.Launch();
        }
#endif
    }
}

interface IArgumentConfig
{
    object? Parse();

    void SetInput(string input);

    bool IsInputSet { get; }

    bool IsRequired { get; }

    string? Desc { get; }

    bool Validate();
}

interface IArgumentConfigBuilder
{
    IArgumentConfigBuilder SetDesc(string desc);

    IArgumentConfigBuilder Required();

    IArgumentConfigBuilder SetValidator(Func<string, bool> validatorFunc);
}

class ArgumentConfig<T>(Func<string?, T?> parser)
    : IArgumentConfig, IArgumentConfigBuilder
{
    private string? _input;
    private string? _desc;
    private bool _isRequired;
    private Func<string, bool>? _validateFunc;
    private bool _hasSetInput;

    public object? Parse()
    {
        return parser(_input);
    }

    public bool IsInputSet => _hasSetInput;

    public bool IsRequired => _isRequired;

    public string? Desc => _desc;

    public bool Validate()
    {
        if (_isRequired)
        {
            if (!_hasSetInput) return false;
            return _validateFunc == null || _validateFunc(_input!);
        }

        if (!_hasSetInput) return true;
        return _validateFunc == null || _validateFunc(_input!);
    }

    public void SetInput(string input)
    {
        _hasSetInput = true;
        _input = input;
    }

    public IArgumentConfigBuilder SetDesc(string desc)
    {
        _desc = desc;
        return this;
    }

    public IArgumentConfigBuilder Required()
    {
        _isRequired = true;
        return this;
    }

    public IArgumentConfigBuilder SetValidator(Func<string, bool> validatorFunc)
    {
        _validateFunc = validatorFunc;
        return this;
    }
}