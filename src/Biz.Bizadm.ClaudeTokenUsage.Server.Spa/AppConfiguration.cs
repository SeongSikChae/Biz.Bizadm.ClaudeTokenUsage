using System.Configuration;
using System.Configuration.Annotation;
using Biz.Bizadm.ClaudeTokenUsage.Server.Core;

namespace Biz.Bizadm.ClaudeTokenUsage.Server.Spa
{
    public sealed class AppConfiguration : IValidatableConfiguration
    {
        [Property(PropertyType.STRING, required: true)]
        public string ContentRootPath { get; set; } = null!;

        [Property(PropertyType.USHORT, required: true)]
        public ushort? Port { get; set; }

        /// <summary>
        /// SPA 빌드 결과 폴더. <see cref="ContentRootPath"/> 기준 상대 경로.
        /// </summary>
        [Property(PropertyType.STRING, DefaultValue = "ClientApp")]
        public string WebRootPath { get; set; } = null!;

        /// <summary>
        /// DuckDB 파일 경로. <see cref="ContentRootPath"/> 기준 상대 경로이며 폴더가 없으면 기동 시 만든다.
        /// </summary>
        [Property(PropertyType.STRING, DefaultValue = "data/token-usage.duckdb")]
        public string DatabaseFilePath { get; set; } = null!;

        public OsKmsConfiguration OsKms { get; set; } = null!;

        public TlsConfiguration? Tls { get; set; }

        public void Validate()
        {
            ArgumentNullException.ThrowIfNull(OsKms, nameof(OsKms));
            ConfigurationValidator.Validate(OsKms);
            if (Tls is not null)
                ConfigurationValidator.Validate(Tls);
        }
    }
}
