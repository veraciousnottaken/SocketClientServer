using System;
using Mes.Core.Common.Log.Services.Logging;
//using NLog;

namespace SocketClientServer
{
    public class SocketClientServerBase
    {
        //public Logger logger = LogManager.GetCurrentClassLogger();
        public SocketClient.ErrorCallbackDelegate OnErrorCallback = null;
        public ILogger logger = LogFactory.Logger();

        public void HandleException(string message, Exception exception)
        {
            //logger.ErrorException(message, exception);
            logger.Error(message, exception);

            if (OnErrorCallback != null)
            {
                OnErrorCallback(exception);
            }
        }
    }
}