using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GriffSoft.GLog.WorkerService.Logic.Forras.Types
{
    public class FUVLogCheckoutData
    {
        public int FromId { get; set; }
        public int ToId { get; set; }
        public string? UniqueId { get; set; }
        public List<int>? AllIds { get; set; }
    }
}
