#region copyright
//****************************************************************************************************
// Copyright ©2023 Impinj, Inc.All rights reserved.              
//                                    
// You may use and modify this code under the terms of the Impinj Software Tools License & Disclaimer. 
// Visit https://support.impinj.com/hc/en-us/articles/360000468370-Software-Tools-License-Disclaimer   
// for full license details, or contact Impinj, Inc.at support@impinj.com for a copy of the license.   
//
//****************************************************************************************************
#endregion
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SmartReaderStandalone.Utils;

public class UDPSocket
{
    private const int bufSize = 8 * 1024;
    public readonly ConcurrentDictionary<string, int> _udpClients = new();
    private readonly State state = new();
    public Socket _socket;
    private EndPoint epFrom = new IPEndPoint(IPAddress.Any, 0);
    private AsyncCallback recv;

    public void Server(string address, int port)
    {
        Console.WriteLine("UDP Socket listening on " + port);
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.ReuseAddress, true);
        _socket.Bind(new IPEndPoint(IPAddress.Parse(address), port));
        Receive();
    }

    public void Client(string address, int port)
    {
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _socket.Connect(IPAddress.Parse(address), port);
        Receive();
    }

    public void Send(string text)
    {
        Console.WriteLine("Trying to send data to UDP...");


        try
        {
            if (_socket != null && _socket.Connected)
            {
                //var clientData = client.Split(":");
                var data = Encoding.ASCII.GetBytes(text);
                _socket.BeginSend(data, 0, data.Length, SocketFlags.None, ar =>
                {
                    var so = (State)ar.AsyncState;
                    var bytes = _socket.EndSend(ar);
                    var remoteEndPoint = "";
                    if (_socket.RemoteEndPoint != null) remoteEndPoint = _socket.RemoteEndPoint.ToString();
                    Console.WriteLine("SEND: {0} - {1}, {2}", remoteEndPoint, bytes, text);
                }, state);
            }
        }
        catch (Exception)
        {
        }
        //foreach (KeyValuePair<string, int> udpClient in _udpClients)
        //{
        //    try
        //    {


        //    }
        //    catch (Exception ex)
        //    {
        //        //Log.Error(ex, "Unexpected error on ProcessUdpDataTagEventDataAsync for UDP");

        //        //_messageQueueTagSmartReaderTagEventSocketServerRetry.Enqueue(smartReaderTagEventData);
        //    }
        //}
    }

    public void Listen()
    {
        while (true)
        {
            Receive();
            Thread.Sleep(100);
        }
    }

    public void Close()
    {
        try
        {
            if (_socket != null &&
                _socket.Connected) _socket.Close(); //Fixed closing bug (System.ObjectDisposedException)
            //Bugfix allows to relaunch server
        }
        catch (Exception)
        {
        }
    }

    private void Receive()
    {
        _socket.BeginReceiveFrom(state.buffer, 0, bufSize, SocketFlags.None, ref epFrom, recv = ar =>
        {
            try
            {
                var so = (State)ar.AsyncState;
                var bytes = _socket.EndReceiveFrom(ar, ref epFrom);

                _socket.BeginReceiveFrom(so.buffer, 0, bufSize, SocketFlags.None, ref epFrom, recv, so);
                Console.WriteLine("RECV: {0}: {1}, {2}", epFrom, bytes, Encoding.ASCII.GetString(so.buffer, 0, bytes));

                try
                {
                    var endPoint = epFrom.ToString().Split(':');
                    Console.WriteLine("endPoint: {0}, {1}", endPoint[0], endPoint[1]);
                    if (!_udpClients.ContainsKey(endPoint[0]))
                        _udpClients.TryAdd(endPoint[0], int.Parse(endPoint[1]));
                    else
                        _udpClients[endPoint[0]] = int.Parse(endPoint[1]);
                }
                catch (Exception)
                {
                }
            }
            catch
            {
            }
        }, state);
    }

    public class State
    {
        public byte[] buffer = new byte[bufSize];
    }
}