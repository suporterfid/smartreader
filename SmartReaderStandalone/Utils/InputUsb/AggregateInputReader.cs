﻿namespace SmartReaderStandalone.Utils.InputUsb
{
    public class AggregateInputReader : IDisposable
    {
        private List<InputReader> _readers = new();

        public event InputReader.RaiseKeyPress OnKeyPress;

        public AggregateInputReader(string filename)
        {
            if(string.IsNullOrEmpty(filename))
            {
                var files = Directory.GetFiles("/dev/input/", "event*");

                foreach (var file in files)
                {
                    var reader = new InputReader(file);

                    reader.OnKeyPress += ReaderOnOnKeyPress;

                    _readers.Add(reader);
                }
            }
            else
            {
                var reader = new InputReader(filename);

                reader.OnKeyPress += ReaderOnOnKeyPress;

                _readers.Add(reader);
            }
            
        }

        private void ReaderOnOnKeyPress(KeyPressEvent e)
        {
            OnKeyPress?.Invoke(e);
        }

        public void Dispose()
        {
            foreach (var d in _readers)
            {
                d.OnKeyPress -= ReaderOnOnKeyPress;
                d.Dispose();
            }

            _readers = null;
        }
    }
}
