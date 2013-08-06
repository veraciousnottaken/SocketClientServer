using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Timers;

namespace SocketClientServer
{
    public class SocketClient : SocketClientServerBase
    {
        public bool Connected { get { return Socket != null && Socket.Connected; } }

        public Socket Socket { get; private set; }

        public int ConnectTimeout { get; private set; }

        public bool Reconnect
        {
            get { return _reconnect; }
            set
            {
                _reconnect = value;

                if (_reconnect)
                {
                    _reconnectTimer = new Timer(ReconnectTimeout);
                    _reconnectTimer.AutoReset = true;
                    _reconnectTimer.Elapsed += _reconnectTimer_Elapsed;
                    _reconnectTimer.Start();
                }
                else
                {
                    _reconnectTimer = null;
                }
            }
        }

        private void _reconnectTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            lock (_reconnectTimer)
            {
                if (Reconnect && Socket != null && !Socket.IsSocketConnected())
                {
                    Open(_addresss, _port);
                }
            }
        }

        private int _reconnectTimeout = 10000;

        public int ReconnectTimeout
        {
            get { return _reconnectTimeout; }
            set
            {
                _reconnectTimeout = value;
                if (_reconnectTimer != null) _reconnectTimer.Interval = value;
            }
        }

        private IAsyncResult m_result;
        private AsyncCallback m_pfnCallBack;
        private int _timeout = 0, _rtc = 0;
        private Timer _timer;

        //private string _sendFrame = string.Empty;
        private long _InCount = 0, _OutCount = 0;

        public delegate void ReadCallbackDelegate(object parameter);

        public delegate void OpenCallbackDelegate(bool state);

        public delegate void CloseCallbackDelegate(bool state);

        public delegate void ErrorCallbackDelegate(Exception e);

        public ReadCallbackDelegate OnReadCallback = null;
        public OpenCallbackDelegate OnOpenCallback = null;
        public CloseCallbackDelegate OnCloseCallback = null;
        private bool _reconnect = false;
        private Timer _reconnectTimer;
        private string _addresss;
        private int _port;

        public SocketClient()
        {
            _timer = new Timer(1000);
            _timer.Elapsed += new ElapsedEventHandler(_timer_Elapsed);
            _timer.Stop();
        }

        private void _timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            ConnectTimeout++;
        }

        public void Send(string frame)
        {
            try
            {
                // New code to send strings
                /*NetworkStream networkStream = new NetworkStream(m_clientSocket);
                System.IO.StreamWriter streamWriter = new System.IO.StreamWriter(networkStream);
                streamWriter.WriteLine(msg);
                streamWriter.Flush();*/

                // send bytes
                byte[] byData = System.Text.Encoding.ASCII.GetBytes(frame);

                if (Socket != null)
                {
                    Socket.Send(byData);
                    _OutCount += frame.Length;
                }
            }
            catch (SocketException se)
            {
                HandleException("Send", se);
            }
        }

        public void Open(string address, int port)
        {
            _addresss = address;
            _port = port;

            Task.Factory
                .StartNew(() =>
                {
                    if (Socket != null && Socket.Connected)
                    {
                        _timer.Stop();
                        ConnectTimeout = 0;
                        return;
                    }

                    try
                    {
                        _timer.Start();

                        // Create the socket instance
                        Socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

                        // Cet the remote IP address
                        IPAddress ip = IPAddress.Parse(address);
                        int iPortNo = System.Convert.ToInt16(port);

                        logger.Info(string.Format("Connecting to {0}:{1}", ip.ToString(), iPortNo));

                        // Create the end point
                        var ipEnd = new IPEndPoint(ip, iPortNo);

                        // Connect to the remote host
                        Socket.Connect(ipEnd);

                        if (Socket.Connected)
                        {
                            if (OnOpenCallback != null)
                            {
                                OnOpenCallback(true);
                            }

                            //Wait for data asynchronously
                            WaitForData();
                        }
                    }
                    catch (Exception e)
                    {
                        HandleException("Open", e);
                    }
                });
        }

        public class SocketPacket
        {
            public System.Net.Sockets.Socket thisSocket;
            public byte[] dataBuffer = new byte[1024];
        }

        private void WaitForData()
        {
            try
            {
                if (m_pfnCallBack == null)
                {
                    m_pfnCallBack = new AsyncCallback(OnDataReceived);
                }
                SocketPacket theSocPkt = new SocketPacket();
                theSocPkt.thisSocket = Socket;

                // Start listening to the data asynchronously
                if (Socket != null)
                    m_result = Socket.BeginReceive(theSocPkt.dataBuffer,
                                                           0, theSocPkt.dataBuffer.Length,
                                                           SocketFlags.None,
                                                           m_pfnCallBack,
                                                           theSocPkt);
            }
            catch (SocketException se)
            {
                HandleException("Wait for data", se);
            }
        }

        private void OnDataReceived(IAsyncResult asyn)
        {
            try
            {
                SocketPacket theSockId = (SocketPacket)asyn.AsyncState;

                if (!theSockId.thisSocket.Connected)
                    return;

                var iRx = theSockId.thisSocket.EndReceive(asyn);
                var chars = new char[iRx + 1];
                var d = System.Text.Encoding.UTF8.GetDecoder();
                var charLen = d.GetChars(theSockId.dataBuffer, 0, iRx, chars, 0);
                var szData = new System.String(chars);

                _InCount += szData.Length;

                if (OnReadCallback != null)
                {
                    OnReadCallback(szData);
                }

                WaitForData();
            }
            catch (ObjectDisposedException e)
            {
                HandleException("On data received object disposed", e);
            }
            catch (SocketException e)
            {
                HandleException("On data received socket exception", e);
            }
        }

        public void Close()
        {
            if (Socket != null)
            {
                Socket.Close();
                Socket = null;

                if (OnCloseCallback != null)
                {
                    OnCloseCallback(false);
                }
            }
        }
    }
}