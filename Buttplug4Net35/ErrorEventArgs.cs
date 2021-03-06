﻿using System;
using Buttplug4Net35.Messages;

namespace Buttplug4Net35
{
    /// <summary>
    /// Event wrapper for a Buttplug Error message. Used when the client recieves an unhandled error,
    /// or an exception is thrown.
    /// </summary>
    public class ErrorEventArgs : EventArgs
    {
        /// <summary>
        /// The Buttplug Error message.
        /// </summary>
        public readonly Error Message;

        /// <summary>
        /// The exception raised.
        /// </summary>
        public readonly Exception Exception;

        /// <summary>
        /// Initializes a new instance of the <see cref="ErrorEventArgs"/> class, based on an Error message.
        /// </summary>
        /// <param name="aMsg">The Buttplug Error message.</param>
        public ErrorEventArgs(Error aMsg)
        {
            Message = aMsg;
            Exception = null;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ErrorEventArgs"/> class, based on the
        /// exception being raised.
        /// </summary>
        /// <param name="aException">The caught exception.</param>
        public ErrorEventArgs(Exception aException)
        {
            Exception = aException;
            Message = new Error(Exception.Message, Error.ErrorClass.ERROR_UNKNOWN, ButtplugConsts.SystemMsgId);
        }
    }
}