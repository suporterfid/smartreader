using System.Net.Sockets;
using System.Net;
using System.Text;

namespace SmartReaderStandalone.Utils
{
    public class SmartreaderSocketClient
    {
        // Define a delegate type for the data received event
        public delegate void DataReceivedHandler(string data);


        // Define an event based on the delegate
        public event DataReceivedHandler DataReceived;

        // Property to get the connection status
        public bool IsConnected { get; private set; }

        // Method to connect to the server and receive data
        public void ConnectAndReceiveData(string ipAddress, int port, int maxRetryAttempts = 3, int retryDelayMilliseconds = 1000)
        {
            int retryCount = 0;

            while (retryCount < maxRetryAttempts)
            {
                try
                {
                    // Create a TCP client socket
                    using (var clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                    {
                        // Connect to the server
                        clientSocket.Connect(new IPEndPoint(IPAddress.Parse(ipAddress), port));

                        // If the connection is successful, set the IsConnected property to true
                        IsConnected = true;

                        // Socket communication
                        byte[] buffer = new byte[1024];
                        int bytesRead;

                        // Send a request to the server
                        //string request = "GET /api/data HTTP/1.1\r\nHost: localhost\r\n\r\n";
                        //byte[] requestData = Encoding.ASCII.GetBytes(request);
                        //clientSocket.Send(requestData);

                        // Receive response from the server and raise the event
                        string response = string.Empty;
                        while ((bytesRead = clientSocket.Receive(buffer)) > 0)
                        {
                            response += Encoding.ASCII.GetString(buffer, 0, bytesRead);
                        }

                        // Raise the event to pass the response data to the subscriber
                        DataReceived?.Invoke(response);
                        //return;
                    }
                }
                catch (SocketException ex)
                {
                    IsConnected = false;
                    // Handle socket-specific exceptions
                    Console.WriteLine($"Socket Exception: {ex.Message}");
                }
                catch (Exception ex)
                {
                    IsConnected = false;
                    // Handle other general exceptions
                    Console.WriteLine($"Exception: {ex.Message}");
                }

                // Retry after the specified delay
                Thread.Sleep(retryDelayMilliseconds);
                retryCount++;
            }
            IsConnected = false;
            // If max retry attempts reached, notify the caller that the connection failed.
            Console.WriteLine("Connection failed after multiple retry attempts.");
        }
    }
}
