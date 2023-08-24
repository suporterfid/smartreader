namespace SmartReaderStandalone.Utils.InputUsb
{
    public class KeyPressEvent : EventArgs
    {
        public KeyPressEvent(EventCode code, KeyState state, string data)
        {
            Code = code;
            State = state;
            Data = data;
        }

        public EventCode Code { get; }

        public KeyState State { get; }

        public string Data { get; }
    }
}
