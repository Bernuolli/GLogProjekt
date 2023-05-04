using Microsoft.Extensions.Logging;

namespace GriffSoft.GLog.WorkerService.Shared;

public class LogEntryDto
{
    public Guid UniqueId { get; set; }

    public Guid? CorrelationId { get; set; }

    public DateTime LogDate { get; set; }

    public string? Session { get; set; }

    public string? System { get; set; }

    public string? SystemVersion { get; set; }

    public string? SubSystem { get; set; }

    public string? SubSystemVersion { get; set; }

    public string? Module { get; set; }

    public string? ModuleVersion { get; set; }

    public string? Function { get; set; }

    public LogLevel LogLevel { get; set; }

    public string? LogType { get; set; }

    public string? Message { get; set; }

    public string? InputValues { get; set; }

    public string? OutputValues { get; set; }

    public string? AdditionalData { get; set; }

    public string? Host { get; set; }

    public string? HostConfig { get; set; }
        
    public string? UserData { get; set; }

    public bool IsAudit { get; set; }

    public string? ExternalId { get; set; }
    public virtual int GlogBrokerDataId { get; set; }
    public virtual int BrokerDataSource { get; set; }
}