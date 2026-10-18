namespace ModbusHelper.Core.Protocol;

/// <summary>
/// 参数校验结果。
/// </summary>
public sealed class ValidationResult
{
    private readonly List<string> _errors = new();
    private readonly List<string> _warnings = new();

    /// <summary>是否通过校验。</summary>
    public bool IsValid => _errors.Count == 0;

    /// <summary>是否包含警告。</summary>
    public bool HasWarnings => _warnings.Count > 0;

    /// <summary>错误列表。</summary>
    public IReadOnlyList<string> Errors => _errors;

    /// <summary>警告列表。</summary>
    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>
    /// 添加错误。
    /// </summary>
    /// <param name="message">中文错误说明。</param>
    public void AddError(string message)
    {
        _errors.Add(message);
    }

    /// <summary>
    /// 添加警告。
    /// </summary>
    /// <param name="message">中文警告说明。</param>
    public void AddWarning(string message)
    {
        _warnings.Add(message);
    }

    /// <summary>
    /// 将错误合并为单行文本。
    /// </summary>
    /// <returns>中文错误文本。</returns>
    public string GetErrorMessage()
    {
        return string.Join("；", _errors);
    }
}
