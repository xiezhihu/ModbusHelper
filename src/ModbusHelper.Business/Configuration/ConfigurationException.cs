namespace ModbusHelper.Business.Configuration;

/// <summary>
/// 配置文件读写或版本兼容异常。
/// </summary>
public sealed class ConfigurationException : Exception
{
    /// <summary>
    /// 初始化配置异常。
    /// </summary>
    /// <param name="message">中文错误信息。</param>
    /// <param name="innerException">原始异常。</param>
    public ConfigurationException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
