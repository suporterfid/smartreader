using System.Text;

namespace SmartReaderStandalone.Utils.InputUsb
{
    public class InputReader : IDisposable
    {
        public delegate void RaiseKeyPress(KeyPressEvent e);

        public delegate void RaiseMouseMove(MouseMoveEvent e);

        public event RaiseKeyPress OnKeyPress;
        public event RaiseMouseMove OnMouseMove;

        private const int BufferLength = 24;

        private readonly byte[] _buffer = new byte[BufferLength];

        private FileStream? _stream;
        private bool _disposing;

        private StringBuilder _bufferStringBuilder;

        public InputReader(string path)
        {
            _bufferStringBuilder = new StringBuilder();
            _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            _ = Task.Run(Run);
        }

        private void Run()
        {
            while (true)
            {
                if (_disposing)
                    break;

                _ = _stream.Read(_buffer, 0, BufferLength);

                try
                {
                    //string hexString = BitConverter.ToString(_buffer).Replace("-", "");
                    //Console.WriteLine($"HID Event HEX: {hexString} ");

                    //var typeHex = hexString.Substring(16, 4);
                    //var codeHex = hexString.Substring(20, 4);
                    //var valueHex = hexString.Substring(24, 8);

                    //Console.WriteLine($"HID Event HEX: {typeHex} Code:{codeHex} Value:{valueHex}");
                    //var convertedTypeHex = Convert.ToUInt16(typeHex, 16);
                    //var convertedCodeHex = Convert.ToUInt16(codeHex, 16);
                    //var convertedValueHex = Convert.ToInt32(valueHex, 16);

                    //Console.WriteLine($"HID Event Type: {convertedTypeHex} Code:{convertedCodeHex} Value:{convertedValueHex}");

                    // UTF conversion - String from bytes
                    //string utfString = Encoding.UTF8.GetString(_buffer, 0, _buffer.Length);
                    //Console.WriteLine($"HID Event UTF8: {utfString} ");

                    // ASCII conversion - string from bytes
                    //string asciiString = Encoding.ASCII.GetString(_buffer, 0, _buffer.Length);
                    //Console.WriteLine($"HID Event ASCII: {asciiString} ");
                }
                catch (Exception)
                {


                }

                //var type = BitConverter.ToInt16(new[] { _buffer[16], _buffer[17] }, 0);
                //var code = BitConverter.ToInt16(new[] { _buffer[18], _buffer[19] }, 0);
                //var value = BitConverter.ToInt32(new[] { _buffer[20], _buffer[21], _buffer[22], _buffer[23] }, 0);

                string hexString = BitConverter.ToString(_buffer).Replace("-", "");
                //Console.WriteLine($"HID Event HEX: {hexString} ");

                var typeHex = hexString.Substring(16, 4);
                var codeHex = hexString.Substring(20, 4);
                var valueHex = hexString.Substring(24, 8);

                //Console.WriteLine($"HID Event HEX: {typeHex} Code:{codeHex} Value:{valueHex}");
                if ("0100".Equals(typeHex))
                {
                    typeHex = typeHex.Substring(0, 2);
                    codeHex = codeHex.Substring(0, 2);
                }
                var type = Convert.ToUInt16(typeHex, 16);
                short code = Convert.ToInt16(codeHex, 16);
                var value = Convert.ToInt32(valueHex, 16);

                //Console.WriteLine($"HID Event Type: {convertedTypeHex} Code:{convertedCodeHex} Value:{convertedValueHex}");


                var eventType = (EventType)type;
                if (eventType == 0)
                {
                    continue;
                }
                //Console.WriteLine($"HID Event Type: {type} Code:{code} Value:{value}");
                switch (eventType)
                {
                    case EventType.EV_KEY:
                        HandleKeyPressEvent(code, value);
                        break;
                    case EventType.EV_REL:
                        var axis = (MouseAxis)code;
                        var e = new MouseMoveEvent(axis, value);
                        OnMouseMove?.Invoke(e);
                        break;
                }
            }
        }

