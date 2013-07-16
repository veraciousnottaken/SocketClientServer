using System;
using NLog;

namespace SocketClientServer
{
    public class SocketClientServerBase
    {
        public Logger logger = LogManager.GetCurrentClassLogger();
        public SocketClient.ErrorCallbackDelegate OnErrorCallback = null;

        public void HandleException(string message, Exception exception)
        {
            logger.ErrorException(message, exception);

            if (OnErrorCallback != null)
            {
                OnErrorCallback(exception);
            }
        }
    }
}