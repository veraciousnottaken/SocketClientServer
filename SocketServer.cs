using System;
using System.Collections;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;

namespace SocketClientServer
{
    public class SocketServer : SocketClientServerBase
    {
        // The following variable will keep track of the cumulative
        // total number of clients connected at any time. Since multiple threads
        // can access this variable, modifying this variable should be done
        // in a thread safe manner
        public int ClientCount = 0;

        public ClientConnectCallbackDelegate OnClientConnectCallback = null;

        public ClientDisconnectCallbackDelegate OnClientDisconnectCallback = null;

        public DataReceivedCallbackDelegate OnDataReceivedCallback;

        public StartCallbackDelegate OnStartCallback;

        public StopCallbackDelegate OnStopCallback;

        // An ArrayList is used to keep track of worker sockets that are designed
        // to communicate with each connected client. Make it a synchronized ArrayList
        // For thread safety
        private readonly ArrayList _mWorkerSocketList = ArrayList.Synchronized(new ArrayList());

        private bool _isStarted;

        private Socket _mMainSocket;

        private AsyncCallback _workerCallBack;

        public delegate void ClientConnectCallbackDelegate(ArrayList list);

        public delegate void ClientDisconnectCallbackDelegate(ArrayList list);

        public delegate void DataReceivedCallbackDelegate(SocketPacket socketPacket);

        public delegate void StartCallbackDelegate(SocketPacket socketPacket);

        public delegate void StopCallbackDelegate(ArrayList list);

        public bool IsStarted { get { return _mMainSocket != null && _isStarted; } }

        public void Broadcast(string msg)
        {
            try
            {
                for (var i = 0; i < _mWorkerSocketList.Count; i++)
                {
                    Send(msg, i);
                }
            }
            catch (SocketException e)
            {
                HandleException("Broadcast", e);
            }
        }

        public NetworkInterface[] GetAllNetworkInterfaces()
        {
            return NetworkInterface.GetAllNetworkInterfaces();
        }

        public string GetPacketAsString(SocketPacket socketPacket)
        {
            int iRx = socketPacket.dataBuffer.GetLength(0);

            char[] chars = new char[iRx + 1];

            // Extract the characters as a buffer
            System.Text.Decoder d = System.Text.Encoding.UTF8.GetDecoder();
            int charLen = d.GetChars(socketPacket.dataBuffer, 0, iRx, chars, 0);

            var szData = new String(chars);
            return szData;
        }

        public IPHostEntry GetServerIp()
        {
            String strHostName = Dns.GetHostName();

            // Find host by name
            return Dns.GetHostEntry(strHostName);
        }

        public void Send(string msg, int client)
        {
            if (client > _mWorkerSocketList.Count)
            {
                HandleException("Unknown client", new ApplicationException());
                return;
            }

            Socket workerSocket = (Socket)_mWorkerSocketList[client];

            if (workerSocket != null && workerSocket.Connected)
            {
                // Convert the reply to byte array
                byte[] byData = System.Text.Encoding.ASCII.GetBytes(msg);
                workerSocket.Send(byData);
            }
            else
            {
                HandleException("Client not connected", new ApplicationException());
            }
        }

        public void Start(int port, int backlog = 4)
        {
            try
            {
                // Create the listening socket...
                _mMainSocket = new Socket(AddressFamily.InterNetwork,
                    SocketType.Stream,
                    ProtocolType.Tcp);

                IPEndPoint ipLocal = new IPEndPoint(IPAddress.Any, port);

                // Bind to local IP Address...
                _mMainSocket.Bind(ipLocal);

                // Start listening...
                _mMainSocket.Listen(backlog);

                // Create the call back for any client connections...
                _mMainSocket.BeginAccept(new AsyncCallback(OnClientConnect), null);

                _isStarted = true;
            }
            catch (SocketException e)
            {
                HandleException("Start", e);
            }
            finally
            {
                if (OnStartCallback != null)
                {
                    OnStartCallback(new SocketPacket(_mMainSocket, -1));
                }
            }
        }

        public void Stop()
        {
            try
            {
                if (_mMainSocket != null)
                {
                    _mMainSocket.Close();
                }

                _isStarted = false;
                while (_mWorkerSocketList.Count > 0)
                {
                    Socket workerSocket = (Socket)_mWorkerSocketList[0];

                    if (workerSocket != null)
                    {
                        workerSocket.Close();
                        workerSocket = null;
                    }

                    _mWorkerSocketList.RemoveAt(0);
                }
            }
            finally
            {
                if (OnStopCallback != null)
                {
                    OnStopCallback(_mWorkerSocketList);
                }
            }
        }

