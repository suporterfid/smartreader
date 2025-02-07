using SmartReaderJobs.ViewModel.Mqtt.Endpoint;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Net.WebRequestMethods;

namespace plugin_contract.ViewModel.Stream
{
public class HttpStreamConfig
    {
        public int EventBufferSize { get; set; }
        public int EventPerSecondLimit { get; set; }
        public int EventAgeLimitMinutes { get; set; }
        public int KeepAliveIntervalSeconds { get; set; }
    }

}
