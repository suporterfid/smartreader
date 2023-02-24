using Renci.SshNet;

namespace SmartReaderStandalone.Utils
{
    public class RShellUtil
    {
        private string _hostAddress;

        private string _username;

        private string _password;

        private SshClient sshClient;
        public RShellUtil(string hostAddress, string username, string password)
        {
            _hostAddress = hostAddress;
            _username = username;
            _password = password;

            sshClient = new SshClient(_hostAddress, _username, _password);
            
            sshClient.HostKeyReceived += (sender, e) =>
            {
                e.CanTrust = true;
            };

            sshClient.Connect();
            
        }

        public string SendCommand(string command)
        {
            string result = "";
            if(sshClient.IsConnected)
            {
               var sshCommand = sshClient.RunCommand(command);
               result = sshCommand.Execute();
            }
            return result;
        }

        public void Disconnect()
        {
            sshClient.Disconnect();
        }
    }
}