        // This is the call back function, which will be invoked when a client is connected
        private void OnClientConnect(IAsyncResult asyn)
        {
            try
            {
                // Here we complete/end the BeginAccept() asynchronous call
                // by calling EndAccept() - which returns the reference to
                // a new Socket object
                Socket workerSocket = _mMainSocket.EndAccept(asyn);

                // Now increment the client count for this client
                // in a thread safe manner
                Interlocked.Increment(ref ClientCount);

                // Add the workerSocket reference to our ArrayList
                _mWorkerSocketList.Add(workerSocket);

                if (OnClientConnectCallback != null)
                {
                    OnClientConnectCallback(_mWorkerSocketList);
                }

                // Let the worker Socket do the further processing for the
                // just connected client
                WaitForData(workerSocket, ClientCount);

                // Since the main Socket is now free, it can go back and wait for
                // other clients who are attempting to connect
                _mMainSocket.BeginAccept(new AsyncCallback(OnClientConnect), null);
            }
            catch (ObjectDisposedException e)
            {
                HandleException("OnClientConnection: Socket has been closed", e);
            }
            catch (SocketException e)
            {
                HandleException("On connect Socket Exception", e);
            }
        }

        // This the call back function which will be invoked when the socket
        // detects any client writing of data on the stream
        private void OnDataReceived(IAsyncResult asyn)
        {
            SocketPacket socketData = (SocketPacket)asyn.AsyncState;
            try
            {
                // Complete the BeginReceive() asynchronous call by EndReceive() method
                // which will return the number of characters written to the stream
                // by the client
                socketData.m_currentSocket.EndReceive(asyn);

                // Send back the reply to the client
                //string replyMsg = "Server Reply:" + szData.ToUpper();

                // Convert the reply to byte array
                //byte[] byData = System.Text.Encoding.ASCII.GetBytes(replyMsg);

                //Socket workerSocket = (Socket)socketData.m_currentSocket;
                //workerSocket.Send(byData);

                if (OnDataReceivedCallback != null)
                {
                    OnDataReceivedCallback(socketData);
                }

                // Continue the waiting for data on the Socket
                WaitForData(socketData.m_currentSocket, socketData.m_clientNumber);
            }
            catch (ObjectDisposedException e)
            {
                //System.Diagnostics.Debugger.Log(0, "1", "\nOnDataReceived: Socket has been closed\n");
                HandleException("nOnDataReceived: Socket has been closed", e);
            }
            catch (SocketException e)
            {
                if (e.ErrorCode == 10054) // Error code for Connection reset by peer
                {
                    var msg = "Client " + socketData.m_clientNumber + " Disconnected" + "\n";

                    //AppendToRichEditControl(msg);
                    HandleException(msg, e);

                    // Remove the reference to the worker socket of the closed client
                    // so that this object will get garbage collected
                    _mWorkerSocketList[socketData.m_clientNumber - 1] = null;

                    if (OnClientDisconnectCallback != null)
                    {
                        OnClientDisconnectCallback(_mWorkerSocketList);
                    }
                }
                else
                {
                    HandleException("Socket exception", e);
                }
            }
        }

        // Start waiting for data from the client
        private void WaitForData(Socket soc, int clientNumber)
        {
            try
            {
                if (_workerCallBack == null)
                {
                    // Specify the call back function which is to be
                    // invoked when there is any write activity by the
                    // connected client
                    _workerCallBack = new AsyncCallback(OnDataReceived);
                }
                SocketPacket theSocPkt = new SocketPacket(soc, clientNumber);

                soc.BeginReceive(theSocPkt.dataBuffer, 0,
                    theSocPkt.dataBuffer.Length,
                    SocketFlags.None,
                    _workerCallBack,
                    theSocPkt);
            }
            catch (SocketException e)
            {
                HandleException("Wait for data", e);
            }
        }

        public class SocketPacket
        {
            // Buffer to store the data sent by the client
            public byte[] dataBuffer = new byte[1024];

            public int m_clientNumber;

            public System.Net.Sockets.Socket m_currentSocket;

            // Constructor which takes a Socket and a client number
            public SocketPacket(System.Net.Sockets.Socket socket, int clientNumber)
            {
                m_currentSocket = socket;
                m_clientNumber = clientNumber;
            }
        }
    }
}