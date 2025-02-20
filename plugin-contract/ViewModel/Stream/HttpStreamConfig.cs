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
