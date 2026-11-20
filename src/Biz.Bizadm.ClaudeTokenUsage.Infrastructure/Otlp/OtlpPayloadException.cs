namespace Biz.Bizadm.ClaudeTokenUsage.Infrastructure.Otlp
{
    /// <summary>
    /// OTLP 요청 본문을 해석할 수 없을 때 던진다(HTTP 400으로 응답).
    /// </summary>
    public sealed class OtlpPayloadException : Exception
    {
        public OtlpPayloadException(string message, Exception? innerException = null) : base(message, innerException)
        {
        }
    }
}