        private void HandleKeyPressEvent(short code, int value)
        {
            var c = (EventCode)code;
            var s = (KeyState)value;
            if (s == KeyState.KeyUp)
            {
                switch (c)
                {
                    case EventCode.Reserved:
                        break;
                    case EventCode.Esc:
                        break;
                    case EventCode.Num1:
                        _ = _bufferStringBuilder.Append("1");
                        break;
                    case EventCode.Num2:
                        _ = _bufferStringBuilder.Append("2");
                        break;
                    case EventCode.Num3:
                        _ = _bufferStringBuilder.Append("3");
                        break;
                    case EventCode.Num4:
                        _ = _bufferStringBuilder.Append("4");
                        break;
                    case EventCode.Num5:
                        _ = _bufferStringBuilder.Append("5");
                        break;
                    case EventCode.Num6:
                        _ = _bufferStringBuilder.Append("6");
                        break;
                    case EventCode.Num7:
                        _ = _bufferStringBuilder.Append("7");
                        break;
                    case EventCode.Num8:
                        _ = _bufferStringBuilder.Append("8");
                        break;
                    case EventCode.Num9:
                        _ = _bufferStringBuilder.Append("9");
                        break;
                    case EventCode.Num0:
                        _ = _bufferStringBuilder.Append("0");
                        break;
                    case EventCode.Minus:
                        _ = _bufferStringBuilder.Append("-");
                        break;
                    case EventCode.Equal:
                        _ = _bufferStringBuilder.Append("=");
                        break;
                    case EventCode.Backspace:
                        break;
                    case EventCode.Tab:
                        break;
                    case EventCode.Q:
                        _ = _bufferStringBuilder.Append("Q");
                        break;
                    case EventCode.W:
                        _ = _bufferStringBuilder.Append("W");
                        break;
                    case EventCode.E:
                        _ = _bufferStringBuilder.Append("E");
                        break;
                    case EventCode.R:
                        _ = _bufferStringBuilder.Append("R");
                        break;
                    case EventCode.T:
                        _ = _bufferStringBuilder.Append("T");
                        break;
                    case EventCode.Y:
                        _ = _bufferStringBuilder.Append("Y");
                        break;
                    case EventCode.U:
                        _ = _bufferStringBuilder.Append("U");
                        break;
                    case EventCode.I:
                        _ = _bufferStringBuilder.Append("I");
                        break;
                    case EventCode.O:
                        _ = _bufferStringBuilder.Append("O");
                        break;
                    case EventCode.P:
                        _ = _bufferStringBuilder.Append("P");
                        break;
                    case EventCode.LeftBrace:
                        break;
                    case EventCode.RightBrace:
                        break;
                    case EventCode.Enter:
                        break;
                    case EventCode.LeftCtrl:
                        break;
                    case EventCode.A:
                        _ = _bufferStringBuilder.Append("A");
                        break;
                    case EventCode.S:
                        _ = _bufferStringBuilder.Append("S");
                        break;
                    case EventCode.D:
                        _ = _bufferStringBuilder.Append("D");
                        break;
                    case EventCode.F:
                        _ = _bufferStringBuilder.Append("F");
                        break;
                    case EventCode.G:
                        _ = _bufferStringBuilder.Append("G");
                        break;
                    case EventCode.H:
                        _ = _bufferStringBuilder.Append("H");
                        break;
                    case EventCode.J:
                        _ = _bufferStringBuilder.Append("J");
                        break;
                    case EventCode.K:
                        _ = _bufferStringBuilder.Append("K");
                        break;
                    case EventCode.L:
                        _ = _bufferStringBuilder.Append("L");
                        break;
                    case EventCode.Semicolon:
                        _ = _bufferStringBuilder.Append(";");
                        break;
                    case EventCode.Apostrophe:
                        _ = _bufferStringBuilder.Append("¨");
                        break;
                    case EventCode.Grave:
                        _ = _bufferStringBuilder.Append("'");
                        break;
                    case EventCode.LeftShift:
                        break;
                    case EventCode.Backslash:
                        _ = _bufferStringBuilder.Append("\\");
                        break;
                    case EventCode.Z:
                        _ = _bufferStringBuilder.Append("Z");
                        break;
                    case EventCode.X:
                        _ = _bufferStringBuilder.Append("Z");
                        break;
                    case EventCode.C:
                        _ = _bufferStringBuilder.Append("C");
                        break;
                    case EventCode.V:
                        _ = _bufferStringBuilder.Append("V");
                        break;
                    case EventCode.B:
                        _ = _bufferStringBuilder.Append("B");
                        break;
                    case EventCode.N:
                        _ = _bufferStringBuilder.Append("N");
                        break;
                    case EventCode.M:
                        _ = _bufferStringBuilder.Append("M");
                        break;
                    case EventCode.Comma:
                        _ = _bufferStringBuilder.Append(",");
                        break;
                    case EventCode.Dot:
                        _ = _bufferStringBuilder.Append(".");
                        break;
                    case EventCode.Slash:
                        _ = _bufferStringBuilder.Append("/");
                        break;
                    case EventCode.RightShift:
                        break;
                    case EventCode.KpAsterisk:
                        break;
                    case EventCode.LeftAlt:
                        break;
                    case EventCode.Space:
                        break;
                    case EventCode.Capslock:
                        break;
                    case EventCode.F1:
                        break;
                    case EventCode.Pf2:
                        break;
                    case EventCode.F3:
                        break;
                    case EventCode.F4:
                        break;
                    case EventCode.F5:
                        break;
                    case EventCode.F6:
                        break;
                    case EventCode.F7:
                        break;
                    case EventCode.F8:
                        break;
                    case EventCode.Pf9:
                        break;
                    case EventCode.F10:
                        break;
                    case EventCode.Numlock:
                        break;
                    case EventCode.ScrollLock:
                        break;
                    case EventCode.Kp7:
                        break;
                    case EventCode.Kp8:
                        break;
                    case EventCode.Kp9:
                        break;
                    case EventCode.PkpMinus:
                        break;
                    case EventCode.Kp4:
                        break;
                    case EventCode.Kp5:
                        break;
                    case EventCode.Kp6:
                        break;
                    case EventCode.KpPlus:
                        break;
                    case EventCode.Kp1:
                        break;
                    case EventCode.Kp2:
                        break;
                    case EventCode.Kp3:
                        break;
                    case EventCode.Kp0:
                        break;
                    case EventCode.KpDot:
                        break;
                    case EventCode.Zenkakuhankaku:
                        break;
                    case EventCode.F11:
                        break;
                    case EventCode.F12:
                        break;
                    case EventCode.Ro:
                        break;
                    case EventCode.Katakana:
                        break;
                    case EventCode.Hiragana:
                        break;
                    case EventCode.Henkan:
                        break;
                    case EventCode.Katakanahiragana:
                        break;
                    case EventCode.Muhenkan:
                        break;
                    case EventCode.KpJpComma:
                        break;
                    case EventCode.KpEnter:
                        break;
                    case EventCode.RightCtrl:
                        break;
                    case EventCode.KpSlash:
                        break;
                    case EventCode.SysRq:
                        break;
                    case EventCode.RightAlt:
                        break;
                    case EventCode.LineFeed:
                        break;
                    case EventCode.Home:
                        break;
                    case EventCode.Up:
                        break;
                    case EventCode.Pageup:
                        break;
                    case EventCode.Left:
                        break;
                    case EventCode.Right:
                        break;
                    case EventCode.End:
                        break;
                    case EventCode.Down:
                        break;
                    case EventCode.Pagedown:
                        break;
                    case EventCode.Insert:
                        break;
                    case EventCode.Delete:
                        break;
                    case EventCode.Macro:
                        break;
                    case EventCode.Mute:
                        break;
                    case EventCode.VolumeDown:
                        break;
                    case EventCode.VolumeUp:
                        break;
                    case EventCode.Power:
                        break;
                    case EventCode.KpEqual:
                        break;
                    case EventCode.KpPlusMinus:
                        break;
                    case EventCode.Pause:
                        break;
                    case EventCode.Scale:
                        break;
                    case EventCode.KpComma:
                        break;
                    case EventCode.Hangeul:
                        break;
                    case EventCode.Hanja:
                        break;
                    case EventCode.Yen:
                        break;
                    case EventCode.LeftMeta:
                        break;
                    case EventCode.RightMeta:
                        break;
                    case EventCode.Compose:
                        break;
                    case EventCode.Stop:
                        break;
                    case EventCode.Again:
                        break;
                    case EventCode.Props:
                        break;
                    case EventCode.Undo:
                        break;
                    case EventCode.Front:
                        break;
                    case EventCode.Copy:
                        break;
                    case EventCode.Open:
                        break;
                    case EventCode.Paste:
                        break;
                    case EventCode.Find:
                        break;
                    case EventCode.Cut:
                        break;
                    case EventCode.Help:
                        break;
                    case EventCode.Menu:
                        break;
                    case EventCode.Calc:
                        break;
                    case EventCode.Setup:
                        break;
                    case EventCode.Sleep:
                        break;
                    case EventCode.Wakeup:
                        break;
                    case EventCode.File:
                        break;
                    case EventCode.Sendfile:
                        break;
                    case EventCode.DeleteFile:
                        break;
                    case EventCode.Xfer:
                        break;
                    case EventCode.Prog1:
                        break;
                    case EventCode.Prog2:
                        break;
                    case EventCode.Www:
                        break;
                    case EventCode.MsDos:
                        break;
                    case EventCode.Coffee:
                        break;
                    case EventCode.RotateDisplay:
                        break;
                    case EventCode.CycleWindows:
                        break;
                    case EventCode.Mail:
                        break;
                    case EventCode.Bookmarks:
                        break;
                    case EventCode.Computer:
                        break;
                    case EventCode.Back:
                        break;
                    case EventCode.Forward:
                        break;
                    case EventCode.CloseCd:
                        break;
                    case EventCode.EjectCd:
                        break;
                    case EventCode.EjectCloseCd:
                        break;
                    case EventCode.NextSong:
                        break;
                    case EventCode.PlayPause:
                        break;
                    case EventCode.PreviousSong:
                        break;
                    case EventCode.StopCd:
                        break;
                    case EventCode.Record:
                        break;
                    case EventCode.Rewind:
                        break;
                    case EventCode.Phone:
                        break;
                    case EventCode.Iso:
                        break;
                    case EventCode.Config:
                        break;
                    case EventCode.Homepage:
                        break;
                    case EventCode.Refresh:
                        break;
                    case EventCode.Exit:
                        break;
                    case EventCode.Move:
                        break;
                    case EventCode.Edit:
                        break;
                    case EventCode.ScrollUp:
                        break;
                    case EventCode.ScrollDown:
                        break;
                    case EventCode.KpLeftParen:
                        break;
                    case EventCode.KpRightParen:
                        break;
                    case EventCode.New:
                        break;
                    case EventCode.Redo:
                        break;
                    case EventCode.F13:
                        break;
                    case EventCode.F14:
                        break;
                    case EventCode.F15:
                        break;
                    case EventCode.F16:
                        break;
                    case EventCode.F17:
                        break;
                    case EventCode.F18:
                        break;
                    case EventCode.F19:
                        break;
                    case EventCode.F20:
                        break;
                    case EventCode.F21:
                        break;
                    case EventCode.F22:
                        break;
                    case EventCode.F23:
                        break;
                    case EventCode.F24:
                        break;
                    case EventCode.PlayCd:
                        break;
                    case EventCode.PauseCd:
                        break;
                    case EventCode.Prog3:
                        break;
                    case EventCode.Prog4:
                        break;
                    case EventCode.Dashboard:
                        break;
                    case EventCode.Suspend:
                        break;
                    case EventCode.Close:
                        break;
                    case EventCode.Play:
                        break;
                    case EventCode.FastForward:
                        break;
                    case EventCode.BassBoost:
                        break;
                    case EventCode.Print:
                        break;
                    case EventCode.Hp:
                        break;
                    case EventCode.Camera:
                        break;
                    case EventCode.Sound:
                        break;
                    case EventCode.Question:
                        break;
                    case EventCode.Email:
                        break;
                    case EventCode.Chat:
                        break;
                    case EventCode.Search:
                        break;
                    case EventCode.Connect:
                        break;
                    case EventCode.Finance:
                        break;
                    case EventCode.Sport:
                        break;
                    case EventCode.Shop:
                        break;
                    case EventCode.AltErase:
                        break;
                    case EventCode.Cancel:
                        break;
                    case EventCode.BrightnessDown:
                        break;
                    case EventCode.BrightnessUp:
                        break;
                    case EventCode.Media:
                        break;
                    case EventCode.SwitchVideoMode:
                        break;
                    case EventCode.KbdIllumToggle:
                        break;
                    case EventCode.KbdIllumDown:
                        break;
                    case EventCode.KbdIllumUp:
                        break;
                    case EventCode.Send:
                        break;
                    case EventCode.Reply:
                        break;
                    case EventCode.ForwardMail:
                        break;
                    case EventCode.Save:
                        break;
                    case EventCode.Documents:
                        break;
                    case EventCode.Battery:
                        break;
                    case EventCode.Bluetooth:
                        break;
                    case EventCode.Wlan:
                        break;
                    case EventCode.Uwb:
                        break;
                    case EventCode.Unknown:
                        break;
                    case EventCode.VideoNext:
                        break;
                    case EventCode.VideoPrev:
                        break;
                    case EventCode.BrightnessCycle:
                        break;
                    case EventCode.BrightnessAuto:
                        break;
                    case EventCode.DisplayOff:
                        break;
                    case EventCode.Wwan:
                        break;
                    case EventCode.RfKill:
                        break;
                    case EventCode.MicMute:
                        break;
                    case EventCode.LeftMouse:
                        break;
                    case EventCode.RightMouse:
                        break;
                    case EventCode.MiddleMouse:
                        break;
                    case EventCode.MouseBack:
                        break;
                    case EventCode.MouseForward:
                        break;
                    case EventCode.ToolFinger:
                        break;
                    case EventCode.ToolQuintTap:
                        break;
                    case EventCode.Touch:
                        break;
                    case EventCode.ToolDoubleTap:
                        break;
                    case EventCode.ToolTripleTap:
                        break;
                    case EventCode.ToolQuadTap:
                        break;
                    case EventCode.Mic:
                        break;
                    default:
                        break;
                }
                if (c == EventCode.Enter)
                {
                    var currentData = _bufferStringBuilder.ToString();
                    _ = _bufferStringBuilder.Clear();
                    var e = new KeyPressEvent(c, s, currentData);
                    OnKeyPress?.Invoke(e);
                }
            }
        }

        public void Dispose()
        {
            _disposing = true;
            _stream.Dispose();
            _stream = null;
        }
    }
}
