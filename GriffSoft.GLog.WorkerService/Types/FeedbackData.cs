using GriffSoft.GLog.WorkerService.Logic.Forras.Types;

namespace GriffSoft.GLog.WorkerService.Helpers
{
    /// <summary>
    /// A Glog-ba bekerült adatok törlését segítő osztály.
    /// </summary>
    public class FeedbackData
    {
        public int GlogBrokerDataId { get; set; }
        public string? ExternalId { get; set; }
        public BrokerDataSource BrokerDataSource { get; set; }
    }
}
